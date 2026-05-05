using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingCaseLayoutDiagnosticsFactory
{
    public static DrawingCaseLayoutDiagnostics FromSelection(
        DrawingLayoutCandidateSelection selection,
        DrawingLayoutCandidateApplyPlan? applyPlan = null,
        DrawingLayoutCandidateApplyDeltaSummary? applyDelta = null,
        DrawingLayoutCandidateApplySafetyDecision? applySafety = null)
    {
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));

        var selected = selection.Selected;
        return new DrawingCaseLayoutDiagnostics
        {
            SelectedCandidateName = ResolveCandidateName(selected, applyPlan),
            SelectedCandidateScore = selected?.Score.TotalScore,
            SelectedCandidateFeasible = selected?.IsFeasible,
            ApplyPlan = applyPlan == null ? null : CreateApplyPlanSummary(applyPlan),
            ApplyDelta = applyDelta == null ? null : CreateApplyDeltaSummary(applyDelta),
            ApplySafety = applySafety == null ? null : CreateApplySafetySummary(applySafety),
            Diagnostics = CollectDiagnostics(selection, selected)
        };
    }

    private static string ResolveCandidateName(
        DrawingLayoutCandidateEvaluation? selected,
        DrawingLayoutCandidateApplyPlan? applyPlan)
    {
        var selectedName = selected?.Candidate.Name;
        if (!string.IsNullOrWhiteSpace(selectedName))
            return selectedName ?? string.Empty;

        return applyPlan?.CandidateName ?? string.Empty;
    }

    private static DrawingCaseApplyPlanSummary CreateApplyPlanSummary(
        DrawingLayoutCandidateApplyPlan plan)
        => new()
        {
            CanApply = plan.CanApply,
            Reason = DrawingLayoutCandidateApplyPlanReasonFormatter.ToTraceString(plan.Reason),
            MoveCount = plan.Moves.Count
        };

    private static DrawingCaseApplyDeltaSummary CreateApplyDeltaSummary(
        DrawingLayoutCandidateApplyDeltaSummary summary)
        => new()
        {
            MoveCount = summary.MoveCount,
            ComparableMoveCount = summary.ComparableMoveCount,
            MissingBaselineCount = summary.MissingBaselineCount,
            MovedCount = summary.MovedCount,
            ScaleChangedCount = summary.ScaleChangedCount,
            MaxDelta = summary.MaxDelta,
            AverageDelta = summary.AverageDelta
        };

    private static DrawingCaseApplySafetySummary CreateApplySafetySummary(
        DrawingLayoutCandidateApplySafetyDecision decision)
        => new()
        {
            RequestedMode = decision.RequestedMode.ToString(),
            EffectiveMode = decision.EffectiveMode.ToString(),
            Allowed = decision.IsAllowed,
            Reason = DrawingLayoutCandidateApplySafetyDecisionReasonFormatter.ToTraceString(decision.Reason)
        };

    private static List<string> CollectDiagnostics(
        DrawingLayoutCandidateSelection selection,
        DrawingLayoutCandidateEvaluation? selected)
    {
        return selection.Diagnostics
            .Concat(selected?.Validation.Diagnostics ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
