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

    public ViewContactsResult GetContactGraph(int viewId, ContactOptions? options = null)
    {
        options ??= new ContactOptions();

        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            return Unavailable(viewId, "no drawing is open");

        var view = FindView(drawing, viewId);
        if (view == null)
            return Unavailable(viewId, $"view {viewId} is not on the active drawing");

        return Build(viewId, VisibleModelIds(view), _solidGeometry, options);
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
        var solids = new List<ISolid>();
        var unread = new List<UnreadPart>();

        foreach (var modelId in modelIds)
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

        return new ViewContactsResult(viewId, ContactGraph.Build(solids, options), unread);
    }

    private static View? FindView(Tekla.Structures.Drawing.Drawing drawing, int viewId)
    {
        var views = drawing.GetSheet().GetViews();
        while (views.MoveNext())
        {
            if (views.Current is View candidate && candidate.GetIdentifier().ID == viewId)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Model ids of the parts this view actually draws, each once. Parts hidden in the
    /// view are left out: they are not on the sheet, and junctions to them would describe
    /// something nobody can see.
    /// </summary>
    private static IEnumerable<int> VisibleModelIds(View view)
    {
        var seen = new HashSet<int>();

        var objects = view.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingPart drawingPart)
                continue;

            if (IsHidden(drawingPart))
                continue;

            var id = drawingPart.ModelIdentifier.ID;
            if (seen.Add(id))
                yield return id;
        }
    }

    private static bool IsHidden(DrawingPart drawingPart)
    {
        try
        {
            return drawingPart.Hideable.IsHidden;
        }
        catch
        {
            // A part that will not say counts as drawn: leaving it out would silently drop
            // junctions, while keeping it only costs a search.
            return false;
        }
    }
}
