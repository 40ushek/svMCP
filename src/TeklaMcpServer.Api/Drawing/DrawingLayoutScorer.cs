using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutScorer
{
    private const double Epsilon = 1e-6;

    public DrawingLayoutScore Score(
        DrawingLayoutCandidate candidate,
        DrawingLayoutScoreWeights? weights = null)
    {
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));

        var score = Score(candidate.ToDrawingContext(), weights);
        var effectiveWeights = weights ?? new DrawingLayoutScoreWeights();
        var preferredSidePenalty = ComputePreferredSidePenalty(candidate.Views);

        score.TotalScore -= effectiveWeights.PreferredSidePenaltyWeight * preferredSidePenalty;
        score.Breakdown.PreferredSidePenalty = preferredSidePenalty;
        score.Breakdown.PreferredSidePenaltyWeight = effectiveWeights.PreferredSidePenaltyWeight;

        return score;
    }

    public DrawingLayoutCandidateEvaluation Evaluate(
        DrawingLayoutCandidate candidate,
        DrawingLayoutScoreWeights? weights = null)
    {
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));

        var score = Score(candidate, weights);
        var validation = new DrawingLayoutCandidateValidation
        {
            MissingRectCount = Math.Max(candidate.Views.Count - score.Breakdown.ScoredViewCount, 0),
            ViewOverlapCount = score.Breakdown.ViewOverlapCount,
            ViewOverlapArea = score.Breakdown.ViewOverlapArea,
            ReservedOverlapCount = score.Breakdown.ReservedOverlapCount,
            ReservedOverlapArea = score.Breakdown.ReservedOverlapArea,
            Diagnostics = candidate.Diagnostics
                .Concat(score.Diagnostics)
                .ToList()
        };

        return new DrawingLayoutCandidateEvaluation
        {
            Candidate = candidate,
            Score = score,
            Validation = validation
        };
    }

    public DrawingLayoutScore Score(
        DrawingContext context,
        DrawingLayoutScoreWeights? weights = null)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var effectiveWeights = weights ?? new DrawingLayoutScoreWeights();
        var result = new DrawingLayoutScore();
        var diagnostics = result.Diagnostics;
        var workspace = DrawingLayoutWorkspace.From(context);

        var scoredViews = BuildViewRects(workspace.Views, diagnostics);
        var sheetArea = Math.Max(workspace.SheetWidth, 0.0) * Math.Max(workspace.SheetHeight, 0.0);
        var reservedAreaUnion = ComputeUnionArea(workspace.ReservedAreas);
        var availableSheetArea = Math.Max(sheetArea - reservedAreaUnion, 0.0);
        var totalViewArea = scoredViews.Sum(static view => view.Rect.Width * view.Rect.Height);

        var fillRatioRaw = availableSheetArea > Epsilon
            ? totalViewArea / availableSheetArea
            : 0.0;
        var fillRatioScore = availableSheetArea > Epsilon
            ? Math.Min(fillRatioRaw, 1.0)
            : 0.0;

        var viewOverlapCount = 0;
        var viewOverlapArea = 0.0;
        for (var i = 0; i < scoredViews.Count; i++)
        for (var j = i + 1; j < scoredViews.Count; j++)
        {
            var overlapArea = TryGetOverlapArea(scoredViews[i].Rect, scoredViews[j].Rect, out var area)
                ? area
                : 0.0;
            if (overlapArea <= Epsilon)
                continue;

            viewOverlapCount++;
            viewOverlapArea += overlapArea;
        }

        var reservedOverlapCount = 0;
        var reservedOverlapArea = 0.0;
        foreach (var view in scoredViews)
        {
            var intersections = new List<ReservedRect>();
            foreach (var reserved in workspace.ReservedAreas)
            {
                if (!TryIntersect(view.Rect, reserved, out var intersection))
                    continue;

                intersections.Add(intersection);
            }

            if (intersections.Count == 0)
                continue;

            reservedOverlapCount++;
            reservedOverlapArea += ComputeUnionArea(intersections);
        }

        var uniformScaleScore = ComputeUniformScaleScore(scoredViews, diagnostics);
        var viewOverlapPenalty = availableSheetArea > Epsilon
            ? Math.Min(viewOverlapArea / availableSheetArea, 1.0)
            : (viewOverlapArea > Epsilon ? 1.0 : 0.0);
        var reservedOverlapPenalty = availableSheetArea > Epsilon
            ? Math.Min(reservedOverlapArea / availableSheetArea, 1.0)
            : (reservedOverlapArea > Epsilon ? 1.0 : 0.0);
        var edgeMarginPenalty = ComputeEdgeMarginPenalty(workspace, scoredViews);
        var compactnessPenalty = ComputeCompactnessPenalty(scoredViews, availableSheetArea);

        result.TotalScore =
            (effectiveWeights.FillRatioWeight * fillRatioScore) +
            (effectiveWeights.UniformScaleWeight * uniformScaleScore) -
            (effectiveWeights.ViewOverlapPenaltyWeight * viewOverlapPenalty) -
            (effectiveWeights.ReservedOverlapPenaltyWeight * reservedOverlapPenalty) -
            (effectiveWeights.EdgeMarginPenaltyWeight * edgeMarginPenalty) -
            (effectiveWeights.CompactnessPenaltyWeight * compactnessPenalty);

        result.Breakdown = new DrawingLayoutScoreBreakdown
        {
            ScoredViewCount = scoredViews.Count,
            NonDetailViewCount = scoredViews.Count(static view => !view.IsDetail && view.Scale > Epsilon),
            BBoxRectCount = scoredViews.Count(static view => view.RectSource == "bbox"),
            FallbackRectCount = scoredViews.Count(static view => view.RectSource == "origin-size"),
            SheetArea = sheetArea,
            ReservedAreaUnion = reservedAreaUnion,
            AvailableSheetArea = availableSheetArea,
            TotalViewArea = totalViewArea,
            FillRatioRaw = fillRatioRaw,
            FillRatioScore = fillRatioScore,
            UniformScaleScore = uniformScaleScore,
            ViewOverlapCount = viewOverlapCount,
            ViewOverlapArea = viewOverlapArea,
            ViewOverlapPenalty = viewOverlapPenalty,
            ReservedOverlapCount = reservedOverlapCount,
            ReservedOverlapArea = reservedOverlapArea,
            ReservedOverlapPenalty = reservedOverlapPenalty,
            EdgeMarginPenalty = edgeMarginPenalty,
            PreferredSidePenalty = 0.0,
            CompactnessPenalty = compactnessPenalty,
            FillRatioWeight = effectiveWeights.FillRatioWeight,
            UniformScaleWeight = effectiveWeights.UniformScaleWeight,
            ViewOverlapPenaltyWeight = effectiveWeights.ViewOverlapPenaltyWeight,
            ReservedOverlapPenaltyWeight = effectiveWeights.ReservedOverlapPenaltyWeight,
            EdgeMarginPenaltyWeight = effectiveWeights.EdgeMarginPenaltyWeight,
            PreferredSidePenaltyWeight = effectiveWeights.PreferredSidePenaltyWeight,
            CompactnessPenaltyWeight = effectiveWeights.CompactnessPenaltyWeight
        };

        if (sheetArea <= Epsilon)
            diagnostics.Add("score:sheet-area-unavailable");

        if (availableSheetArea <= Epsilon)
            diagnostics.Add("score:available-sheet-area-unavailable");

        if (scoredViews.Count == 0)
            diagnostics.Add("score:no-view-rects");

        return result;
    }

    private static double ComputePreferredSidePenalty(IReadOnlyList<DrawingLayoutCandidateView> views)
    {
        var comparableCount = 0;
        var mismatchCount = 0;

        foreach (var view in views)
        {
            if (IsPlacementSideMissing(view.PreferredPlacementSide) ||
                IsPlacementSideMissing(view.ActualPlacementSide))
            {
                continue;
            }

            comparableCount++;
            if (!string.Equals(
                    view.PreferredPlacementSide.Trim(),
                    view.ActualPlacementSide.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                mismatchCount++;
            }
        }

        return comparableCount == 0
            ? 0.0
            : (double)mismatchCount / comparableCount;
    }

    private static bool IsPlacementSideMissing(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return string.Equals(value.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase);
    }

    private static double ComputeCompactnessPenalty(
        IReadOnlyList<ScoredViewRect> views,
        double availableSheetArea)
    {
        if (views.Count == 0 || availableSheetArea <= Epsilon)
            return 0.0;

        var bounds = new ReservedRect(
            views.Min(static view => view.Rect.MinX),
            views.Min(static view => view.Rect.MinY),
            views.Max(static view => view.Rect.MaxX),
            views.Max(static view => view.Rect.MaxY));

        var boundsArea = Math.Max(bounds.Width, 0.0) * Math.Max(bounds.Height, 0.0);
        return Math.Min(boundsArea / availableSheetArea, 1.0);
    }

    private static List<ScoredViewRect> BuildViewRects(
        IReadOnlyList<DrawingLayoutViewItem> views,
        List<string> diagnostics)
    {
        var result = new List<ScoredViewRect>(views.Count);
        foreach (var view in views)
        {
            if (view.LayoutRect == null)
            {
                diagnostics.Add($"score:view-rect-missing:view={view.Id}");
                continue;
            }

            result.Add(new ScoredViewRect(
                view.Id,
                view.SemanticKind,
                view.Scale,
                view.LayoutRect,
                view.LayoutRectSource));
        }

        return result;
    }

    private static double ComputeUniformScaleScore(
        IReadOnlyList<ScoredViewRect> views,
        List<string> diagnostics)
    {
        var scales = views
            .Where(static view => !view.IsDetail && view.Scale > Epsilon)
            .Select(static view => view.Scale)
            .ToList();

        if (scales.Count <= 1)
        {
            if (scales.Count == 0)
                diagnostics.Add("score:uniform-scale-no-non-detail-views");

            return 1.0;
        }

        var minScale = scales.Min();
        var maxScale = scales.Max();
        if (maxScale <= Epsilon)
            return 1.0;

        return Math.Max(0.0, Math.Min(minScale / maxScale, 1.0));
    }

    private static double ComputeEdgeMarginPenalty(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<ScoredViewRect> views)
    {
        if (views.Count == 0)
            return 0.0;

        var bounds = new ReservedRect(
            views.Min(static view => view.Rect.MinX),
            views.Min(static view => view.Rect.MinY),
            views.Max(static view => view.Rect.MaxX),
            views.Max(static view => view.Rect.MaxY));
        var margin = Math.Max(workspace.Margin, 0.0);
        var usableWidth = workspace.SheetWidth - (2 * margin);
        var usableHeight = workspace.SheetHeight - (2 * margin);
        var available = Math.Min(usableWidth, usableHeight);
        if (available <= Epsilon)
            return 0.0;

        var safeDistance = Math.Min(Math.Max(margin, 10.0), available * 0.25);
        if (safeDistance <= Epsilon)
            return 0.0;

        var left = bounds.MinX - margin;
        var right = (workspace.SheetWidth - margin) - bounds.MaxX;
        var bottom = bounds.MinY - margin;
        var top = (workspace.SheetHeight - margin) - bounds.MaxY;

        return (ComputeEdgeShortfall(left, safeDistance)
                + ComputeEdgeShortfall(right, safeDistance)
                + ComputeEdgeShortfall(bottom, safeDistance)
                + ComputeEdgeShortfall(top, safeDistance)) * 0.05;
    }

    private static double ComputeEdgeShortfall(double distance, double safeDistance)
        => distance >= safeDistance ? 0.0 : (safeDistance - Math.Max(distance, 0.0)) / safeDistance;

    private static double ComputeUnionArea(IReadOnlyList<ReservedRect> rects)
    {
        var validRects = rects
            .Where(static rect => rect.Width > Epsilon && rect.Height > Epsilon)
            .ToList();
        if (validRects.Count == 0)
            return 0.0;

        var xs = validRects
            .SelectMany(static rect => new[] { rect.MinX, rect.MaxX })
            .Distinct()
            .OrderBy(static x => x)
            .ToList();

        var area = 0.0;
        for (var i = 0; i < xs.Count - 1; i++)
        {
            var x1 = xs[i];
            var x2 = xs[i + 1];
            var width = x2 - x1;
            if (width <= Epsilon)
                continue;

            var active = validRects
                .Where(rect => rect.MinX < x2 - Epsilon && rect.MaxX > x1 + Epsilon)
                .Select(static rect => (rect.MinY, rect.MaxY))
                .OrderBy(static interval => interval.MinY)
                .ToList();
            if (active.Count == 0)
                continue;

            var coveredY = 0.0;
            var currentMin = active[0].MinY;
            var currentMax = active[0].MaxY;
            for (var j = 1; j < active.Count; j++)
            {
                var interval = active[j];
                if (interval.MinY <= currentMax + Epsilon)
                {
                    currentMax = Math.Max(currentMax, interval.MaxY);
                    continue;
                }

                coveredY += currentMax - currentMin;
                currentMin = interval.MinY;
                currentMax = interval.MaxY;
            }

            coveredY += currentMax - currentMin;
            area += width * coveredY;
        }

        return area;
    }

    private static bool TryGetOverlapArea(
        ReservedRect first,
        ReservedRect second,
        out double area)
    {
        if (!TryIntersect(first, second, out var intersection))
        {
            area = 0.0;
            return false;
        }

        area = intersection.Width * intersection.Height;
        return area > Epsilon;
    }

    private static bool TryIntersect(
        ReservedRect first,
        ReservedRect second,
        out ReservedRect intersection)
    {
        var minX = Math.Max(first.MinX, second.MinX);
        var minY = Math.Max(first.MinY, second.MinY);
        var maxX = Math.Min(first.MaxX, second.MaxX);
        var maxY = Math.Min(first.MaxY, second.MaxY);

        if (maxX <= minX + Epsilon || maxY <= minY + Epsilon)
        {
            intersection = null!;
            return false;
        }

        intersection = new ReservedRect(minX, minY, maxX, maxY);
        return true;
    }

    private sealed class ScoredViewRect
    {
        public ScoredViewRect(
            int viewId,
            string semanticKind,
            double scale,
            ReservedRect rect,
            string rectSource)
        {
            ViewId = viewId;
            SemanticKind = semanticKind;
            Scale = scale;
            Rect = rect;
            RectSource = rectSource;
        }

        public int ViewId { get; }

        public string SemanticKind { get; }

        public double Scale { get; }

        public ReservedRect Rect { get; }

        public string RectSource { get; }

        public bool IsDetail => string.Equals(SemanticKind, "Detail", StringComparison.OrdinalIgnoreCase);
    }
}
