using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Model _model;
    private readonly IDrawingPartSolidGeometryApi _solidGeometry;

    public TeklaDrawingAssemblyOutlineApi(Model model, IDrawingPartSolidGeometryApi? solidGeometry = null)
    {
        _model = model;
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

        var selected = DrawingViewParts.GetDepthFilteredParts(_model, view);
        var visible = selected.ModelIds;

        // A caller's list is narrowed to what the view actually draws. Asking for a part
        // the view hides would otherwise put geometry into an outline of a drawing that
        // does not show it - and the outline is supposed to be the shape on the sheet.
        var wanted = modelIds == null
            ? visible
            : visible.Where(modelIds.Contains).ToList();

        var requestedIds = modelIds?.ToArray();
        var missing = requestedIds == null
            ? Array.Empty<int>()
            : requestedIds.Where(id => !selected.CandidateModelIds.Contains(id)).ToArray();
        var selectionUnread = requestedIds == null
            ? selected.Incomplete
            : selected.Incomplete.Where(part => requestedIds.Contains(part.ModelId));
        var outsideDepth = requestedIds == null
            ? selected.OutsideDepthModelIds
            : selected.OutsideDepthModelIds.Where(requestedIds.Contains).ToArray();
        var unresolvedDepth = requestedIds == null
            ? selected.Incomplete.Select(part => part.ModelId).Distinct().ToArray()
            : selected.Incomplete
                .Where(part => requestedIds.Contains(part.ModelId))
                .Select(part => part.ModelId)
                .Distinct()
                .ToArray();

        return Build(
            viewId,
            wanted,
            _solidGeometry,
            options,
            restricted: modelIds != null,
            visible.Count,
            requestedIds,
            missing,
            outsideDepth,
            unresolvedDepth,
            selectionUnread.ToList(),
            ReadDepthRange(view));
    }

    // The view's depth along its own Z, usable only where the solids are read in the same
    // coordinates as the window: on some sections the display origin differs from the view's.
    private (double Min, double Max, double? Cut)? ReadDepthRange(View view)
    {
        static double[] Numbers(Tekla.Structures.Geometry3d.CoordinateSystem system) => [
            system.Origin.X, system.Origin.Y, system.Origin.Z, system.AxisX.X, system.AxisX.Y, system.AxisX.Z,
            system.AxisY.X, system.AxisY.Y, system.AxisY.Z];
        return ViewDepthWindow.Read(_model, view).Box is { } box
            ? ViewDepthWindow.DepthRangeInView(box.MinZ, box.MaxZ,
                Numbers(view.ViewCoordinateSystem), Numbers(view.DisplayCoordinateSystem))
            : null;
    }

    /// <summary>
    /// What a view shows of a part that reaches beyond its depth. Geometry beyond the depth is
    /// not drawn there and nothing may be dimensioned to it.
    /// A part is drawn by its faces lying entirely within the depth: true corners. Where the
    /// view plane cuts the part outside what those faces show (a gusset running through, with
    /// only a notch inside the depth), the contour of the cut is drawn with them.
    /// A member running through the view plane with no face within the depth is drawn by the cut.
    /// A member lying along the view plane (a lengthwise section), a part that cannot be cut
    /// cleanly, and a part wholly inside the depth are returned unchanged: projected in full.
    /// </summary>
    internal static PartSolidGeometryInViewResult WithinDepth(PartSolidGeometryInViewResult geometry,
        (double Min, double Max, double? Cut)? depth)
    {
        if (depth is not { } range || !geometry.Success) return geometry;
        const double tolerance = 0.5;
        var vertices = geometry.Solid.Vertices.Where(vertex => vertex.Point.Length >= 3)
            .GroupBy(vertex => vertex.Index).ToDictionary(group => group.Key, group => group.First().Point);
        bool Inside(double[] point) => point[2] >= range.Min - tolerance && point[2] <= range.Max + tolerance;
        if (vertices.Count == 0 || vertices.Values.All(Inside)) return geometry;

        PartSolidGeometryInViewResult With(List<PartVertexGeometry> shownVertices, List<PartFaceGeometry> shownFaces) => new() {
            Success = true, ViewId = geometry.ViewId, ModelId = geometry.ModelId,
            StartPoint = geometry.StartPoint, EndPoint = geometry.EndPoint,
            CoordinateSystemOrigin = geometry.CoordinateSystemOrigin, AxisX = geometry.AxisX, AxisY = geometry.AxisY,
            Solid = new PartSolidGeometry {
                BboxMin = geometry.Solid.BboxMin, BboxMax = geometry.Solid.BboxMax,
                SolidGeometryComplete = geometry.Solid.SolidGeometryComplete,
                ViewHull = geometry.Solid.ViewHull, Vertices = shownVertices, Faces = shownFaces
            }
        };
        static double Low(IEnumerable<double[]> points, int axis) => points.Min(point => point[axis]);
        static double High(IEnumerable<double[]> points, int axis) => points.Max(point => point[axis]);

        // Tekla's own cut where the read carries it; worked out from the faces otherwise.
        var firstCutIndex = vertices.Keys.Max() + 1;
        var cut = range.Cut is not { } plane ? null
            : geometry.Solid.ViewPlaneCut is { } regions ? SolidDepthClip.FromRegions(geometry.Solid, regions, firstCutIndex)
            : SolidDepthClip.Section(geometry.Solid, plane, firstCutIndex);
        var cutPoints = cut?.Vertices.Select(vertex => vertex.Point).ToArray() ?? [];
        var faces = geometry.Solid.Faces.Where(face => face.Loops.Count > 0
            && face.Loops.All(loop => loop.VertexIndexes.All(index =>
                vertices.TryGetValue(index, out var point) && Inside(point)))).ToList();
        // A face seen edge-on adds nothing to the outline; at least one face must show an area.
        if (faces.Any(face => SolidDepthClip.ProjectedArea(geometry.Solid, face) > 1e-6))
        {
            var facePoints = faces.SelectMany(face => face.Loops).SelectMany(loop => loop.VertexIndexes)
                .Select(index => vertices[index]).ToArray();
            // The cut of a part ending at the view plane repeats its end face, shifted a little
            // on a tilted part (8 becomes 7.9): the face alone is drawn.
            var slack = Dimensions.DimensionPlacementSettings.CutRepeatsOwnFaceToleranceViewUnits;
            var repeats = cut == null || new[] { 0, 1 }.All(axis =>
                Low(cutPoints, axis) >= Low(facePoints, axis) - slack && High(cutPoints, axis) <= High(facePoints, axis) + slack);
            return repeats
                ? With(geometry.Solid.Vertices, faces)
                : With(geometry.Solid.Vertices.Concat(cut!.Vertices).ToList(), faces.Concat(cut.Faces).ToList());
        }

        if (cut == null) return geometry;
        // Through the plane or along it: the cut of a member running through is smaller than
        // the member is deep; a lengthwise cut is as long as the member.
        var through = Math.Max(High(cutPoints, 0) - Low(cutPoints, 0), High(cutPoints, 1) - Low(cutPoints, 1))
            < High(vertices.Values, 2) - Low(vertices.Values, 2);
        return through ? With(cut.Vertices, cut.Faces) : geometry;
    }

    internal static ViewAssemblyOutlineResult Build(
        int viewId,
        IEnumerable<int> modelIds,
        IDrawingPartSolidGeometryApi solidGeometry,
        OutlineOptions? options = null,
        bool restricted = false,
        int visibleCount = 0,
        IReadOnlyList<int>? requestedIds = null,
        IReadOnlyList<int>? notVisibleRequestedIds = null,
        IReadOnlyList<int>? outsideDepthModelIds = null,
        IReadOnlyList<int>? unresolvedDepthModelIds = null,
        IReadOnlyList<UnreadPart>? selectionUnread = null,
        (double Min, double Max, double? Cut)? depthRange = null)
    {
        var partOutlines = new Dictionary<int, PolyTreeD>();
        var partSolidGeometries = new Dictionary<int, PartSolidGeometryInViewResult>();
        var unread = selectionUnread?.ToList() ?? new List<UnreadPart>();

        foreach (var modelId in modelIds.Distinct())
        {
            try
            {
                var geometry = solidGeometry.GetPartSolidGeometryInView(viewId, modelId);
                if (!geometry.Success)
                {
                    unread.Add(new UnreadPart(modelId, geometry.Error ?? "geometry read failed"));
                    continue;
                }

                // Retain the detached data from this read for the view context.
                partSolidGeometries.Add(modelId, geometry);

                var solid = ViewSolidAdapter.FromGeometry(WithinDepth(geometry, depthRange));
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

                // KNOWN GAP (see ROADMAP_SECTION_CROSS_SECTIONS.md, "Open gap: no signal
                // when an included solid outgrows the window"): this flattens the part's
                // FULL, unclipped solid. GetDepthFilteredParts deliberately includes a
                // member whose solid runs past RestrictionBox on the depth axis (a
                // mid-span section must still list it), but nothing here checks whether
                // THIS solid's own extent stayed inside that box. For a part fully inside
                // the window the projection is the true cut; for one that reaches past it,
                // this silently returns the whole member's silhouette instead - and no
                // field on the result says which happened. Not fixed here; do not treat an
                // outline from a section/end view as a trustworthy cut shape without first
                // closing this.
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
            notVisibleRequestedIds,
            outsideDepthModelIds,
            unresolvedDepthModelIds,
            partSolidGeometries,
            depthRange);
    }

    private static ViewAssemblyOutlineResult Unavailable(int viewId, string reason) =>
        new(
            viewId,
            new PolyTreeD(),
            new Dictionary<int, PolyTreeD>(),
            Array.Empty<UnreadPart>(),
            reason);

}
