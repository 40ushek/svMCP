using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class SimpleMarkCandidateGenerator : IMarkCandidateGenerator
{
    // Scale factor applied to baseOffset when generating axis/local candidates
    // (keeps candidates closer to anchor than leader-line rings)
    private const double LocalCandidateScale = 0.6;

    // 8 compass directions: E, NE, N, NW, W, SW, S, SE
    private static readonly (double Dx, double Dy)[] Directions =
    {
        ( 1,  0), ( 1,  1), ( 0,  1), (-1,  1),
        (-1,  0), (-1, -1), ( 0, -1), ( 1, -1)
    };

    public IReadOnlyList<MarkCandidate> GenerateCandidates(MarkLayoutItem item, MarkLayoutOptions options)
    {
        var baseOffsetX = (item.Width  / 2.0) + options.CandidateOffset + options.Gap;
        var baseOffsetY = (item.Height / 2.0) + options.CandidateOffset + options.Gap;

        var candidates = item.HasLeaderLine
            ? BuildLeaderCandidates(item, baseOffsetX, baseOffsetY, options.CandidateDistanceMultipliers)
            : item.HasAxis
                ? BuildAxisCandidates(item, ComputeAxisBaseOffset(item, options), options.CandidateDistanceMultipliers)
            : BuildLocalCandidates(item, baseOffsetX, baseOffsetY, options.CandidateDistanceMultipliers);

        var filtered = candidates
            .Where(c => IsWithinAnchorDistance(c, item, options))
            .Where(c => !item.HasBounds || IsWithinBounds(c, item))
            .ToList();

        return RemoveNearDuplicates(filtered);
    }

    /// <summary>
    /// For leader-line marks: rings of candidates around the anchor point at each distance
    /// multiplier, with priority ordered so closer positions and same-quadrant positions
    /// score better before the cost evaluator applies its own weights.
    /// </summary>
    private static List<MarkCandidate> BuildLeaderCandidates(
        MarkLayoutItem item,
        double baseOffsetX,
        double baseOffsetY,
        double[] multipliers)
    {
        var result = new List<MarkCandidate>
        {
            // Priority 0: keep current position — always the cheapest first try
            new() { X = item.CurrentX, Y = item.CurrentY, Priority = 0 }
        };

        // Determine the quadrant of the current mark relative to its anchor so we
        // can favour same-quadrant candidates (lower priority number = preferred).
        var currentSignX = Math.Sign(item.CurrentX - item.AnchorX);
        var currentSignY = Math.Sign(item.CurrentY - item.AnchorY);

        var priority = 1;
        foreach (var multiplier in multipliers.OrderBy(m => m))
        {
            var ox = baseOffsetX * multiplier;
            var oy = baseOffsetY * multiplier;

            // Sort directions: same quadrant first, then adjacent, then opposite.
            var ordered = Directions
                .Select(d => (d.Dx, d.Dy, Affinity: QuadrantAffinity(d.Dx, d.Dy, currentSignX, currentSignY)))
                .OrderBy(d => d.Affinity)
                .Select(d => (d.Dx, d.Dy));

            foreach (var (dx, dy) in ordered)
            {
                result.Add(new MarkCandidate
                {
                    X        = item.AnchorX + dx * ox,
                    Y        = item.AnchorY + dy * oy,
                    Priority = priority++
                });
            }
        }

        return result;
    }

    private static List<MarkCandidate> BuildAxisCandidates(
        MarkLayoutItem item,
        double baseOffset,
        double[] multipliers)
    {
        var result = new List<MarkCandidate>
        {
            new() { X = item.CurrentX, Y = item.CurrentY, Priority = 0 }
        };

        var priority = 1;
        foreach (var multiplier in multipliers.OrderBy(m => m))
        {
            // Generate from AnchorX/Y (fixed part position) so that
            // IsWithinAnchorDistance can correctly bound the final position.
            var distance = Math.Max(baseOffset * multiplier, 2.0);
            result.Add(new MarkCandidate
            {
                X = item.AnchorX + (item.AxisDx * distance),
                Y = item.AnchorY + (item.AxisDy * distance),
                Priority = priority++
            });
            result.Add(new MarkCandidate
            {
                X = item.AnchorX - (item.AxisDx * distance),
                Y = item.AnchorY - (item.AxisDy * distance),
                Priority = priority++
            });
        }

        return result;
    }

    /// <summary>
    /// For non-leader marks: rings around the current position at each distance multiplier.
    /// </summary>
    private static List<MarkCandidate> BuildLocalCandidates(
        MarkLayoutItem item,
        double baseOffsetX,
        double baseOffsetY,
        double[] multipliers)
    {
        var result = new List<MarkCandidate>
        {
            new() { X = item.CurrentX, Y = item.CurrentY, Priority = 0 }
        };

        var priority = 1;
        foreach (var multiplier in multipliers.OrderBy(m => m))
        {
            var ox = Math.Max(baseOffsetX * multiplier * LocalCandidateScale, 2.0);
            var oy = Math.Max(baseOffsetY * multiplier * LocalCandidateScale, 2.0);

            foreach (var (dx, dy) in Directions)
            {
                result.Add(new MarkCandidate
                {
                    X        = item.CurrentX + dx * ox,
                    Y        = item.CurrentY + dy * oy,
                    Priority = priority++
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Returns 0 for same quadrant as current, 1 for adjacent, 2 for opposite.
    /// Used to order candidates so the cost evaluator sees preferred positions first
    /// (lower Priority → lower CandidatePriorityWeight cost).
    /// </summary>
    private static int QuadrantAffinity(double dx, double dy, int signX, int signY)
    {
        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        var matchX = signX == 0 || sx == 0 || sx == signX;
        var matchY = signY == 0 || sy == 0 || sy == signY;
        if (matchX && matchY) return 0;
        if (matchX || matchY) return 1;
        return 2;
    }

    private static bool IsWithinBounds(MarkCandidate c, MarkLayoutItem item)
    {
        var hw = item.Width  / 2.0;
        var hh = item.Height / 2.0;
        return c.X - hw >= item.BoundsMinX && c.X + hw <= item.BoundsMaxX &&
               c.Y - hh >= item.BoundsMinY && c.Y + hh <= item.BoundsMaxY;
    }

    private static bool IsWithinAnchorDistance(MarkCandidate candidate, MarkLayoutItem item, MarkLayoutOptions options)
    {
        if (options.MaxDistanceFromAnchor <= 0)
            return true;

        var maxDistance = options.MaxDistanceFromAnchor;

        // For non-axis, non-leader marks: limit drift to the mark's shorter dimension.
        // For axis marks (baseline): movement is already constrained to the axis in
        // ApplyPlacements, so only use MaxDistanceFromAnchor.
        if (!item.HasLeaderLine && !item.HasAxis)
            maxDistance = Math.Min(maxDistance, Math.Min(item.Width, item.Height) + options.Gap);

        var dx = candidate.X - item.AnchorX;
        var dy = candidate.Y - item.AnchorY;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= maxDistance;
    }

    /// <summary>
    /// For axis candidates, use the half-extent along the axis direction so marks are
    /// spaced by their actual along-axis size, not the (usually smaller) perpendicular size.
    /// </summary>
    private static double ComputeAxisBaseOffset(MarkLayoutItem item, MarkLayoutOptions options)
    {
        double axisHalfExtent;
        if (item.LocalCorners.Count >= 3)
        {
            axisHalfExtent = item.LocalCorners
                .Select(c => Math.Abs((c[0] * item.AxisDx) + (c[1] * item.AxisDy)))
                .DefaultIfEmpty(0.0)
                .Max();
        }
        else
        {
            axisHalfExtent = (Math.Abs(item.AxisDx) * (item.Width / 2.0))
                           + (Math.Abs(item.AxisDy) * (item.Height / 2.0));
        }

        return axisHalfExtent + options.CandidateOffset + options.Gap;
    }

    private static IReadOnlyList<MarkCandidate> RemoveNearDuplicates(IEnumerable<MarkCandidate> candidates)
    {
        const double epsilon = 0.1;
        var result = new List<MarkCandidate>();

        foreach (var candidate in candidates.OrderBy(x => x.Priority))
        {
            if (result.Any(existing =>
                    Math.Abs(existing.X - candidate.X) < epsilon &&
                    Math.Abs(existing.Y - candidate.Y) < epsilon))
                continue;

            result.Add(candidate);
        }

        return result;
    }
}
