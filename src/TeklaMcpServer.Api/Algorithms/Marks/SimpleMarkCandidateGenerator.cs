using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class SimpleMarkCandidateGenerator : IMarkCandidateGenerator
{
    // Scale factor applied to baseOffset when generating axis/local candidates.
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

        if (item.HasLeaderLine)
        {
            if (TryBuildSourceAwareLeaderCandidates(item, options, baseOffsetX, baseOffsetY, out var sourceAwareCandidates))
            {
                var filteredSourceAware = sourceAwareCandidates
                    .Where(c => IsWithinAnchorDistance(c, item, options))
                    .Where(c => !item.HasBounds || IsWithinBounds(c, item))
                    .ToList();

                if (filteredSourceAware.Count > 0)
                    return RemoveNearDuplicates(filteredSourceAware);
            }

            var localCandidates = BuildLocalCandidates(item, baseOffsetX, baseOffsetY, options.CandidateDistanceMultipliers)
                .Where(c => IsWithinAnchorDistance(c, item, options))
                .Where(c => !item.HasBounds || IsWithinBounds(c, item))
                .ToList();

            return RemoveNearDuplicates(localCandidates);
        }

        var candidates = item.HasAxis
                ? BuildAxisCandidates(item, ComputeAxisBaseOffset(item, options), options.CandidateDistanceMultipliers)
                : BuildLocalCandidates(item, baseOffsetX, baseOffsetY, options.CandidateDistanceMultipliers);

        var filtered = candidates
            .Where(c => IsWithinAnchorDistance(c, item, options))
            .Where(c => !item.HasBounds || IsWithinBounds(c, item))
            .ToList();

        return RemoveNearDuplicates(filtered);
    }

    private static bool TryBuildSourceAwareLeaderCandidates(
        MarkLayoutItem item,
        MarkLayoutOptions options,
        double baseOffsetX,
        double baseOffsetY,
        out List<MarkCandidate> candidates)
    {
        candidates = new List<MarkCandidate>();

        if (item.SourceKind != MarkLayoutSourceKind.Part ||
            !item.SourceModelId.HasValue ||
            !options.PartPolygonsByModelId.TryGetValue(item.SourceModelId.Value, out var polygon) ||
            polygon.Count < 3 ||
            !TryGetPolygonBounds(polygon, out var minX, out var minY, out var maxX, out var maxY))
        {
            return false;
        }

        var sourceWidth = maxX - minX;
        var sourceHeight = maxY - minY;
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;
        var shiftX = item.Width + options.Gap;
        var shiftY = item.Height + options.Gap;
        var aboveY = maxY + baseOffsetY;
        var belowY = minY - baseOffsetY;
        var leftX = minX - baseOffsetX;
        var rightX = maxX + baseOffsetX;
        var priority = 1;

        candidates.Add(new MarkCandidate { X = item.CurrentX, Y = item.CurrentY, Priority = 0 });

        if (sourceWidth >= sourceHeight * 1.5)
        {
            AddHorizontalCandidates(candidates, centerX, aboveY, belowY, shiftX, item.CurrentX, priority);
            return true;
        }

        if (sourceHeight >= sourceWidth * 1.5)
        {
            AddVerticalCandidates(candidates, leftX, rightX, centerY, shiftY, item.CurrentY, priority);
            return true;
        }

        AddCompactCandidates(candidates, centerX, centerY, aboveY, belowY, leftX, rightX, shiftX, shiftY, item.CurrentX, item.CurrentY, priority);
        return true;
    }

    private static void AddHorizontalCandidates(
        List<MarkCandidate> candidates,
        double centerX,
        double aboveY,
        double belowY,
        double shiftX,
        double currentX,
        int priority)
    {
        var preferredShift = GetPreferredShift(currentX - centerX, shiftX);
        var oppositeShift = -preferredShift;

        candidates.Add(new MarkCandidate { X = centerX, Y = aboveY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX, Y = belowY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX + preferredShift, Y = aboveY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX + oppositeShift, Y = aboveY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX + preferredShift, Y = belowY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX + oppositeShift, Y = belowY, Priority = priority });
    }

    private static void AddVerticalCandidates(
        List<MarkCandidate> candidates,
        double leftX,
        double rightX,
        double centerY,
        double shiftY,
        double currentY,
        int priority)
    {
        var preferredShift = GetPreferredShift(currentY - centerY, shiftY);
        var oppositeShift = -preferredShift;

        candidates.Add(new MarkCandidate { X = leftX, Y = centerY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = rightX, Y = centerY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = leftX, Y = centerY + preferredShift, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = leftX, Y = centerY + oppositeShift, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = rightX, Y = centerY + preferredShift, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = rightX, Y = centerY + oppositeShift, Priority = priority });
    }

    private static void AddCompactCandidates(
        List<MarkCandidate> candidates,
        double centerX,
        double centerY,
        double aboveY,
        double belowY,
        double leftX,
        double rightX,
        double shiftX,
        double shiftY,
        double currentX,
        double currentY,
        int priority)
    {
        candidates.Add(new MarkCandidate { X = centerX, Y = aboveY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX, Y = belowY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = leftX, Y = centerY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = rightX, Y = centerY, Priority = priority++ });

        var horizontalShift = GetPreferredShift(currentX - centerX, shiftX);
        var verticalShift = GetPreferredShift(currentY - centerY, shiftY);

        candidates.Add(new MarkCandidate { X = centerX + horizontalShift, Y = aboveY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = centerX + horizontalShift, Y = belowY, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = leftX, Y = centerY + verticalShift, Priority = priority++ });
        candidates.Add(new MarkCandidate { X = rightX, Y = centerY + verticalShift, Priority = priority });
    }

    private static double GetPreferredShift(double delta, double shift)
    {
        var sign = Math.Sign(delta);
        if (sign == 0)
            sign = 1;

        return sign * shift;
    }

    private static bool TryGetPolygonBounds(
        IReadOnlyList<double[]> polygon,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        if (polygon.Count < 3 || polygon.Any(point => point.Length < 2))
        {
            minX = 0.0;
            minY = 0.0;
            maxX = 0.0;
            maxY = 0.0;
            return false;
        }

        PolygonGeometry.GetBounds(polygon, out minX, out minY, out maxX, out maxY);
        return true;
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
            // Axis-bound marks slide along the axis passing through their current center.
            // In the drawing pipeline baseline marks are collected with Anchor == Current,
            // so using CurrentX/Y preserves production behavior and keeps the axis-candidate
            // contract consistent for standalone tests as well.
            var distance = Math.Max(baseOffset * multiplier, 2.0);
            result.Add(new MarkCandidate
            {
                X = item.CurrentX + (item.AxisDx * distance),
                Y = item.CurrentY + (item.AxisDy * distance),
                Priority = priority++
            });
            result.Add(new MarkCandidate
            {
                X = item.CurrentX - (item.AxisDx * distance),
                Y = item.CurrentY - (item.AxisDy * distance),
                Priority = priority++
            });
        }

        return result;
    }

    /// <summary>
    /// Generates local candidates around the source center (when known) or current position.
    /// This keeps candidates near the associated object instead of orbiting the leader anchor.
    /// </summary>
    private static List<MarkCandidate> BuildLocalCandidates(
        MarkLayoutItem item,
        double baseOffsetX,
        double baseOffsetY,
        double[] multipliers)
    {
        // Prefer source center as ring origin so candidates cluster near the associated part.
        var ringX = item.HasSourceCenter ? item.SourceCenterX!.Value : item.CurrentX;
        var ringY = item.HasSourceCenter ? item.SourceCenterY!.Value : item.CurrentY;

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
                    X        = ringX + dx * ox,
                    Y        = ringY + dy * oy,
                    Priority = priority++
                });
            }
        }

        return result;
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

        // For non-leader, non-axis marks with a known source center: allow movement up to
        // MaxDistanceFromAnchor from the source center so marks can reach their objects.
        // Without a source center, fall back to the tight anchor-relative clamp.
        if (!item.HasLeaderLine && !item.HasAxis)
        {
            if (item.HasSourceCenter)
            {
                var dsx = candidate.X - item.SourceCenterX!.Value;
                var dsy = candidate.Y - item.SourceCenterY!.Value;
                return Math.Sqrt((dsx * dsx) + (dsy * dsy)) <= options.MaxDistanceFromAnchor;
            }

            var maxDistance = Math.Min(options.MaxDistanceFromAnchor, Math.Min(item.Width, item.Height) + options.Gap);
            var dx = candidate.X - item.AnchorX;
            var dy = candidate.Y - item.AnchorY;
            return Math.Sqrt((dx * dx) + (dy * dy)) <= maxDistance;
        }

        var ddx = candidate.X - item.AnchorX;
        var ddy = candidate.Y - item.AnchorY;
        return Math.Sqrt((ddx * ddx) + (ddy * ddy)) <= options.MaxDistanceFromAnchor;
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
