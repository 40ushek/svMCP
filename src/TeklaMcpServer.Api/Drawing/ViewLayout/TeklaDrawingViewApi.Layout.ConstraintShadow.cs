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
    /// Shadow run of the constraint planner (ROADMAP_CONSTRAINT_LAYOUT.md, phase 2):
    /// lays out the same views, scores both layouts and writes the comparison to the
    /// trace. It changes neither the drawing nor the result, and a failure here must
    /// not fail the layout.
    /// </summary>
    private static void TraceConstraintLayoutShadow(
        DrawingLayoutWorkspace workspace,
        DrawingLayoutCandidate? reference,
        double gap)
    {
        if (reference == null)
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var baseView = BaseViewSelection.Select(workspace.RuntimeViews).View;
            var layoutCase = baseView == null
                ? null
                : new ConstraintLayoutCase
                {
                    Reference = reference,
                    BaseViewId = baseView.GetIdentifier().ID,
                    Gap = gap,
                    SectionLineAnchors = workspace.Views
                        .Where(static view => view.ParentRelationKind == DrawingLayoutWorkspace.RelationKindSectionMark)
                        .Select(static view => new ConstraintLayoutCaseAnchor
                        {
                            ViewId = view.Id,
                            X = view.ParentAnchorX,
                            Y = view.ParentAnchorY
                        })
                        .ToList(),
                    ModelAnchors = BuildConstraintLayoutModelAnchors(workspace, reference)
                };
            if (layoutCase != null)
                SaveConstraintLayoutCase(layoutCase);

            var result = layoutCase == null ? null : ConstraintLayoutShadow.Evaluate(layoutCase);
            if (result == null)
            {
                PerfTrace.Write("api-view", "constraint_layout_shadow", stopwatch.ElapsedMilliseconds, "result=skipped reason=no-base-view");
                return;
            }

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
                    "shadowFeasible={8} shadowScore={9:0.###} shadowDeviations={10} referenceCandidate={11} referenceFeasible={12} referenceScore={13:0.###} " +
                    "alignmentChecked={15} alignmentResidualMax={16:0.###} slots={14}",
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
                    result.AlignmentResidualMax));
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
        foreach (var view in reference.Views)
        {
            var coordinateSystem = workspace.TryGetRuntimeView(view.Id)?.ViewCoordinateSystem;
            if (view.LayoutRect == null || coordinateSystem == null)
                continue;

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
