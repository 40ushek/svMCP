using System;
using System.Collections.Generic;
using SolidContacts;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Contacts between the parts of a drawing view.
///
/// Asked of the view rather than of a part, because a contact is a property of a set: to
/// find one you need both bodies, and the per-part reader only ever has one. The view is
/// also the right boundary for the answer - a section through a layered wall shows one
/// layer, and reaching past it to the assembly would report junctions that appear nowhere
/// in it.
///
/// Nothing is cached. The obvious key - the view and its coordinate system - says nothing
/// about whether the model changed underneath, and a stale contact is not a slow answer
/// but a wrong one that dimensioning would act on. If the cost ever shows up in a
/// measurement, a key that includes the tolerances and some revision of the geometry can
/// be built then.
/// </summary>
public sealed class TeklaDrawingViewContactApi : IDrawingViewContactApi
{
    private readonly Model _model;
    private readonly IDrawingPartSolidGeometryApi _solidGeometry;

    public TeklaDrawingViewContactApi(Model model, IDrawingPartSolidGeometryApi? solidGeometry = null)
    {
        _model = model;
        _solidGeometry = solidGeometry ?? new TeklaDrawingPartSolidGeometryApi(model);
    }

    public ViewContactsResult GetContactGraph(int viewId, ContactOptions? options = null) =>
        GetContactGraph(viewId, options, beforeSolidRead: null);

    /// <summary>
    /// As <see cref="GetContactGraph(int, ContactOptions?)"/>, while exposing the exact view
    /// selection before any solid is read. The bridge uses it to name a measurement's source
    /// without reconstructing that selection from a partial graph afterwards.
    /// </summary>
    /// <param name="modelIds">
    /// Narrows the search to these parts when given, instead of everything the view draws.
    /// Answering "does A touch B" this way was added after a caller had to grep an
    /// unfiltered whole-view result by hand to find one pair, and mixed up a coordinate from
    /// a different part in the process - a slow search is a nuisance, a wrong answer copied
    /// out of one is a defect in whatever the caller builds next.
    /// </param>
    public ViewContactsResult GetContactGraph(
        int viewId,
        ContactOptions? options,
        Action<IReadOnlyList<int>>? beforeSolidRead,
        IReadOnlyCollection<int>? modelIds = null)
    {
        // Guarded here, not only by whichever caller happens to check first: one body can
        // never form a pair, so a single-id filter would always come back empty regardless
        // of what that part actually touches - a query with no honest answer. Checked before
        // anything Tekla-side, the same way a bad viewId would be, so every caller of this
        // API gets the same protection the bridge command's own check already gave it.
        if (RejectSingleIdFilter(viewId, modelIds) is { } rejected)
            return rejected;

        options ??= new ContactOptions();

        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            return Unavailable(viewId, "no drawing is open");

        var view = DrawingViewParts.FindView(drawing, viewId);
        if (view == null)
            return Unavailable(viewId, $"view {viewId} is not on the active drawing");

        var selected = DrawingViewParts.GetDepthFilteredParts(_model, view);
        var visible = selected.ModelIds;
        var wanted = modelIds == null ? visible : visible.Where(modelIds.Contains).ToList();
        var notVisible = modelIds == null
            ? Array.Empty<int>()
            : modelIds.Where(id => !selected.CandidateModelIds.Contains(id)).ToArray();
        var outsideDepth = modelIds == null
            ? Array.Empty<int>()
            : selected.OutsideDepthModelIds.Where(modelIds.Contains).ToArray();
        var unresolvedDepth = modelIds == null
            ? Array.Empty<int>()
            : selected.Incomplete.Select(part => part.ModelId).Where(modelIds.Contains).Distinct().ToArray();
        var selectionUnread = modelIds == null
            ? selected.Incomplete
            : selected.Incomplete.Where(part => modelIds.Contains(part.ModelId));

        beforeSolidRead?.Invoke(wanted);
        return Build(
            viewId, wanted, _solidGeometry, options,
            restricted: modelIds != null,
            notVisibleRequestedIds: notVisible,
            outsideDepthRequestedIds: outsideDepth,
            unresolvedDepthRequestedIds: unresolvedDepth,
            selectionUnread: selectionUnread.ToList());
    }

    /// <summary>
    /// No view, so no search. Reported as incomplete rather than as an empty view: an
    /// empty view is a finding, and something downstream would be entitled to conclude
    /// from it that nothing there touches anything.
    /// </summary>
    private static ViewContactsResult Unavailable(int viewId, string reason) =>
        new(viewId,
            ContactGraph.Build(Array.Empty<ISolid>()),
            Array.Empty<UnreadPart>(),
            reason);

    /// <summary>
    /// Null when the filter is usable, an error result when it is not. Kept apart from
    /// <see cref="GetContactGraph(int, ContactOptions?, Action{IReadOnlyList{int}}?, IReadOnlyCollection{int}?)"/>
    /// so the guard is testable on its own - that method needs a live view to run at all,
    /// and this check must not depend on one to be trusted.
    ///
    /// Counts distinct ids, not <see cref="IReadOnlyCollection{T}.Count"/>: <c>[10, 10]</c>
    /// has two elements and one part. Left as a raw count, it would pass this guard, reach a
    /// live search that reads the same part twice, and throw inside
    /// <see cref="ContactGraph.Build"/> - `SolidContacts` refuses two bodies sharing an id -
    /// which is a worse failure than the one this guard exists to give a clean answer for.
    /// </summary>
    internal static ViewContactsResult? RejectSingleIdFilter(int viewId, IReadOnlyCollection<int>? modelIds) =>
        modelIds != null && modelIds.Distinct().Take(2).Count() < 2
            ? Unavailable(
                viewId,
                "modelIds needs at least two distinct parts - one part, named once or " +
                "repeated, can never form a pair, so the search would always come back " +
                "empty regardless of what that part touches")
            : null;

    /// <summary>
    /// The part of the work that needs no Tekla: read each named part, take in what came
    /// back, and account for what did not. Separated so it can be tested without a model
    /// open, which is where the reporting of unread parts actually gets checked.
    /// </summary>
    internal static ViewContactsResult Build(
        int viewId,
        IEnumerable<int> modelIds,
        IDrawingPartSolidGeometryApi solidGeometry,
        ContactOptions options,
        bool restricted = false,
        IReadOnlyList<int>? notVisibleRequestedIds = null,
        IReadOnlyList<int>? outsideDepthRequestedIds = null,
        IReadOnlyList<int>? unresolvedDepthRequestedIds = null,
        IReadOnlyList<UnreadPart>? selectionUnread = null)
    {
        var requestedIds = modelIds.ToList();
        var solids = new List<ISolid>();
        var unread = selectionUnread?.ToList() ?? new List<UnreadPart>();

        foreach (var modelId in requestedIds)
        {
            PartSolidGeometryInViewResult geometry;
            try
            {
                geometry = solidGeometry.GetPartSolidGeometryInView(viewId, modelId);
            }
            catch (Exception exception)
            {
                unread.Add(new UnreadPart(modelId, exception.Message));
                continue;
            }

            if (!geometry.Success)
            {
                unread.Add(new UnreadPart(modelId, geometry.Error ?? "geometry read failed"));
                continue;
            }

            var solid = ViewSolidAdapter.FromGeometry(geometry);
            if (solid == null)
            {
                unread.Add(new UnreadPart(modelId, "no usable faces after translation"));
                continue;
            }

            if (solid.DroppedFaces > 0)
                unread.Add(new UnreadPart(modelId, $"{solid.DroppedFaces} face(s) dropped, body searched anyway"));

            solids.Add(solid);
        }

        return new ViewContactsResult(
            viewId, ContactGraph.Build(solids, options), unread, requestedIds: requestedIds,
            restricted: restricted,
            notVisibleRequestedIds: notVisibleRequestedIds,
            outsideDepthRequestedIds: outsideDepthRequestedIds,
            unresolvedDepthRequestedIds: unresolvedDepthRequestedIds);
    }

}
