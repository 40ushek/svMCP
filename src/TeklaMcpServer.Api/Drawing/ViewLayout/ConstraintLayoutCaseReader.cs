using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Reads from the drawing what the constraint planner works with: the views with
/// their frames and scales, the view the layout is built around, where section
/// lines and detail marks are, which side each view belongs on, and how much of
/// the model each view draws. The one place the planner's input comes from; it
/// asks nothing of the legacy planner's layout, only of the workspace.
/// </summary>
internal static class ConstraintLayoutCaseReader
{
    /// <summary>Name of the reference candidate <see cref="ReadViews"/> returns.</summary>
    public const string ViewsAsTheyStand = "constraint-layout:views-as-they-stand";

    /// <summary>
    /// The views as they stand on the drawing: kind, scale, frame and where the
    /// origin is within the frame, at the scales and frame sizes selected in the
    /// workspace. No side is recorded on them; the planner works those out itself.
    /// </summary>
    public static DrawingLayoutCandidate ReadViews(DrawingLayoutWorkspace workspace)
        => DrawingLayoutCandidateBuilder.FromPlannedViews(
            ViewsAsTheyStand,
            workspace,
            DrawingLayoutCandidateBuilder.ToPlannedViews(workspace, workspace.RuntimeViews, System.Array.Empty<ArrangedView>()));

    /// <summary>
    /// The view the layout is built around: the base view as the current path picks
    /// it, and on a drawing that has none -- sections only, as of a pipe -- the
    /// section that draws the most of the model.
    /// </summary>
    public static View? SelectBaseView(DrawingLayoutWorkspace workspace)
    {
        var baseView = BaseViewSelection.Select(workspace.RuntimeViews).View;
        if (baseView != null)
            return baseView;

        return workspace.RuntimeViews
            .Where(view => IsPlannedViewKind(workspace, view))
            .OrderByDescending(view =>
            {
                var size = workspace.GetSelectedFrameSize(view.GetIdentifier().ID, view.Width, view.Height);
                var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
                return size.Width * size.Height * scale * scale;
            })
            .ThenBy(static view => view.GetIdentifier().ID)
            .FirstOrDefault();
    }

    /// <summary>
    /// Section marks of every view but the base view, not only of the views the
    /// current path classifies as sections: an end view with section arrows is one too.
    /// </summary>
    private static DetailRelationSet BuildSectionMarks(DrawingLayoutWorkspace workspace, View baseView)
        => DetailRelationResolver.BuildSectionMarkRelations(
            workspace.RuntimeViews,
            workspace.RuntimeViews.Where(view =>
                !ReferenceEquals(view, baseView) && IsPlannedViewKind(workspace, view)));

    /// <summary>The views that are sections by their role: another view carries their section mark.</summary>
    public static HashSet<int> FindSectionRoleViewIds(DrawingLayoutWorkspace workspace)
    {
        var baseView = SelectBaseView(workspace);
        return baseView == null
            ? new HashSet<int>()
            : BuildSectionMarks(workspace, baseView).All
                .Select(static relation => relation.DetailView.GetIdentifier().ID)
                .ToHashSet();
    }

    /// <summary>
    /// Everything the planner needs of the drawing in one case. Null when there is
    /// no view to build the layout around.
    /// </summary>
    /// <param name="reference">
    /// The views with their frames, as <see cref="ReadViews"/> gives them -- or, in
    /// the shadow run under the legacy planner, as that planner laid them out.
    /// </param>
    public static ConstraintLayoutCase? Read(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate reference,
        double gap,
        ProjectionAlignmentResult? referenceProjection)
    {
        var baseView = SelectBaseView(workspace);
        if (baseView == null)
            return null;

        var sectionMarks = BuildSectionMarks(workspace, baseView);

        var shorteningByOwnerId = new Dictionary<int, ViewShorteningCoordinateMapper?>();
        return new ConstraintLayoutCase
        {
            Reference = reference,
            BaseViewId = baseView.GetIdentifier().ID,
            Gap = gap,
            SectionLineAnchors = sectionMarks.All
                .Select(relation => ToCaseAnchor(relation, shorteningByOwnerId))
                .ToList(),
            DetailMarkAnchors = DetailRelationResolver
                .Build(
                    workspace.RuntimeViews,
                    workspace.RuntimeViews.Where(view =>
                        workspace.GetLayoutViewKind(view.GetIdentifier().ID) == LayoutViewKind.Detail))
                .All
                .Select(relation => ToCaseAnchor(relation, shorteningByOwnerId))
                .ToList(),
            ModelAnchors = BuildModelAnchors(workspace, reference),
            ModelExtents = BuildModelExtents(workspace, reference),
            ViewSides = BuildViewSides(workspace, baseView, sectionMarks),
            ReferenceProjectionMode = referenceProjection?.Mode ?? "none"
        };
    }

    private static ConstraintLayoutCaseAnchor ToCaseAnchor(
        DetailRelation relation,
        Dictionary<int, ViewShorteningCoordinateMapper?> shorteningByOwnerId)
    {
        var owner = relation.OwnerView;
        var ownerId = owner.GetIdentifier().ID;
        var anchor = new ConstraintLayoutCaseAnchor
        {
            ViewId = relation.DetailView.GetIdentifier().ID,
            X = relation.AnchorX,
            Y = relation.AnchorY,
            OwnerViewId = ownerId
        };
        if (owner.Origin == null || relation.AnchorX is not { } sheetX || relation.AnchorY is not { } sheetY)
            return anchor;

        // The anchor is the owner view's origin plus the point over the scale
        // (DetailRelationResolver.TryProjectToSheet); this takes the point back out.
        var scale = owner.Attributes.Scale > 0 ? owner.Attributes.Scale : 1.0;
        var localX = (sheetX - owner.Origin.X) * scale;
        var localY = (sheetY - owner.Origin.Y) * scale;

        // A shortened view draws the member with stretches of it left out, and the
        // point is given as if nothing were: move it to where it is drawn.
        if (!shorteningByOwnerId.TryGetValue(ownerId, out var shortening))
            shorteningByOwnerId[ownerId] = shortening = DimensionTextBoxContextLoader.TryBuildShorteningMapper(owner);
        if (shortening?.HasShortening == true)
        {
            localX = ToDrawnCoordinate(localX, shortening.XIntervals, x => shortening.ConvertPointToVisual(x, 0)[0]);
            localY = ToDrawnCoordinate(localY, shortening.YIntervals, y => shortening.ConvertPointToVisual(0, y)[1]);
        }

        anchor.OwnerLocalX = localX;
        anchor.OwnerLocalY = localY;
        return anchor;
    }

    /// <summary>
    /// Where a shortened view draws a coordinate. A section line that falls in a
    /// stretch the view leaves out is drawn at the break, between the two pieces.
    /// </summary>
    private static double ToDrawnCoordinate(
        double raw,
        IReadOnlyList<ViewShorteningInterval> shown,
        System.Func<double, double> toDrawn)
    {
        for (var i = 0; i + 1 < shown.Count; i++)
        {
            if (raw > shown[i].Max && raw < shown[i + 1].Min)
                return (toDrawn(shown[i].Max) + toDrawn(shown[i + 1].Min)) / 2.0;
        }

        return toDrawn(raw);
    }

    private static bool IsPlannedViewKind(DrawingLayoutWorkspace workspace, View view)
        => workspace.GetLayoutViewKind(view.GetIdentifier().ID)
            is LayoutViewKind.MainProjected or LayoutViewKind.StandardSection or LayoutViewKind.AnchorDetailSection;

    /// <summary>
    /// The side of the base view each view belongs on, by where the view looks
    /// (ConstraintViewSideResolver). A section, and an end view with or without a
    /// section mark, stands where its arrows point; any other projection stands on
    /// the observer's side, so on an unturned base view the top view is above.
    /// </summary>
    private static List<ConstraintLayoutCaseSide> BuildViewSides(
        DrawingLayoutWorkspace workspace,
        View baseView,
        DetailRelationSet sectionMarks)
    {
        var neighbors = workspace.GetTopology().Neighbors;
        var sides = new List<ConstraintLayoutCaseSide>();
        foreach (var view in workspace.RuntimeViews)
        {
            var id = view.GetIdentifier().ID;
            if (ReferenceEquals(view, baseView) || !IsPlannedViewKind(workspace, view))
                continue;

            // The side comes from where the view looks, relative to the base view; the
            // view type is not asked, since a turned base view makes its name misleading.
            // A section or an end view stands where its arrows point; another projection
            // stands on the observer's side.
            // A section is placed against the view that carries its mark, and its side is
            // a side of that view: a back view is the base view mirrored, left for right.
            var hasSectionMark = sectionMarks.TryGet(id, out var relation);
            var host = hasSectionMark
                && !ReferenceEquals(relation.OwnerView, view)
                && IsPlannedViewKind(workspace, relation.OwnerView)
                    ? relation.OwnerView
                    : baseView;
            var side = SectionPlacementSide.Unknown;
            var baseSystem = host.ViewCoordinateSystem;
            var viewSystem = view.ViewCoordinateSystem;
            if (baseSystem != null && viewSystem != null)
            {
                side = ConstraintViewSideResolver.Resolve(
                    (baseSystem.AxisX.X, baseSystem.AxisX.Y, baseSystem.AxisX.Z),
                    (baseSystem.AxisY.X, baseSystem.AxisY.Y, baseSystem.AxisY.Z),
                    (viewSystem.AxisX.X, viewSystem.AxisX.Y, viewSystem.AxisX.Z),
                    (viewSystem.AxisY.X, viewSystem.AxisY.Y, viewSystem.AxisY.Z),
                    standsWhereItLooks: hasSectionMark || view.ViewType == View.ViewTypes.EndView);
            }

            // A view that looks the same way as the base view, or straight back at it,
            // has no side of its own; the topology's role for it stands (back view below).
            if (side == SectionPlacementSide.Unknown)
            {
                side = (neighbors?.GetRole(id) ?? NeighborRole.Unknown) switch
                {
                    NeighborRole.Top => SectionPlacementSide.Top,
                    NeighborRole.Bottom => SectionPlacementSide.Bottom,
                    NeighborRole.SideLeft => SectionPlacementSide.Left,
                    NeighborRole.SideRight => SectionPlacementSide.Right,
                    _ => SectionPlacementSide.Unknown
                };

                // The topology's roles are those of a member drawn lying down. An upright
                // base view is that drawing turned a quarter: what was above stands on
                // the left, as the view from above does, and what was below on the right.
                if (baseView.Height > baseView.Width)
                {
                    side = side switch
                    {
                        SectionPlacementSide.Top => SectionPlacementSide.Left,
                        SectionPlacementSide.Bottom => SectionPlacementSide.Right,
                        SectionPlacementSide.Left => SectionPlacementSide.Bottom,
                        SectionPlacementSide.Right => SectionPlacementSide.Top,
                        _ => side
                    };
                }
            }

            if (side != SectionPlacementSide.Unknown)
                sides.Add(new ConstraintLayoutCaseSide { ViewId = id, Side = side.ToString() });
        }

        return sides;
    }

    /// <summary>
    /// One model anchor per view that has a frame and a readable coordinate system.
    /// Not verified on a live drawing: alignmentResidualMax in the trace compares
    /// these anchors with the alignment the current path produced.
    /// </summary>
    private static List<ConstraintLayoutCaseModelAnchor> BuildModelAnchors(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate reference)
    {
        var anchors = new List<ConstraintLayoutCaseModelAnchor>();
        var baseSystem = SelectBaseView(workspace)?.ViewCoordinateSystem;
        foreach (var view in reference.Views)
        {
            var coordinateSystem = workspace.TryGetRuntimeView(view.Id)?.ViewCoordinateSystem;
            if (view.LayoutRect == null || coordinateSystem == null)
                continue;

            // Two views line up through the model origin only when their axes are
            // square to each other. A section cut across a sloping member is turned
            // against the base view; it is aligned by frame centers instead.
            if (baseSystem != null
                && !(IsAlongAnAxisOf(baseSystem, coordinateSystem.AxisX) && IsAlongAnAxisOf(baseSystem, coordinateSystem.AxisY)))
            {
                continue;
            }

            var anchor = ConstraintLayoutCaseModelAnchor.FromViewCoordinateSystem(
                view.Id,
                view.LayoutRect,
                view.OriginX,
                view.OriginY,
                view.Scale,
                (coordinateSystem.Origin.X, coordinateSystem.Origin.Y, coordinateSystem.Origin.Z),
                (coordinateSystem.AxisX.X, coordinateSystem.AxisX.Y, coordinateSystem.AxisX.Z),
                (coordinateSystem.AxisY.X, coordinateSystem.AxisY.Y, coordinateSystem.AxisY.Z));
            if (anchor != null)
                anchors.Add(anchor);
        }

        return anchors;
    }

    /// <summary>
    /// How much of the model each view draws: its visible areas, which for a
    /// shortened view are the stretches it keeps. A view that cannot be read is
    /// left out.
    /// </summary>
    private static List<ConstraintLayoutCaseModelExtent> BuildModelExtents(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate reference)
    {
        var extents = new List<ConstraintLayoutCaseModelExtent>();
        foreach (var view in reference.Views)
        {
            var runtimeView = workspace.TryGetRuntimeView(view.Id);
            if (runtimeView == null)
                continue;

            try
            {
                var boxes = DimensionTextBoxContextLoader.ReadVisibleAreaRestrictionBoxes(runtimeView);
                if (boxes.Count == 0 && runtimeView.RestrictionBox != null)
                    boxes.Add(runtimeView.RestrictionBox);

                var width = CoveredLength(boxes.Select(static box => (box.MinPoint.X, box.MaxPoint.X)));
                var height = CoveredLength(boxes.Select(static box => (box.MinPoint.Y, box.MaxPoint.Y)));
                if (width > 0 && height > 0)
                    extents.Add(new ConstraintLayoutCaseModelExtent { ViewId = view.Id, Width = width, Height = height });
            }
            catch (System.Exception)
            {
                // Without the extent the frame is scaled proportionally.
            }
        }

        return extents;
    }

    /// <summary>The total length the intervals cover, overlaps counted once.</summary>
    private static double CoveredLength(IEnumerable<(double Min, double Max)> intervals)
    {
        var covered = 0.0;
        var reach = double.NegativeInfinity;
        foreach (var (min, max) in intervals.Where(static interval => interval.Max > interval.Min).OrderBy(static interval => interval.Min))
        {
            if (max <= reach)
                continue;

            covered += max - System.Math.Max(min, reach);
            reach = max;
        }

        return covered;
    }

    private static bool IsAlongAnAxisOf(Tekla.Structures.Geometry3d.CoordinateSystem system, Tekla.Structures.Geometry3d.Vector direction)
    {
        var length = direction.GetLength();
        if (length <= 1e-9)
            return false;

        var normal = system.AxisX.Cross(system.AxisY);
        return new[] { system.AxisX, system.AxisY, normal }.Any(axis =>
            axis.GetLength() > 1e-9
            && System.Math.Abs(axis.Dot(direction)) / (axis.GetLength() * length) >= 0.9999);
    }
}
