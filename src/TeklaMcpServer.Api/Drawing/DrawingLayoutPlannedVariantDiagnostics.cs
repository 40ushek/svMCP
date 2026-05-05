using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutPlannedVariantSummary
{
    public int MovedCount { get; set; }

    public int DetailMovedCount { get; set; }

    public double MaxDelta { get; set; }

    public double AverageDelta { get; set; }

    public ReservedRect? BoundingBoxBefore { get; set; }

    public ReservedRect? BoundingBoxAfter { get; set; }

    public int ReservedOverlapBefore { get; set; }

    public int ReservedOverlapAfter { get; set; }
}

internal static class DrawingLayoutPlannedVariantDiagnostics
{
    public static DrawingLayoutPlannedVariantSummary BuildSummary(
        IReadOnlyList<DrawingLayoutPlannedView> baseline,
        DrawingLayoutCandidateEvaluation? baselineEvaluation,
        IReadOnlyList<DrawingLayoutPlannedView> variant,
        DrawingLayoutCandidateEvaluation? variantEvaluation)
    {
        var summary = new DrawingLayoutPlannedVariantSummary
        {
            BoundingBoxBefore = TryGetNonDetailBoundingBox(baseline),
            BoundingBoxAfter = TryGetNonDetailBoundingBox(variant),
            ReservedOverlapBefore = baselineEvaluation?.Validation.ReservedOverlapCount ?? -1,
            ReservedOverlapAfter = variantEvaluation?.Validation.ReservedOverlapCount ?? -1
        };

        var baselineById = baseline.ToDictionary(static view => view.Id);
        var totalDelta = 0.0;
        var deltaCount = 0;

        foreach (var view in variant)
        {
            if (!baselineById.TryGetValue(view.Id, out var baselineView))
                continue;

            var dx = view.OriginX - baselineView.OriginX;
            var dy = view.OriginY - baselineView.OriginY;
            var delta = Math.Sqrt(dx * dx + dy * dy);
            if (delta < 1.0)
                continue;

            summary.MovedCount++;
            totalDelta += delta;
            deltaCount++;
            summary.MaxDelta = Math.Max(summary.MaxDelta, delta);

            if (IsDetail(view))
                summary.DetailMovedCount++;
        }

        summary.AverageDelta = deltaCount > 0 ? totalDelta / deltaCount : 0.0;
        return summary;
    }

    private static ReservedRect? TryGetNonDetailBoundingBox(
        IReadOnlyList<DrawingLayoutPlannedView> views)
    {
        var rects = views
            .Where(static view => !IsDetail(view))
            .Select(static view => view.LayoutRect)
            .ToList();
        if (rects.Count == 0)
            return null;

        return new ReservedRect(
            rects.Min(static rect => rect.MinX),
            rects.Min(static rect => rect.MinY),
            rects.Max(static rect => rect.MaxX),
            rects.Max(static rect => rect.MaxY));
    }

    private static bool IsDetail(DrawingLayoutPlannedView view)
        => string.Equals(view.SemanticKind, "Detail", StringComparison.OrdinalIgnoreCase);
}
