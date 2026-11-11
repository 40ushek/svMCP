using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    /// <summary>
    /// The constraint planner's layout for the views as the current planner sized
    /// and sided them at this scale, or null when it has none to offer: no base
    /// view, a view kind it does not handle yet on the sheet, or nothing that fits
    /// under the policy. A failure is traced and treated as "no layout". Cost is
    /// what the layout's deviations cost under the policy and Emptiness what it
    /// costs for leaving the sheet empty, both in scale steps.
    /// </summary>
    private static (DrawingLayoutCandidateEvaluation? Evaluation, double Cost, double Emptiness) TryEvaluateConstraintLayoutCandidate(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutVariantResult currentVariant,
        double gap,
        DrawingLayoutPlannerMode plannerMode,
        double scale,
        int variant)
    {
        var stopwatch = Stopwatch.StartNew();
        string outcome;
        try
        {
            var reference = currentVariant.Candidates.LastOrDefault();
            var layoutCase = reference == null
                ? null
                : BuildConstraintLayoutCase(workspace, reference, gap, currentVariant.ProjectionResult);
            var policy = plannerMode switch
            {
                DrawingLayoutPlannerMode.ConstraintKeepProjection => ConstraintLayoutPolicy.KeepProjection,
                DrawingLayoutPlannerMode.ConstraintPreferScale => ConstraintLayoutPolicy.PreferScale,
                _ => ConstraintLayoutPolicy.Balanced
            };
            var result = layoutCase == null ? null : ConstraintLayoutShadow.Evaluate(layoutCase, policy, variant);
            if (layoutCase != null)
                SaveConstraintLayoutCase(layoutCase);

            if (result == null)
                outcome = "skipped reason=no-base-view";
            else if (result.SkippedViews > 0)
                outcome = $"skipped reason=unsupported-views count={result.SkippedViews}";
            else if (result.Best == null)
                outcome = result.Plan.Candidates.Count > 0
                    ? $"no-such-variant variant={variant} variants={result.Plan.Candidates.Count}"
                    : $"no-fit needs={result.Plan.PreferredRequiredWidth:0.#}x{result.Plan.PreferredRequiredHeight:0.#}";
            else
            {
                var fill = result.Best.Score.Breakdown.FillRatioRaw;
                var emptiness = policy.GetEmptinessCost(fill);
                PerfTrace.Write(
                    "api-view",
                    "constraint_layout_plan",
                    stopwatch.ElapsedMilliseconds,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "scale=1:{0:0.###} mode={1} result=ok feasible={2} cost={3:0.##} deviations={4} topologies={5} fill={6:0.###} emptiness={7:0.##} variant={8} variants={9} slots={10}",
                        scale,
                        plannerMode,
                        result.Best.IsFeasible ? 1 : 0,
                        result.BestPlan!.Cost,
                        result.BestPlan.Deviations,
                        result.Plan.EvaluatedTopologies,
                        fill,
                        emptiness,
                        variant,
                        result.Plan.Candidates.Count,
                        string.Join(",", result.BestPlan.Slots.OrderBy(static slot => slot.Key).Select(static slot => $"{slot.Key}:{slot.Value}"))));
                return (result.Best, result.BestPlan.Cost, emptiness);
            }
        }
        catch (System.Exception exception)
        {
            outcome = $"error type={exception.GetType().Name} message={exception.Message}";
        }

        PerfTrace.Write(
            "api-view",
            "constraint_layout_plan",
            stopwatch.ElapsedMilliseconds,
            string.Format(CultureInfo.InvariantCulture, "scale=1:{0:0.###} mode={1} result={2}", scale, plannerMode, outcome));
        return (null, 0.0, 0.0);
    }

    private static ConstraintLayoutCase? BuildConstraintLayoutCase(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate reference,
        double gap,
        ProjectionAlignmentResult? referenceProjection)
    {
        var baseView = BaseViewSelection.Select(workspace.RuntimeViews).View;
        if (baseView == null)
            return null;

        // Every view with a section mark on another view, not only the views the
        // current path classifies as sections: an end view with section arrows is one too.
        var sectionMarks = DetailRelationResolver.BuildSectionMarkRelations(
            workspace.RuntimeViews,
            workspace.RuntimeViews.Where(view =>
                !ReferenceEquals(view, baseView) && IsConstraintLayoutViewKind(workspace, view)));

        var shorteningByOwnerId = new Dictionary<int, ViewShorteningCoordinateMapper?>();
        return new ConstraintLayoutCase
        {
            Reference = reference,
            BaseViewId = baseView.GetIdentifier().ID,
            Gap = gap,
            SectionLineAnchors = sectionMarks.All
                .Select(relation => ToConstraintLayoutCaseAnchor(relation, shorteningByOwnerId))
                .ToList(),
            DetailMarkAnchors = DetailRelationResolver
                .Build(
                    workspace.RuntimeViews,
                    workspace.RuntimeViews.Where(view =>
                        workspace.GetLayoutViewKind(view.GetIdentifier().ID) == LayoutViewKind.Detail))
                .All
                .Select(relation => ToConstraintLayoutCaseAnchor(relation, shorteningByOwnerId))
                .ToList(),
            ModelAnchors = BuildConstraintLayoutModelAnchors(workspace, reference),
            ViewSides = BuildConstraintLayoutViewSides(workspace, baseView, sectionMarks),
            ReferenceProjectionMode = referenceProjection?.Mode ?? "none"
        };
    }

    private static ConstraintLayoutCaseAnchor ToConstraintLayoutCaseAnchor(
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

    /// <summary>
    /// Shadow run of the constraint planner (ROADMAP_CONSTRAINT_LAYOUT.md, phase 2):
    /// lays out the same views, scores both layouts and writes the comparison to the
    /// trace. It changes neither the drawing nor the result, and a failure here must
    /// not fail the layout.
    /// </summary>
    private static void TraceConstraintLayoutShadow(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate? reference,
        double gap,
        ProjectionAlignmentResult? referenceProjection)
    {
        if (reference == null)
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var layoutCase = BuildConstraintLayoutCase(workspace, reference, gap, referenceProjection);
            if (layoutCase != null)
                SaveConstraintLayoutCase(layoutCase);

            var result = layoutCase == null ? null : ConstraintLayoutShadow.Evaluate(layoutCase);
            if (result == null)
            {
                PerfTrace.Write("api-view", "constraint_layout_shadow", stopwatch.ElapsedMilliseconds, "result=skipped reason=no-base-view");
                return;
            }

            // The same case under the strict policy: does it fit with every view on
            // its side and in projection?
            var keepProjection = result.BestPlan?.Cost == 0.0
                ? "fits"
                : ConstraintLayoutShadow.Evaluate(layoutCase!, ConstraintLayoutPolicy.KeepProjection)?.Best == null
                    ? "no-fit"
                    : "fits";
            var slots = result.BestPlan == null
                ? "none"
                : string.Join(",", result.BestPlan.Slots.OrderBy(static slot => slot.Key).Select(static slot => $"{slot.Key}:{slot.Value}"));
            PerfTrace.Write(
                "api-view",
                "constraint_layout_shadow",
                stopwatch.ElapsedMilliseconds,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "result={0} views={1} skippedViews={2} topologies={3} truncated={4} fitting={5} preferredNeeds={6:0.#}x{7:0.#} " +
                    "shadowFeasible={8} shadowScore={9:0.###} shadowDeviations={10} shadowCost={19:0.##} keepProjection={20} referenceCandidate={11} referenceFeasible={12} referenceScore={13:0.###} " +
                    "alignmentCheck={17} alignmentMatched={18}/{15} alignmentResidualMax={16:0.###} slots={14}",
                    result.Best == null ? "no-fit" : "ok",
                    result.IncludedViews,
                    result.SkippedViews,
                    result.Plan.EvaluatedTopologies,
                    result.Plan.Truncated ? 1 : 0,
                    result.Plan.Candidates.Count,
                    result.Plan.PreferredRequiredWidth,
                    result.Plan.PreferredRequiredHeight,
                    result.Best?.IsFeasible == true ? 1 : 0,
                    result.Best?.Score.TotalScore ?? 0.0,
                    result.BestPlan?.Deviations ?? 0,
                    string.IsNullOrWhiteSpace(reference.Name) ? "none" : reference.Name,
                    result.Reference.IsFeasible ? 1 : 0,
                    result.Reference.Score.TotalScore,
                    slots,
                    result.AlignmentCheckedViews,
                    result.AlignmentResidualMax,
                    result.AlignmentCheckValid
                        ? "valid"
                        : $"reference-not-aligned({layoutCase!.ReferenceProjectionMode})",
                    result.AlignmentMatchedViews,
                    result.BestPlan?.Cost ?? 0.0,
                    keepProjection));
        }
        catch (System.Exception exception)
        {
            PerfTrace.Write(
                "api-view",
                "constraint_layout_shadow",
                stopwatch.ElapsedMilliseconds,
                $"result=error type={exception.GetType().Name} message={exception.Message}");
        }
    }

    private static bool IsConstraintLayoutViewKind(DrawingLayoutWorkspace workspace, View view)
        => workspace.GetLayoutViewKind(view.GetIdentifier().ID)
            is LayoutViewKind.MainProjected or LayoutViewKind.StandardSection or LayoutViewKind.AnchorDetailSection;

    /// <summary>
    /// The side of the base view each view belongs on, by where the view looks
    /// (ConstraintViewSideResolver). A section, and an end view with or without a
    /// section mark, stands where its arrows point; any other projection stands on
    /// the observer's side, so on an unturned base view the top view is above.
    /// </summary>
    private static List<ConstraintLayoutCaseSide> BuildConstraintLayoutViewSides(
        DrawingLayoutWorkspace workspace,
        View baseView,
        DetailRelationSet sectionMarks)
    {
        var neighbors = workspace.GetTopology().Neighbors;
        var sides = new List<ConstraintLayoutCaseSide>();
        foreach (var view in workspace.RuntimeViews)
        {
            var id = view.GetIdentifier().ID;
            if (ReferenceEquals(view, baseView) || !IsConstraintLayoutViewKind(workspace, view))
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
                && IsConstraintLayoutViewKind(workspace, relation.OwnerView)
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
    private static List<ConstraintLayoutCaseModelAnchor> BuildConstraintLayoutModelAnchors(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate reference)
    {
        var anchors = new List<ConstraintLayoutCaseModelAnchor>();
        var baseSystem = BaseViewSelection.Select(workspace.RuntimeViews).View?.ViewCoordinateSystem;
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

    /// <summary>
    /// Keeps the run's input for offline replay (phase 3 corpus). One file per
    /// drawing, overwritten on every run; a failed write is traced and ignored.
    /// </summary>
    private static void SaveConstraintLayoutCase(ConstraintLayoutCase layoutCase)
    {
        try
        {
            var path = ConstraintLayoutCaseStore.Save(ConstraintLayoutCaseStore.DefaultDirectory, layoutCase);
            PerfTrace.Write("api-view", "constraint_layout_case", 0, $"result=saved path={path}");
        }
        catch (System.Exception exception)
        {
            PerfTrace.Write(
                "api-view",
                "constraint_layout_case",
                0,
                $"result=error type={exception.GetType().Name} message={exception.Message}");
        }
    }
}
