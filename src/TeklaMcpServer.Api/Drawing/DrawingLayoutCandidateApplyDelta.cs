using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingLayoutCandidateApplyTolerances
{
    public const double Movement = 0.01;
    public const double Scale = 0.01;
}

internal sealed class DrawingLayoutCandidateApplyDeltaSummary
{
    public string BaselineCandidateName { get; set; } = string.Empty;

    public string CandidateName { get; set; } = string.Empty;

    public int MoveCount { get; set; }

    public int ComparableMoveCount { get; set; }

    public int MissingBaselineCount { get; set; }

    public int MovedCount { get; set; }

    public int ScaleChangedCount { get; set; }

    public double MaxDelta { get; set; }

    public double AverageDelta { get; set; }

    public List<DrawingLayoutCandidateApplyDelta> Deltas { get; set; } = new();
}

internal sealed class DrawingLayoutCandidateApplyDelta
{
    public int ViewId { get; set; }

    public bool MissingBaseline { get; set; }

    public bool Moved { get; set; }

    public bool ScaleChanged { get; set; }

    public double CurrentOriginX { get; set; }

    public double CurrentOriginY { get; set; }

    public double TargetOriginX { get; set; }

    public double TargetOriginY { get; set; }

    public double DeltaX { get; set; }

    public double DeltaY { get; set; }

    public double Distance { get; set; }

    public double CurrentScale { get; set; }

    public double TargetScale { get; set; }
}

internal static class DrawingLayoutCandidateApplyDeltaBuilder
{
    public static DrawingLayoutCandidateApplyDeltaSummary BuildDeltas(
        DrawingLayoutCandidate baseline,
        DrawingLayoutCandidateApplyPlan plan)
    {
        if (baseline == null)
            throw new ArgumentNullException(nameof(baseline));
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));

        var baselineViews = baseline.Views.ToDictionary(static view => view.Id);
        var summary = new DrawingLayoutCandidateApplyDeltaSummary
        {
            BaselineCandidateName = baseline.Name,
            CandidateName = plan.CandidateName,
            MoveCount = plan.Moves.Count
        };

        foreach (var move in plan.Moves)
        {
            if (!baselineViews.TryGetValue(move.ViewId, out var baselineView))
            {
                summary.Deltas.Add(new DrawingLayoutCandidateApplyDelta
                {
                    ViewId = move.ViewId,
                    MissingBaseline = true,
                    TargetOriginX = move.TargetOriginX,
                    TargetOriginY = move.TargetOriginY,
                    TargetScale = move.Scale
                });
                continue;
            }

            var dx = move.TargetOriginX - baselineView.OriginX;
            var dy = move.TargetOriginY - baselineView.OriginY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            var scaleChanged = Math.Abs(move.Scale - baselineView.Scale) >= DrawingLayoutCandidateApplyTolerances.Scale;

            summary.Deltas.Add(new DrawingLayoutCandidateApplyDelta
            {
                ViewId = move.ViewId,
                CurrentOriginX = baselineView.OriginX,
                CurrentOriginY = baselineView.OriginY,
                TargetOriginX = move.TargetOriginX,
                TargetOriginY = move.TargetOriginY,
                DeltaX = dx,
                DeltaY = dy,
                Distance = distance,
                Moved = distance >= DrawingLayoutCandidateApplyTolerances.Movement,
                CurrentScale = baselineView.Scale,
                TargetScale = move.Scale,
                ScaleChanged = scaleChanged
            });
        }

        var comparable = summary.Deltas
            .Where(static delta => !delta.MissingBaseline)
            .ToList();
        summary.ComparableMoveCount = comparable.Count;
        summary.MissingBaselineCount = summary.Deltas.Count(static delta => delta.MissingBaseline);
        summary.MovedCount = comparable.Count(static delta => delta.Moved);
        summary.ScaleChangedCount = comparable.Count(static delta => delta.ScaleChanged);
        summary.MaxDelta = comparable.Count == 0 ? 0.0 : comparable.Max(static delta => delta.Distance);
        summary.AverageDelta = comparable.Count == 0 ? 0.0 : comparable.Average(static delta => delta.Distance);
        return summary;
    }
}
