using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Algorithms.Marks;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class LeaderTextCleanupMarkDryRunResult
{
    public int MarkId { get; set; }
    public double CurrentSeverity { get; set; }
    public bool HasImprovement { get; set; }
    public double BestProjectedSeverity { get; set; }
    public double BestDeltaSeverity { get; set; }
    public LeaderAnchorCandidateKind BestKind { get; set; }
    public double BestAnchorX { get; set; }
    public double BestAnchorY { get; set; }
}

internal sealed class LeaderTextCleanupDryRunResult
{
    public int ConflictingMarks { get; set; }
    public int ImprovableMarks { get; set; }
    public double TotalCurrentSeverity { get; set; }
    public double TotalBestCaseSeverity { get; set; }
    public List<LeaderTextCleanupMarkDryRunResult> Marks { get; } = new List<LeaderTextCleanupMarkDryRunResult>();
}

internal static class LeaderTextCleanupDryRunner
{
    private const double LeaderAnchorDepthPaperMm = 10.0;
    private const double LeaderAnchorFarEdgeClearancePaperMm = 5.0;
    private const double ImprovementThreshold = 0.001;

    public static LeaderTextCleanupDryRunResult Analyze(
        IReadOnlyList<LeaderTextOverlapMark> overlapMarks,
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, List<double[]>> partPolygons,
        double viewScale,
        double ownEndIgnoreDistance)
    {
        var result = new LeaderTextCleanupDryRunResult();
        var summary = LeaderTextOverlapAnalyzer.Analyze(overlapMarks, ownEndIgnoreDistance);
        if (summary.TotalCrossings == 0)
            return result;

        var severityByMarkId = summary.Conflicts
            .GroupBy(static c => c.MarkId)
            .ToDictionary(static g => g.Key, static g => g.Sum(static c => c.Severity));

        var overlapMarkById = overlapMarks.ToDictionary(static m => m.MarkId);
        var entryById = entries
            .Where(static e => e.Item.HasLeaderLine)
            .ToDictionary(e => e.Mark.GetIdentifier().ID);

        var resolvedScale = viewScale > 0 ? viewScale : 1.0;
        var depthMm = LeaderAnchorDepthPaperMm * resolvedScale;
        var farEdgeClearanceMm = LeaderAnchorFarEdgeClearancePaperMm * resolvedScale;

        result.ConflictingMarks = severityByMarkId.Count;
        result.TotalCurrentSeverity = severityByMarkId.Values.Sum();

        foreach (var pair in severityByMarkId)
        {
            var markId = pair.Key;
            var currentSeverity = pair.Value;

            if (!entryById.TryGetValue(markId, out var entry))
                continue;
            if (!overlapMarkById.TryGetValue(markId, out var overlapMark))
                continue;
            if (!entry.Item.SourceModelId.HasValue)
                continue;
            if (!partPolygons.TryGetValue(entry.Item.SourceModelId.Value, out var polygon))
                continue;
            if (entry.MarkContext.LeaderSnapshot == null)
                continue;

            var snapshot = entry.MarkContext.LeaderSnapshot;
            var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
                polygon, snapshot, depthMm, farEdgeClearanceMm);

            if (candidates.Count == 0)
                continue;

            var leaderEndX = overlapMark.LeaderPolyline.Count >= 2
                ? overlapMark.LeaderPolyline[overlapMark.LeaderPolyline.Count - 1][0] : 0;
            var leaderEndY = overlapMark.LeaderPolyline.Count >= 2
                ? overlapMark.LeaderPolyline[overlapMark.LeaderPolyline.Count - 1][1] : 0;

            var markResult = new LeaderTextCleanupMarkDryRunResult
            {
                MarkId = markId,
                CurrentSeverity = currentSeverity,
                BestProjectedSeverity = currentSeverity,
                BestDeltaSeverity = 0,
            };

            foreach (var candidate in candidates)
            {
                if (candidate.AnchorPoint == null)
                    continue;

                var simulatedPolyline = new List<double[]>
                {
                    new[] { candidate.AnchorPoint.X, candidate.AnchorPoint.Y },
                    new[] { leaderEndX, leaderEndY }
                };

                var projectedSeverity = ScoreLeaderAgainstTextPolygons(
                    simulatedPolyline, markId, overlapMark, overlapMarks, ownEndIgnoreDistance);
                var delta = projectedSeverity - currentSeverity;

                if (delta < markResult.BestDeltaSeverity)
                {
                    markResult.BestDeltaSeverity = delta;
                    markResult.BestProjectedSeverity = projectedSeverity;
                    markResult.BestKind = candidate.Kind;
                    markResult.BestAnchorX = candidate.AnchorPoint.X;
                    markResult.BestAnchorY = candidate.AnchorPoint.Y;
                }
            }

            markResult.HasImprovement = markResult.BestDeltaSeverity < -ImprovementThreshold;
            result.Marks.Add(markResult);

            if (markResult.HasImprovement)
            {
                result.ImprovableMarks++;
                result.TotalBestCaseSeverity += markResult.BestProjectedSeverity;
            }
            else
            {
                result.TotalBestCaseSeverity += currentSeverity;
            }
        }

        return result;
    }

    private static double ScoreLeaderAgainstTextPolygons(
        List<double[]> simulatedPolyline,
        int markId,
        LeaderTextOverlapMark originalMark,
        IReadOnlyList<LeaderTextOverlapMark> allMarks,
        double ownEndIgnoreDistance)
    {
        var testMark = new LeaderTextOverlapMark
        {
            MarkId = markId,
            TextPolygon = originalMark.TextPolygon,
            LeaderPolyline = simulatedPolyline
        };
        var testList = allMarks
            .Select(m => m.MarkId == markId ? testMark : m)
            .ToList();
        return LeaderTextOverlapAnalyzer.Analyze(testList, ownEndIgnoreDistance).Conflicts
            .Where(c => c.MarkId == markId)
            .Sum(static c => c.Severity);
    }
}
