using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutCandidateApplyPlan
{
    public string CandidateName { get; set; } = string.Empty;

    public bool CanApply { get; set; }

    public DrawingLayoutCandidateApplyPlanReason Reason { get; set; }

    public List<DrawingLayoutCandidateApplyMove> Moves { get; set; } = new();
}

internal sealed class DrawingLayoutCandidateApplyMove
{
    public int ViewId { get; set; }

    public double TargetOriginX { get; set; }

    public double TargetOriginY { get; set; }

    public double Scale { get; set; }

    public ReservedRect? LayoutRect { get; set; }
}

internal enum DrawingLayoutCandidateApplyPlanReason
{
    NoSelectedCandidate,
    PlannedCandidate,
    RuntimeCandidate
}

internal static class DrawingLayoutCandidateApplyPlanReasonFormatter
{
    public static string ToTraceString(DrawingLayoutCandidateApplyPlanReason reason)
        => reason switch
        {
            DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate => "no-selected-candidate",
            DrawingLayoutCandidateApplyPlanReason.PlannedCandidate => "planned-candidate",
            DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate => "runtime-candidate",
            _ => "unknown"
        };
}

internal static class DrawingLayoutCandidateApplyPlanBuilder
{
    private const string PlannedCandidatePrefix = "fit_views_to_sheet:planned-";

    public static DrawingLayoutCandidateApplyPlan FromEvaluation(
        DrawingLayoutCandidateEvaluation? evaluation)
    {
        if (evaluation == null)
        {
            return new DrawingLayoutCandidateApplyPlan
            {
                Reason = DrawingLayoutCandidateApplyPlanReason.NoSelectedCandidate
            };
        }

        var candidate = evaluation.Candidate;
        var canApply = IsPlannedCandidate(candidate);
        var plan = new DrawingLayoutCandidateApplyPlan
        {
            CandidateName = candidate.Name,
            CanApply = canApply,
            Reason = canApply
                ? DrawingLayoutCandidateApplyPlanReason.PlannedCandidate
                : DrawingLayoutCandidateApplyPlanReason.RuntimeCandidate
        };

        if (!plan.CanApply)
            return plan;

        plan.Moves = candidate.Views
            .Select(static view => new DrawingLayoutCandidateApplyMove
            {
                ViewId = view.Id,
                TargetOriginX = view.OriginX,
                TargetOriginY = view.OriginY,
                Scale = view.Scale,
                LayoutRect = view.LayoutRect
            })
            .ToList();

        return plan;
    }

    private static bool IsPlannedCandidate(DrawingLayoutCandidate candidate)
        => candidate.Name.StartsWith(PlannedCandidatePrefix, StringComparison.Ordinal);
}
