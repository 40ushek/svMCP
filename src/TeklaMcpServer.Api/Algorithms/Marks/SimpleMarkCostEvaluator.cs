using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class SimpleMarkCostEvaluator : IMarkCostEvaluator
{
    public double EvaluateCandidate(
        MarkLayoutItem item,
        MarkCandidate candidate,
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutOptions options)
    {
        var score = 0.0;

        foreach (var placement in placements)
        {
            score += CalculateOverlapPenalty(candidate, item, placement, options);
        }

        score += candidate.Priority * options.CandidatePriorityWeight;
        score += Distance(candidate.X, candidate.Y, item.CurrentX, item.CurrentY) * options.CurrentPositionWeight;

        if (item.HasLeaderLine)
            score += Distance(candidate.X, candidate.Y, item.AnchorX, item.AnchorY) * options.LeaderLengthWeight;

        return score;
    }

    private static double CalculateOverlapPenalty(
        MarkCandidate candidate,
        MarkLayoutItem item,
        MarkLayoutPlacement placement,
        MarkLayoutOptions options)
    {
        var halfWidthA = item.Width / 2.0;
        var halfHeightA = item.Height / 2.0;
        var halfWidthB = placement.Width / 2.0;
        var halfHeightB = placement.Height / 2.0;

        var leftA = candidate.X - halfWidthA;
        var rightA = candidate.X + halfWidthA;
        var bottomA = candidate.Y - halfHeightA;
        var topA = candidate.Y + halfHeightA;

        var leftB = placement.X - halfWidthB;
        var rightB = placement.X + halfWidthB;
        var bottomB = placement.Y - halfHeightB;
        var topB = placement.Y + halfHeightB;

        var overlapX = Math.Min(rightA, rightB) - Math.Max(leftA, leftB);
        var overlapY = Math.Min(topA, topB) - Math.Max(bottomA, bottomB);

        if (overlapX <= 0 || overlapY <= 0)
            return 0;

        return options.OverlapPenalty + (overlapX * overlapY);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
