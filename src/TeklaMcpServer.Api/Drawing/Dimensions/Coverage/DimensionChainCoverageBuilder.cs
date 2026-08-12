using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Joins one dimension chain to the candidate-point layer, point by point.
///
/// The join answers a single question: does the candidate layer already contain the points a
/// person actually used? It records every match and never selects one — selection is the rule
/// the placement planner has to derive, and pre-selecting here would hide the evidence for it.
/// </summary>
public static class DimensionChainCoverageBuilder
{
    /// <summary>
    /// How close two candidates must be to count as one place. This is deliberately not the
    /// search tolerance: the search radius says how far from the dimension point to look, while
    /// coincidence is exact in practice — a hull vertex is built from a solid vertex, and one
    /// physical edge shared by two faces yields the identical midpoint. Anything further apart
    /// than solid vertex rounding is a real alternative and must be reported as one.
    /// </summary>
    public const double PositionEpsilonMm = 0.01;

    public static DimensionChainCoverageResult Build(
        DimensionContextInfo dimension,
        IReadOnlyDictionary<int, IReadOnlyList<DrawingPartCandidatePoint>> candidatesByModelId,
        double tolerance)
    {
        var result = new DimensionChainCoverageResult
        {
            Success = true,
            ViewId = dimension.ViewId ?? 0,
            DimensionId = dimension.DimensionId,
            Tolerance = tolerance,
            SearchedModelIds = candidatesByModelId.Keys.OrderBy(static id => id).ToList()
        };

        if (dimension.PointAssociations.Count == 0)
            result.Warnings.Add("dimension_has_no_point_associations");

        var order = 0;
        foreach (var association in dimension.PointAssociations)
        {
            result.Points.Add(BuildPoint(dimension, association, candidatesByModelId, tolerance, order++));
        }

        return result;
    }

    private static DimensionPointCoverage BuildPoint(
        DimensionContextInfo dimension,
        DimensionContextPointAssociationInfo association,
        IReadOnlyDictionary<int, IReadOnlyList<DrawingPartCandidatePoint>> candidatesByModelId,
        double tolerance,
        int order)
    {
        var x = association.Point.X;
        var y = association.Point.Y;

        var coverage = new DimensionPointCoverage
        {
            DimensionId = dimension.DimensionId,
            PointOrder = order,
            Point = [x, y],
            AssociatedModelId = association.MatchedModelId,
            AssociationStatus = association.Status,
            SegmentIds = FindTouchingSegments(dimension, x, y, tolerance)
        };

        // Always search every part. Stopping at the first hit on the associated part would hide
        // the case worth seeing — the same place claimed by two parts — and would quietly pick a
        // winner, which is the one thing this join must not do.
        var matches = CollectMatches(candidatesByModelId.Values.SelectMany(static list => list), x, y, tolerance);

        coverage.Stage = matches.Count == 0
            ? DimensionCoverageMatchStage.None
            : matches.Any(match => match.ModelObjectId == association.MatchedModelId)
                ? DimensionCoverageMatchStage.SamePart
                : DimensionCoverageMatchStage.NearestByDistance;

        Apply(coverage, matches);
        return coverage;
    }

    private static void Apply(DimensionPointCoverage coverage, List<DimensionCoverageMatch> matches)
    {
        coverage.Matches = matches;
        coverage.Status = ResolveStatus(matches);
        coverage.BestConfidence = matches
            .Select(static match => match.Confidence)
            .OrderBy(static confidence => ConfidenceRank(confidence))
            .FirstOrDefault() ?? string.Empty;
        coverage.FallbackOnly = matches.Count > 0 && matches.All(static match => IsFallback(match.Confidence));
    }

    private static bool IsFallback(string confidence) =>
        confidence == nameof(DrawingPartCandidateConfidence.DerivedGeometry)
        || confidence == nameof(DrawingPartCandidateConfidence.BoundingBoxFallback);

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        nameof(DrawingPartCandidateConfidence.ExactGeometry) => 0,
        nameof(DrawingPartCandidateConfidence.ReferenceGeometry) => 1,
        nameof(DrawingPartCandidateConfidence.DerivedGeometry) => 2,
        _ => 3
    };

    private static List<DimensionCoverageMatch> CollectMatches(
        IEnumerable<DrawingPartCandidatePoint> candidates,
        double x,
        double y,
        double tolerance)
    {
        var matches = new List<DimensionCoverageMatch>();
        foreach (var candidate in candidates)
        {
            if (candidate.Point.Length < 2)
                continue;

            var distance = Distance(candidate.Point[0], candidate.Point[1], x, y);
            if (distance > tolerance)
                continue;

            // One match per owning part, not one per candidate. A candidate is a point and
            // can belong to several parts - a contact belongs to both sides of it - while a
            // match answers "does this point coincide with something of part X", which is
            // per part by nature. Coverage already keeps every match and deliberately picks
            // no winner, so two owners simply produce two.
            //
            // A candidate with no owner produces none. That is not a loss: derived geometry
            // from the assembly contour has no part to associate with, and inventing one by
            // proximity is the thing this join exists to avoid.
            foreach (var modelObjectId in candidate.ModelObjectIds)
            {
                matches.Add(new DimensionCoverageMatch
                {
                    ModelObjectId = modelObjectId,
                    AnchorKey = AnchorKeyFor(candidate, modelObjectId),
                    Source = candidate.Source.ToString(),
                    Confidence = candidate.Confidence.ToString(),
                    Point = [candidate.Point[0], candidate.Point[1]],
                    InPlaneNormal = candidate.InPlaneNormal is { Length: >= 2 }
                        ? [candidate.InPlaneNormal[0], candidate.InPlaneNormal[1]]
                        : null,
                    Distance = distance
                });
            }
        }

        return matches
            .OrderBy(static match => match.Distance)
            .ThenBy(static match => match.AnchorKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The anchor key for this part's match, and empty where there is none.
    ///
    /// An anchor is a feature of one part - a face, a vertex - and only that part may claim
    /// it. Where a point belongs to several, handing the first one's anchor to the rest
    /// would publish "matched part B at part A's face", which is untrue and visible in the
    /// serialized answer.
    ///
    /// Empty rather than a substitute. A key made from the coordinate looks stable and is
    /// not: places here are counted at 0.01 mm, so two points held to be one place can round
    /// to different keys, and two genuinely different contacts can share a coordinate in
    /// projection. Nothing counts places by this key - the status compares point distances -
    /// so an empty one costs nothing today, and when a Contact source arrives it can carry
    /// an identity of the contact itself instead of one invented from geometry.
    /// </summary>
    private static string AnchorKeyFor(DrawingPartCandidatePoint candidate, int modelObjectId) =>
        modelObjectId == candidate.Anchor.ModelObjectId
            ? candidate.Anchor.Key
            : string.Empty;

    /// <summary>
    /// One geometric position normally carries several anchor keys — a face edge belongs to two
    /// faces, and a vertex coincides with its edge ends once projected. Counting keys would make
    /// every real match ambiguous, so the status counts places instead.
    /// </summary>
    private static DimensionCoverageStatus ResolveStatus(IReadOnlyList<DimensionCoverageMatch> matches)
    {
        if (matches.Count == 0)
            return DimensionCoverageStatus.Missing;

        var nearest = matches[0];
        foreach (var match in matches)
        {
            if (Distance(match.Point[0], match.Point[1], nearest.Point[0], nearest.Point[1]) > PositionEpsilonMm)
                return DimensionCoverageStatus.Ambiguous;
        }

        return DimensionCoverageStatus.Matched;
    }

    private static List<int> FindTouchingSegments(DimensionContextInfo dimension, double x, double y, double tolerance)
    {
        var ids = new List<int>();
        foreach (var segment in dimension.SegmentContexts)
        {
            var geometry = segment.Geometry;
            if (Distance(geometry.StartX, geometry.StartY, x, y) <= tolerance
                || Distance(geometry.EndX, geometry.EndY, x, y) <= tolerance)
            {
                ids.Add(segment.SegmentId);
            }
        }

        ids.Sort();
        return ids;
    }

    private static double Distance(double firstX, double firstY, double secondX, double secondY)
    {
        var dx = firstX - secondX;
        var dy = firstY - secondY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
