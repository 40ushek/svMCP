using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Drawing;

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
        score += Distance(candidate.X, candidate.Y, item.AnchorX, item.AnchorY) * options.AnchorDistanceWeight;
        score += CalculateSourceDistancePenalty(candidate, item, options);
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
        if (item.LocalCorners.Count >= 3 && placement.LocalCorners.Count >= 3)
        {
            var candidatePolygon = PolygonGeometry.Translate(item.LocalCorners, candidate.X, candidate.Y);
            var placementPolygon = PolygonGeometry.Translate(placement.LocalCorners, placement.X, placement.Y);
            if (!PolygonGeometry.Intersects(candidatePolygon, placementPolygon))
            {
                PolygonGeometry.GetBounds(candidatePolygon, out var candidateMinX, out var candidateMinY, out var candidateMaxX, out var candidateMaxY);
                PolygonGeometry.GetBounds(placementPolygon, out var placementMinX, out var placementMinY, out var placementMaxX, out var placementMaxY);

                if (!PolygonGeometry.RectanglesOverlap(
                        candidateMinX - (options.Gap / 2.0),
                        candidateMinY - (options.Gap / 2.0),
                        candidateMaxX + (options.Gap / 2.0),
                        candidateMaxY + (options.Gap / 2.0),
                        placementMinX - (options.Gap / 2.0),
                        placementMinY - (options.Gap / 2.0),
                        placementMaxX + (options.Gap / 2.0),
                        placementMaxY + (options.Gap / 2.0)))
                    return 0;

                var shortfallX = Math.Min(candidateMaxX + options.Gap, placementMaxX + options.Gap)
                    - Math.Max(candidateMinX - options.Gap, placementMinX - options.Gap);
                var shortfallY = Math.Min(candidateMaxY + options.Gap, placementMaxY + options.Gap)
                    - Math.Max(candidateMinY - options.Gap, placementMinY - options.Gap);
                return Math.Max(shortfallX, 0) + Math.Max(shortfallY, 0);
            }

            return options.OverlapPenalty;
        }

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

    private static double CalculateSourceDistancePenalty(
        MarkCandidate candidate,
        MarkLayoutItem item,
        MarkLayoutOptions options)
    {
        if (item.HasLeaderLine || !item.HasSourceCenter || options.SourceDistanceWeight <= 0)
            return 0;

        return Distance(
                   candidate.X,
                   candidate.Y,
                   item.SourceCenterX!.Value,
                   item.SourceCenterY!.Value)
               * options.SourceDistanceWeight;
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
