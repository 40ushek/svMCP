using System;
using System.Collections.Generic;
using Clipper2Lib;
using SolidContacts;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Reads each visible solid in the drawing view's coordinate system, then delegates both
/// polygon unions to <see cref="ProjectedOutlineBuilder"/>. Tekla is deliberately only a
/// geometry source here; Clipper2 owns the 2D contour model.
/// </summary>
public sealed class TeklaDrawingAssemblyOutlineApi : IDrawingViewOutlineApi
{
    private readonly IDrawingPartSolidGeometryApi _solidGeometry;

    public TeklaDrawingAssemblyOutlineApi(Model model, IDrawingPartSolidGeometryApi? solidGeometry = null)
    {
        _solidGeometry = solidGeometry ?? new TeklaDrawingPartSolidGeometryApi(model);
    }

    public ViewAssemblyOutlineResult GetAssemblyOutline(
        int viewId,
        OutlineOptions? options = null,
        IReadOnlyCollection<int>? modelIds = null)
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            return Unavailable(viewId, "no drawing is open");

        var view = DrawingViewParts.FindView(drawing, viewId);
        if (view == null)
            return Unavailable(viewId, $"view {viewId} is not on the active drawing");

        var visible = DrawingViewParts.VisibleModelIds(view).ToList();

        // A caller's list is narrowed to what the view actually draws. Asking for a part
        // the view hides would otherwise put geometry into an outline of a drawing that
        // does not show it - and the outline is supposed to be the shape on the sheet.
        var wanted = modelIds == null
            ? visible
            : visible.Where(modelIds.Contains).ToList();

        var missing = modelIds == null
            ? Array.Empty<int>()
            : modelIds.Where(id => !visible.Contains(id)).ToArray();

        return Build(
            viewId,
            wanted,
            _solidGeometry,
            options,
            restricted: modelIds != null,
            visible.Count,
            modelIds?.ToArray(),
            missing);
    }

    internal static ViewAssemblyOutlineResult Build(
        int viewId,
        IEnumerable<int> modelIds,
        IDrawingPartSolidGeometryApi solidGeometry,
        OutlineOptions? options = null,
        bool restricted = false,
        int visibleCount = 0,
        IReadOnlyList<int>? requestedIds = null,
        IReadOnlyList<int>? notVisibleRequestedIds = null)
    {
        var partOutlines = new Dictionary<int, PolyTreeD>();
        var unread = new List<UnreadPart>();

        foreach (var modelId in modelIds)
        {
            try
            {
                var geometry = solidGeometry.GetPartSolidGeometryInView(viewId, modelId);
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
                {
                    unread.Add(new UnreadPart(modelId, $"{solid.DroppedFaces} face(s) dropped; outline not used"));
                    continue;
                }

                partOutlines.Add(modelId, ProjectedOutlineBuilder.BuildPart(solid));
            }
            catch (Exception exception)
            {
                unread.Add(new UnreadPart(modelId, exception.Message));
            }
        }

        return new ViewAssemblyOutlineResult(
            viewId,
            ProjectedOutlineBuilder.BuildAssembly(partOutlines.Values, options),
            partOutlines,
            unread,
            error: null,
            restricted,
            visibleCount,
            requestedIds,
            notVisibleRequestedIds);
    }

    private static ViewAssemblyOutlineResult Unavailable(int viewId, string reason) =>
        new(
            viewId,
            new PolyTreeD(),
            new Dictionary<int, PolyTreeD>(),
            Array.Empty<UnreadPart>(),
            reason);
}
