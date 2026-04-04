using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionPointObjectMappingStatus
{
    Matched,
    Ambiguous,
    NoCandidates,
    NoGeometry
}

internal sealed class DimensionPointObjectCandidateScore
{
    public DimensionSourceCandidateInfo Candidate { get; set; } = new();
    public double Distance { get; set; }
    public DrawingPointInfo? NearestGeometryPoint { get; set; }
}

internal sealed class DimensionPointObjectMapping
{
    public DrawingPointInfo Point { get; set; } = new();
    public DimensionPointObjectMappingStatus Status { get; set; }
    public DimensionSourceCandidateInfo? MatchedCandidate { get; set; }
    public double? DistanceToGeometry { get; set; }
    public DrawingPointInfo? NearestGeometryPoint { get; set; }
    public int CandidateCount { get; set; }
    public string Warning { get; set; } = string.Empty;
}

internal sealed class DimensionPointObjectMapper
{
    private const double AmbiguityTolerance = 1.0;

    public List<DimensionPointObjectMapping> Map(
        IReadOnlyList<DrawingPointInfo> measuredPoints,
        IReadOnlyList<DimensionSourceCandidateInfo> candidates,
        IReadOnlyDictionary<int, IReadOnlyList<string>> preferredOwnersByPointOrder)
    {
        var result = new List<DimensionPointObjectMapping>(measuredPoints.Count);
        foreach (var point in measuredPoints.OrderBy(static point => point.Order))
        {
            preferredOwnersByPointOrder.TryGetValue(point.Order, out var preferredOwners);
            var candidatePool = SelectCandidatePool(candidates, preferredOwners);
            result.Add(MapPoint(point, candidatePool));
        }

        return result;
    }

    private static List<DimensionSourceCandidateInfo> SelectCandidatePool(
        IReadOnlyList<DimensionSourceCandidateInfo> candidates,
        IReadOnlyList<string>? preferredOwners)
    {
        if (preferredOwners is { Count: > 0 })
        {
            var segmentPool = DistinctCandidates(candidates
                .Where(candidate => preferredOwners.Contains(candidate.Owner))
                .ToList());
            if (segmentPool.Count > 0)
                return segmentPool;
        }

        var dimensionSetPool = DistinctCandidates(candidates
            .Where(static candidate => string.Equals(candidate.Owner, "dimensionSet", System.StringComparison.Ordinal))
            .ToList());
        if (dimensionSetPool.Count > 0)
            return dimensionSetPool;

        return [];
    }

    private static List<DimensionSourceCandidateInfo> DistinctCandidates(IEnumerable<DimensionSourceCandidateInfo> candidates)
    {
        return candidates
            .GroupBy(static candidate => new
            {
                candidate.Owner,
                candidate.DrawingObjectId,
                candidate.ModelId,
                candidate.Type,
                candidate.SourceKind
            })
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.Owner)
            .ThenBy(static candidate => candidate.ModelId)
            .ThenBy(static candidate => candidate.DrawingObjectId)
            .ToList();
    }

    private static DimensionPointObjectMapping MapPoint(
        DrawingPointInfo point,
        IReadOnlyList<DimensionSourceCandidateInfo> candidatePool)
    {
        if (candidatePool.Count == 0)
        {
            return new DimensionPointObjectMapping
            {
                Point = CopyPoint(point),
                Status = DimensionPointObjectMappingStatus.NoCandidates,
                CandidateCount = 0,
                Warning = "no_candidates"
            };
        }

        var scores = candidatePool
            .Where(static candidate => candidate.HasGeometry && candidate.GeometryPoints.Count > 0)
            .Select(candidate => ScoreCandidate(point, candidate))
            .OrderBy(static score => score.Distance)
            .ThenBy(static score => score.Candidate.Owner)
            .ThenBy(static score => score.Candidate.ModelId)
            .ThenBy(static score => score.Candidate.DrawingObjectId)
            .ToList();

        if (scores.Count == 0)
        {
            return new DimensionPointObjectMapping
            {
                Point = CopyPoint(point),
                Status = DimensionPointObjectMappingStatus.NoGeometry,
                CandidateCount = candidatePool.Count,
                Warning = "no_geometry"
            };
        }

        var best = scores[0];
        var ambiguous = scores.Count > 1 && System.Math.Abs(scores[1].Distance - best.Distance) <= AmbiguityTolerance;
        return new DimensionPointObjectMapping
        {
            Point = CopyPoint(point),
            Status = ambiguous ? DimensionPointObjectMappingStatus.Ambiguous : DimensionPointObjectMappingStatus.Matched,
            MatchedCandidate = best.Candidate,
            DistanceToGeometry = System.Math.Round(best.Distance, 3),
            NearestGeometryPoint = CopyPoint(best.NearestGeometryPoint),
            CandidateCount = candidatePool.Count,
            Warning = ambiguous ? "ambiguous_nearest_candidate" : string.Empty
        };
    }

    private static DimensionPointObjectCandidateScore ScoreCandidate(DrawingPointInfo point, DimensionSourceCandidateInfo candidate)
    {
        var nearest = candidate.GeometryPoints
            .Select(geometryPoint => new
            {
                Point = geometryPoint,
                Distance = GetDistance(point, geometryPoint)
            })
            .OrderBy(static score => score.Distance)
            .ThenBy(static score => score.Point.Order)
            .First();

        return new DimensionPointObjectCandidateScore
        {
            Candidate = candidate,
            Distance = nearest.Distance,
            NearestGeometryPoint = CopyPoint(nearest.Point)
        };
    }

    private static double GetDistance(DrawingPointInfo left, DrawingPointInfo right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static DrawingPointInfo CopyPoint(DrawingPointInfo? point)
    {
        return point == null
            ? new DrawingPointInfo()
            : new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = point.Order
            };
    }
}
