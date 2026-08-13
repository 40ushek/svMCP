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
    public ViewContactsResult GetContactGraph(
        int viewId,
        ContactOptions? options,
        Action<IReadOnlyList<int>>? beforeSolidRead)
    {
        options ??= new ContactOptions();

        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            return Unavailable(viewId, "no drawing is open");

        var view = DrawingViewParts.FindView(drawing, viewId);
        if (view == null)
            return Unavailable(viewId, $"view {viewId} is not on the active drawing");

        var requestedIds = DrawingViewParts.VisibleModelIds(view).ToList();
        beforeSolidRead?.Invoke(requestedIds);
        return Build(viewId, requestedIds, _solidGeometry, options);
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
    /// The part of the work that needs no Tekla: read each named part, take in what came
    /// back, and account for what did not. Separated so it can be tested without a model
    /// open, which is where the reporting of unread parts actually gets checked.
    /// </summary>
    internal static ViewContactsResult Build(
        int viewId,
        IEnumerable<int> modelIds,
        IDrawingPartSolidGeometryApi solidGeometry,
        ContactOptions options)
    {
        var requestedIds = modelIds.ToList();
        var solids = new List<ISolid>();
        var unread = new List<UnreadPart>();

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
            viewId, ContactGraph.Build(solids, options), unread, requestedIds: requestedIds);
    }

}
