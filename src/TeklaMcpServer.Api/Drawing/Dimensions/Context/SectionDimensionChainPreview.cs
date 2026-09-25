using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing.Dimensions;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Conservative, read-only section candidate plan. A projected full solid is not a clipped
/// section, so every answer remains provisional until checked against the actual drawing.
/// </summary>
internal sealed class SectionDimensionChainPreview
{
    private const double SameCoordinate = 0.01;
    private readonly DimensionPointCatalog _catalog;
    private readonly int _mainId;
    private readonly int[] _secondaryIds;
    private readonly double[] _xLevels;
    private readonly double[] _yLevels;
    private readonly double _readableGapViewUnits;
    private readonly GeometryGroupExtent _extent;

    private SectionDimensionChainPreview(DimensionPointCatalog catalog, int mainId,
        int[] secondaryIds, double[] xLevels, double[] yLevels, double scale, GeometryGroupExtent extent)
    {
        _catalog = catalog;
        _mainId = mainId;
        _secondaryIds = secondaryIds;
        _xLevels = xLevels;
        _yLevels = yLevels;
        _readableGapViewUnits = DimensionPlacementSettings.SectionReadabilityGapPaperMm * scale;
        _extent = extent;
    }

    public IReadOnlyList<int> SecondaryIds => _secondaryIds;

    internal sealed class SideResult(object[] chains, int[] locatedPartIds)
    {
        public object[] Chains { get; } = chains;
        public int[] LocatedPartIds { get; } = locatedPartIds;
    }

    public static bool TryCreate(DimensionPointCatalog catalog, GeometryGroup group, int mainId,
        IEnumerable<int> includedIds, double minFeatureLength, double scale,
        out SectionDimensionChainPreview? preview,
        out string? reason)
    {
        preview = null;
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            reason = "view scale is not a finite positive number";
            return false;
        }
        var outer = group.Shapes.Where(s => s.ModelId == mainId && !s.IsHole
            && s.Shape.Kind == PlanarShapeKind.Polygon).ToArray();
        if (outer.Length != 1 || group.Extent == null)
        {
            reason = "section profile needs exactly one projected outer contour of the main part";
            return false;
        }

        var points = outer[0].Shape.Points;
        var xLevels = AxisLevels(points, alongX: false, minFeatureLength);
        var yLevels = AxisLevels(points, alongX: true, minFeatureLength);
        var iProfile = xLevels.Length == 4 && yLevels.Length == 4 &&
            (HasIAxisEdges(points, xLevels, yLevels) || HasIAxisEdges(
                points.Select(p => new Vec3(p.Y, p.X, 0)).ToArray(), yLevels, xLevels));
        var rectangle = xLevels.Length == 2 && yLevels.Length == 2 &&
            HasRectangle(points, xLevels, yLevels);
        if (!iProfile && !rectangle)
        {
            reason = "main-part contour is not a verified rectangular or I profile; profile dimensions need manual selection";
            return false;
        }

        preview = new SectionDimensionChainPreview(catalog, mainId,
            includedIds.Where(id => id != mainId).Distinct().OrderBy(id => id).ToArray(), xLevels, yLevels,
            scale, group.Extent);
        reason = null;
        return true;
    }

    public SideResult Build(DimensionChainSide side)
    {
        var line = _catalog.LinePoints(side);
        var alongX = AlongX(side);
        var levels = alongX ? _xLevels : _yLevels;
        var profile = new List<DimensionPoint>();
        foreach (var level in levels)
        {
            var match = line.Where(p => Owns(p, _mainId) && Math.Abs(Along(p, side) - level) <= SameCoordinate)
                .OrderByDescending(p => Outside(p, side)).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (match == null)
                return Refused("main-profile support is not offered on this side");
            profile.Add(match);
        }

        var location = new List<DimensionPoint> { profile[0], profile[profile.Count - 1] };
        var locatedByProfile = new List<int>();
        var otherSide = new List<int>();
        var candidates = new List<(int Id, DimensionPoint Point)>();
        foreach (var id in _secondaryIds)
        {
            var own = line.Where(p => Owns(p, id)).ToArray();
            if (own.Length == 0)
            {
                continue;
            }
            var allOwn = _catalog.AllPoints.Where(p => Owns(p, id)).ToArray();
            var center = alongX ? (_yLevels[0] + _yLevels[_yLevels.Length - 1]) / 2
                : (_xLevels[0] + _xLevels[_xLevels.Length - 1]) / 2;
            var reachesHigh = allOwn.Any(p => Across(p, side) > center + SameCoordinate);
            var reachesLow = allOwn.Any(p => Across(p, side) < center - SameCoordinate);
            var thisSide = side is DimensionChainSide.Top or DimensionChainSide.Right ? reachesHigh : reachesLow;
            if (!thisSide && (reachesHigh || reachesLow))
            {
                // Keep an otherwise stranded part, but do not claim this side owns it
                // when the opposite preliminary side can carry its points.
                if (_catalog.LinePoints(Opposite(side)).Any(p => Owns(p, id)))
                {
                    otherSide.Add(id);
                    continue;
                }
            }

            var low = own.Min(p => Along(p, side));
            var high = own.Max(p => Along(p, side));
            var wanted = new List<double>();
            if (low < levels[0] - SameCoordinate) wanted.Add(low);
            if (high > levels[levels.Length - 1] + SameCoordinate) wanted.Add(high);
            if (wanted.Count == 0) wanted.Add(low);
            foreach (var coordinate in wanted)
            {
                var pick = own.Where(p => Math.Abs(Along(p, side) - coordinate) <= SameCoordinate)
                    .OrderByDescending(p => Outside(p, side)).ThenBy(p => p.Id, StringComparer.Ordinal).First();
                if (profile.Any(p => Math.Abs(Along(p, side) - Along(pick, side)) <= SameCoordinate))
                    locatedByProfile.Add(id);
                else
                    candidates.Add((id, pick));
            }
        }

        var mergedNearby = new List<int>();
        var located = new List<int>(locatedByProfile);
        DimensionPoint? last = null;
        foreach (var candidate in candidates.OrderBy(c => Along(c.Point, side)).ThenBy(c => c.Id))
        {
            if (last != null && Along(candidate.Point, side) - Along(last, side) < _readableGapViewUnits)
                mergedNearby.Add(candidate.Id);
            else
            {
                location.Add(candidate.Point);
                last = candidate.Point;
            }
            located.Add(candidate.Id);
        }

        var lowOverall = alongX ? _extent.MinX : _extent.MinY;
        var highOverall = alongX ? _extent.MaxX : _extent.MaxY;
        var locationLow = location.Min(p => Along(p, side));
        var locationHigh = location.Max(p => Along(p, side));
        var overallNote = Math.Abs(locationLow - lowOverall) <= SameCoordinate
            && Math.Abs(locationHigh - highOverall) <= SameCoordinate
                ? "location chain already spans the overall"
                : "section overall needs separate visual review; full-solid projection may not be the cut outline";
        return new SideResult([
            Chain("profile", profile, side, [], [], []),
            Chain("location", location, side, locatedByProfile, otherSide, mergedNearby),
            Empty("overall", overallNote)], located.Distinct().ToArray());
    }

    public static SideResult Refused(string reason) => new(
        [Empty("profile", reason), Empty("location", reason), Empty("overall", reason)], []);

    private static object Chain(string kind, IEnumerable<DimensionPoint> points, DimensionChainSide side,
        IReadOnlyCollection<int> byProfile,
        IReadOnlyCollection<int> otherSide, IReadOnlyCollection<int> mergedNearby)
    {
        var ordered = points.GroupBy(p => Along(p, side))
            .Select(g => g.OrderByDescending(p => Outside(p, side)).ThenBy(p => p.Id, StringComparer.Ordinal).First())
            .OrderBy(p => Along(p, side)).ToArray();
        return new {
            kind,
            pointIds = ordered.Select(p => p.Id).ToArray(),
            segments = ordered.Zip(ordered.Skip(1), (a, b) => Math.Round(Along(b, side) - Along(a, side), 3)).ToArray(),
            points = ordered.Select(p => new { pointId = p.Id,
                partIds = p.Parents.Where(parent => parent.ModelId.HasValue).Select(parent => parent.ModelId!.Value).Distinct().ToArray() }).ToArray(),
            locatedByProfilePartIds = byProfile.ToArray(),
            offeredOnOtherSidePartIds = otherSide.ToArray(),
            mergedNearbyPartIds = mergedNearby.ToArray(),
            note = (string?)null
        };
    }

    private static object Empty(string kind, string note) => new {
        kind, pointIds = Array.Empty<string>(), segments = Array.Empty<double>(),
        points = Array.Empty<object>(),
        locatedByProfilePartIds = Array.Empty<int>(), offeredOnOtherSidePartIds = Array.Empty<int>(),
        mergedNearbyPartIds = Array.Empty<int>(), note
    };

    private static double[] AxisLevels(IReadOnlyList<Vec3> points, bool alongX, double minLength)
    {
        var levels = new List<double>();
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var delta = alongX ? Math.Abs(a.X - b.X) : Math.Abs(a.Y - b.Y);
            var fixedDelta = alongX ? Math.Abs(a.Y - b.Y) : Math.Abs(a.X - b.X);
            if (delta >= minLength && fixedDelta <= SameCoordinate)
                levels.Add(alongX ? a.Y : a.X);
        }
        return levels.OrderBy(x => x).Aggregate(new List<double>(), (result, x) => {
            if (result.Count == 0 || x - result[result.Count - 1] > SameCoordinate) result.Add(x);
            return result;
        }).ToArray();
    }

    private static bool HasIAxisEdges(IReadOnlyList<Vec3> points, double[] x, double[] y)
    {
        if (points.Count < 12) return false;
        var horizontalCaps = 0;
        var webEdges = new HashSet<int>();
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            if ((Math.Abs(a.Y - y[0]) <= SameCoordinate || Math.Abs(a.Y - y[3]) <= SameCoordinate)
                && Math.Abs(a.Y - b.Y) <= SameCoordinate
                && Math.Min(a.X, b.X) <= x[0] + SameCoordinate
                && Math.Max(a.X, b.X) >= x[3] - SameCoordinate)
                horizontalCaps++;
            for (var web = 1; web <= 2; web++)
                if (Math.Abs(a.X - x[web]) <= SameCoordinate && Math.Abs(b.X - x[web]) <= SameCoordinate
                    && Math.Min(a.Y, b.Y) < (y[1] + y[2]) / 2
                    && Math.Max(a.Y, b.Y) > (y[1] + y[2]) / 2)
                    webEdges.Add(web);
        }
        return horizontalCaps == 2 && webEdges.Count == 2;
    }

    private static bool HasRectangle(IReadOnlyList<Vec3> points, double[] x, double[] y)
    {
        if (points.Count != 4) return false;
        return new[] { (x[0], y[0]), (x[0], y[1]), (x[1], y[0]), (x[1], y[1]) }
            .All(pair => points.Any(p => Math.Abs(p.X - pair.Item1) <= SameCoordinate
                                      && Math.Abs(p.Y - pair.Item2) <= SameCoordinate));
    }

    private static bool Owns(DimensionPoint point, int modelId) => point.Parents.Any(p => p.ModelId == modelId);
    private static bool AlongX(DimensionChainSide side) => side is DimensionChainSide.Top or DimensionChainSide.Bottom;
    private static double Along(DimensionPoint p, DimensionChainSide side) => AlongX(side) ? p.X : p.Y;
    private static double Across(DimensionPoint p, DimensionChainSide side) => AlongX(side) ? p.Y : p.X;
    private static double Outside(DimensionPoint p, DimensionChainSide side) =>
        (side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1) * Across(p, side);
    private static DimensionChainSide Opposite(DimensionChainSide side) => side switch {
        DimensionChainSide.Top => DimensionChainSide.Bottom, DimensionChainSide.Bottom => DimensionChainSide.Top,
        DimensionChainSide.Left => DimensionChainSide.Right, _ => DimensionChainSide.Left
    };
}
