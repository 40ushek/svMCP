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
            score += CalculateCrowdingPenalty(candidate, item, placement, options);
        }

        score += candidate.Priority * options.CandidatePriorityWeight;
        score += Distance(candidate.X, candidate.Y, item.CurrentX, item.CurrentY) * options.CurrentPositionWeight;
        score += CalculatePreferredSidePenalty(candidate, item, options);

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

    private static double CalculateCrowdingPenalty(
        MarkCandidate candidate,
        MarkLayoutItem item,
        MarkLayoutPlacement placement,
        MarkLayoutOptions options)
    {
        var desiredDx = ((item.Width + placement.Width) / 2.0) + options.Gap;
        var desiredDy = ((item.Height + placement.Height) / 2.0) + options.Gap;
        var actualDx = Math.Abs(candidate.X - placement.X);
        var actualDy = Math.Abs(candidate.Y - placement.Y);

        if (actualDx >= desiredDx || actualDy >= desiredDy)
            return 0;

        var shortfallX = desiredDx - actualDx;
        var shortfallY = desiredDy - actualDy;
        return (shortfallX + shortfallY) * options.CrowdingPenaltyWeight;
    }

    private static double CalculatePreferredSidePenalty(
        MarkCandidate candidate,
        MarkLayoutItem item,
        MarkLayoutOptions options)
    {
        if (!item.HasLeaderLine)
            return 0;

        var currentSignX = GetSign(item.CurrentX - item.AnchorX);
        var currentSignY = GetSign(item.CurrentY - item.AnchorY);
        var candidateSignX = GetSign(candidate.X - item.AnchorX);
        var candidateSignY = GetSign(candidate.Y - item.AnchorY);

        var penalty = 0.0;
        if (currentSignX != 0 && candidateSignX != 0 && currentSignX != candidateSignX)
            penalty += options.PreferredSidePenaltyWeight;

        if (currentSignY != 0 && candidateSignY != 0 && currentSignY != candidateSignY)
            penalty += options.PreferredSidePenaltyWeight;

        return penalty;
    }

    private static int GetSign(double value)
    {
        if (value > 0.01) return 1;
        if (value < -0.01) return -1;
        return 0;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
