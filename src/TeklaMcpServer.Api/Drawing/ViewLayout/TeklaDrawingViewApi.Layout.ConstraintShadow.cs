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
    /// costs for leaving the sheet empty, both in the policy's prices. NoFit tells
    /// "laid the views out and found no room" from the other reasons.
    /// </summary>
    private (DrawingLayoutCandidateEvaluation? Evaluation, double Cost, double Emptiness, bool NoFit, ConstraintLayoutCase? Case, string? NoSuchVariant) TryEvaluateConstraintLayoutCandidate(
        DrawingLayoutWorkspace workspace,
        double gap,
        DrawingLayoutPlannerMode plannerMode,
        double scale,
        int variant)
    {
        var stopwatch = Stopwatch.StartNew();
        string outcome;
        var noFit = false;
        string? noSuchVariant = null;
        ConstraintLayoutCase? layoutCase = null;
        try
        {
            // The views as they stand, read by the planner's own reader: the legacy
            // planner is not asked to lay the drawing out first.
            layoutCase = ConstraintLayoutCaseReader.Read(
                workspace, ConstraintLayoutCaseReader.ReadViews(workspace), gap, referenceProjection: null);
            var policy = ResolveConstraintLayoutPolicy(plannerMode);
            var result = layoutCase == null ? null : ConstraintLayoutShadow.Evaluate(layoutCase, policy, variant);
            if (layoutCase != null)
                SaveConstraintLayoutCase(layoutCase);

            if (result == null)
                outcome = "skipped reason=no-base-view";
            else if (result.SkippedViews > 0)
                outcome = $"skipped reason=unsupported-views count={result.SkippedViews}";
            else if (result.Best == null)
            {
                noFit = result.Plan.Candidates.Count == 0;
                outcome = noFit
                    ? $"no-fit needs={result.Plan.PreferredRequiredWidth:0.#}x{result.Plan.PreferredRequiredHeight:0.#}"
                    : $"no-such-variant variant={variant} variants={result.Plan.Candidates.Count}";
                // The caller asked for a layout by number and there is none with it:
                // that is refused, not answered with another planner's layout.
                if (!noFit)
                    noSuchVariant = string.Format(
                        CultureInfo.InvariantCulture,
                        "plannerVariant={0} does not exist: the planner has {1} layout(s) at scale 1:{2:0.###}, numbered 0..{3}",
                        variant, result.Plan.Candidates.Count, scale, result.Plan.Candidates.Count - 1);
            }
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
                        "scale=1:{0:0.###} mode={1} result=ok feasible={2} cost={3:0.##} deviations={4} topologies={5} fill={6:0.###} emptiness={7:0.##} variant={8} variants={9} slots={10} shift={11}",
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
                        string.Join(",", result.BestPlan.Slots.OrderBy(static slot => slot.Key).Select(static slot => $"{slot.Key}:{slot.Value}")),
                        result.BestPlan.ShiftedViewId is { } shiftedId
                            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.#}", shiftedId, result.BestPlan.Shift)
                            : "none"));
                return (result.Best, result.BestPlan.Cost, emptiness, false, layoutCase, null);
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
            string.Format(CultureInfo.InvariantCulture, "scale=1:{0:0.###} mode={1} oppositeSide={3} mainViewShift={4} result={2}", scale, plannerMode, outcome, PlannerAllowOppositeSide ? 1 : 0, PlannerAllowMainViewShift ? 1 : 0));
        return (null, 0.0, 0.0, noFit, layoutCase, noSuchVariant);
    }

    /// <summary>
    /// Constraint planner setting: whether a section may stand on the side of its
    /// host view opposite its own. Set by the caller before FitViewsToSheet; the
    /// planned settings file will fill settings like this one.
    /// </summary>
    public bool PlannerAllowOppositeSide { get; set; } = true;

    /// <summary>
    /// Constraint planner setting: whether a main view may slide along its row off
    /// the base view's axis to make room for a free view. Off unless the caller
    /// sets it.
    /// </summary>
    public bool PlannerAllowMainViewShift { get; set; }

    private ConstraintLayoutPolicy ResolveConstraintLayoutPolicy(DrawingLayoutPlannerMode plannerMode)
    {
        var policy = plannerMode switch
        {
            DrawingLayoutPlannerMode.ConstraintKeepProjection => ConstraintLayoutPolicy.KeepProjection,
            DrawingLayoutPlannerMode.ConstraintPreferScale => ConstraintLayoutPolicy.PreferScale,
            _ => ConstraintLayoutPolicy.Balanced
        };
        policy.AllowOppositeSide = PlannerAllowOppositeSide;
        policy.AllowMainViewShift = PlannerAllowMainViewShift;
        return policy;
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
            var layoutCase = ConstraintLayoutCaseReader.Read(workspace, reference, gap, referenceProjection);
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
