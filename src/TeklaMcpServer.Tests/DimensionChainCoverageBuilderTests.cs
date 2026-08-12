using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Coverage is acceptance material for the placement planner, so its value lies in what it
/// refuses to decide. These tests pin that: every match survives, and the status describes
/// places rather than anchor keys.
/// </summary>
public sealed class DimensionChainCoverageBuilderTests
{
    [Fact]
    public void Build_TreatsSeveralAnchorsOnOnePositionAsMatched()
    {
        // A face edge belongs to two faces and coincides with a vertex once projected, so a real
        // point normally carries three or four keys. Counting keys would make every genuine match
        // ambiguous and the whole report useless.
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:FaceEdge:0/0/0-1", 100d, 200d),
            (11, "11:FaceEdge:2/0/0-1", 100d, 200d),
            (11, "11:Vertex:0", 100d, 200d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(DimensionCoverageStatus.Matched, point.Status);
        Assert.Equal(DimensionCoverageMatchStage.SamePart, point.Stage);
        Assert.Equal(3, point.Matches.Count);
        Assert.Equal(
            ["11:FaceEdge:0/0/0-1", "11:FaceEdge:2/0/0-1", "11:Vertex:0"],
            point.Matches.Select(match => match.AnchorKey).OrderBy(key => key));
    }

    [Fact]
    public void Build_ReportsAmbiguousWhenMatchesSitAtDifferentPlaces()
    {
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:Vertex:0", 100d, 200d),
            (11, "11:Vertex:5", 100d, 209d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 10d).Points);

        Assert.Equal(DimensionCoverageStatus.Ambiguous, point.Status);
        Assert.Equal(2, point.Matches.Count);
    }

    [Fact]
    public void Build_SeparatesTheSearchRadiusFromPositionCoincidence()
    {
        // A wide search radius must not merge two real alternatives into one place. Using the
        // search tolerance for both would report a 9 mm choice as settled.
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:Vertex:0", 100d, 200d),
            (11, "11:Vertex:5", 100d, 200.5d));

        var narrow = DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 0.1d).Points[0];
        var wide = DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 5d).Points[0];

        Assert.Equal(DimensionCoverageStatus.Matched, narrow.Status);
        Assert.Single(narrow.Matches);
        Assert.Equal(DimensionCoverageStatus.Ambiguous, wide.Status);
        Assert.Equal(2, wide.Matches.Count);
    }

    [Fact]
    public void Build_FlagsAPointThatOnlyReachesAFallbackCandidate()
    {
        // Seen on a real drawing: a vertical chain ended at the bounding-box corner of a raked
        // top plate, where nothing exists — the plate reaches that height at the opposite end.
        // The chain is wrong and should be extended to the real vertex, so this has to be
        // visible rather than counted as a clean match.
        var dimension = CreateDimension((1833.5d, 1691.1d, ModelId: 30));
        var candidates = new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>>
        {
            [30] =
            [
                Candidate(30, DrawingPartCandidateAnchorKind.BoundingBoxCorner, "max_x_max_y", 1833.5d, 1691.1d,
                    DrawingPartCandidatePointSource.BoundingBoxCorner, DrawingPartCandidateConfidence.BoundingBoxFallback),
                Candidate(30, DrawingPartCandidateAnchorKind.Vertex, "3", 0d, 1691.1d,
                    DrawingPartCandidatePointSource.SolidVertex, DrawingPartCandidateConfidence.ExactGeometry)
            ]
        };

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(DimensionCoverageStatus.Matched, point.Status);
        Assert.True(point.FallbackOnly);
        Assert.Equal("BoundingBoxFallback", point.BestConfidence);
    }

    [Fact]
    public void Build_DoesNotFlagFallbackWhenExactGeometryIsAlsoPresent()
    {
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>>
        {
            [11] =
            [
                Candidate(11, DrawingPartCandidateAnchorKind.BoundingBoxCorner, "min_x_min_y", 100d, 200d,
                    DrawingPartCandidatePointSource.BoundingBoxCorner, DrawingPartCandidateConfidence.BoundingBoxFallback),
                Candidate(11, DrawingPartCandidateAnchorKind.Vertex, "0", 100d, 200d,
                    DrawingPartCandidatePointSource.SolidVertex, DrawingPartCandidateConfidence.ExactGeometry)
            ]
        };

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.False(point.FallbackOnly);
        Assert.Equal("ExactGeometry", point.BestConfidence);
    }

    [Fact]
    public void Build_ReportsMissingWhenNothingIsWithinTolerance()
    {
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates((11, "11:Vertex:0", 400d, 200d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(DimensionCoverageStatus.Missing, point.Status);
        Assert.Equal(DimensionCoverageMatchStage.None, point.Stage);
        Assert.Empty(point.Matches);
    }

    [Fact]
    public void Build_FallsBackToOtherPartsAndSaysSo()
    {
        // The association naming one part while the point sits on another is the finding worth
        // having. Searching only the associated part would report it as `missing` and lose it.
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:Vertex:0", 900d, 900d),
            (22, "22:FaceEdge:1/0/2-3", 100d, 200d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(DimensionCoverageMatchStage.NearestByDistance, point.Stage);
        Assert.Equal(DimensionCoverageStatus.Matched, point.Status);
        Assert.Equal(22, Assert.Single(point.Matches).ModelObjectId);
    }

    [Fact]
    public void Build_KeepsMatchesFromOtherPartsEvenWhenTheAssociatedPartMatches()
    {
        // Two parts meeting at one place is the collision worth seeing — it is what a shared grid
        // position looks like from below. Returning as soon as the associated part matched would
        // hide the second part and quietly settle a choice this join must leave open.
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:Vertex:0", 100d, 200d),
            (22, "22:Vertex:4", 100d, 200d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(DimensionCoverageMatchStage.SamePart, point.Stage);
        Assert.Equal(DimensionCoverageStatus.Matched, point.Status);
        Assert.Equal([11, 22], point.Matches.Select(match => match.ModelObjectId).OrderBy(id => id));
    }

    [Fact]
    public void Build_OrdersMatchesByDistanceWithoutDroppingAny()
    {
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        var candidates = Candidates(
            (11, "11:Vertex:9", 100.6d, 200d),
            (11, "11:FaceEdge:0/0/0-1", 100.1d, 200d));

        var point = Assert.Single(DimensionChainCoverageBuilder.Build(dimension, candidates, tolerance: 1d).Points);

        Assert.Equal(
            ["11:FaceEdge:0/0/0-1", "11:Vertex:9"],
            point.Matches.Select(match => match.AnchorKey));
        Assert.True(point.Matches[0].Distance < point.Matches[1].Distance);
    }

    [Fact]
    public void Build_CarriesChainIdentityForEveryPoint()
    {
        var dimension = CreateDimension((100d, 200d, ModelId: 11));
        dimension.SegmentContexts.Add(new DimensionContextSegmentInfo
        {
            SegmentId = 771,
            Geometry = new DimensionSegmentInfo { Id = 771, StartX = 100d, StartY = 200d, EndX = 500d, EndY = 200d }
        });

        var result = DimensionChainCoverageBuilder.Build(dimension, Candidates((11, "11:Vertex:0", 100d, 200d)), tolerance: 1d);

        var point = Assert.Single(result.Points);
        Assert.Equal(770, point.DimensionId);
        Assert.Equal([771], point.SegmentIds);
        Assert.Equal(11, point.AssociatedModelId);
        Assert.Equal("Matched", point.AssociationStatus);
        Assert.Equal([11], result.SearchedModelIds);
        Assert.Equal(1d, result.Tolerance);
    }

    private static DimensionContextInfo CreateDimension(params (double X, double Y, int ModelId)[] points)
    {
        var dimension = new DimensionContextInfo { DimensionId = 770, ViewId = 1214 };
        foreach (var point in points)
        {
            dimension.PointAssociations.Add(new DimensionContextPointAssociationInfo
            {
                Point = new DrawingPointInfo { X = point.X, Y = point.Y },
                Status = "Matched",
                MatchedModelId = point.ModelId
            });
        }

        return dimension;
    }

    private static DrawingPartCandidatePoint Candidate(
        int modelId,
        DrawingPartCandidateAnchorKind kind,
        string anchorId,
        double x,
        double y,
        DrawingPartCandidatePointSource source,
        DrawingPartCandidateConfidence confidence) =>
        new()
        {
            ModelObjectIds = [modelId],
            Point = [x, y, 0d],
            Source = source,
            Confidence = confidence,
            Anchor = new DrawingPartCandidateAnchor { ModelObjectId = modelId, Kind = kind, Id = anchorId }
        };

    [Fact]
    public void APointSharedByTwoPartsGivesBothAMatchAndNeitherTheOthersAnchor()
    {
        // A contact belongs to both sides of it. An anchor does not: a face is a feature of
        // one part, and handing part A's face to part B would publish "matched B at A's
        // face", which is untrue and visible in the serialized answer.
        var shared = new DrawingPartCandidatePoint
        {
            ModelObjectIds = [11, 22],
            Point = [100d, 0d, 0d],
            Source = DrawingPartCandidatePointSource.SolidVertex,
            Confidence = DrawingPartCandidateConfidence.ExactGeometry,
            Anchor = new DrawingPartCandidateAnchor
            {
                ModelObjectId = 11,
                Kind = DrawingPartCandidateAnchorKind.Vertex,
                Id = "v1"
            }
        };

        var coverage = DimensionChainCoverageBuilder.Build(
            CreateDimension((100d, 0d, 11)),
            new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>> { [11] = [shared] },
            tolerance: 0.5);

        var matches = coverage.Points.Single().Matches;

        Assert.Equal([11, 22], matches.Select(match => match.ModelObjectId).OrderBy(id => id));
        Assert.Equal("11:Vertex:v1", matches.Single(match => match.ModelObjectId == 11).AnchorKey);
        // Empty, not a substitute: a key made from the coordinate would look stable and is
        // not, and nothing here counts places by the key anyway.
        Assert.Equal(string.Empty, matches.Single(match => match.ModelObjectId == 22).AnchorKey);
    }

    [Fact]
    public void APointWithNoOwningPartGivesNoMatchAtAll()
    {
        // Derived geometry from the assembly contour has no part to associate with, and
        // finding the nearest one would invent an owner.
        var derived = new DrawingPartCandidatePoint
        {
            ModelObjectIds = [],
            Point = [100d, 0d, 0d],
            Source = DrawingPartCandidatePointSource.SolidVertex,
            Confidence = DrawingPartCandidateConfidence.DerivedGeometry
        };

        var coverage = DimensionChainCoverageBuilder.Build(
            CreateDimension((100d, 0d, 0)),
            new Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>> { [0] = [derived] },
            tolerance: 0.5);

        Assert.Empty(coverage.Points.Single().Matches);
    }

    private static Dictionary<int, IReadOnlyList<DrawingPartCandidatePoint>> Candidates(
        params (int ModelId, string Key, double X, double Y)[] candidates)
    {
        var result = new Dictionary<int, List<DrawingPartCandidatePoint>>();
        foreach (var candidate in candidates)
        {
            if (!result.TryGetValue(candidate.ModelId, out var list))
                result[candidate.ModelId] = list = [];

            var separator = candidate.Key.Split(':');
            list.Add(new DrawingPartCandidatePoint
            {
                ModelObjectIds = [candidate.ModelId],
                Point = [candidate.X, candidate.Y, 0d],
                Source = DrawingPartCandidatePointSource.SolidVertex,
                Confidence = DrawingPartCandidateConfidence.ExactGeometry,
                Anchor = new DrawingPartCandidateAnchor
                {
                    ModelObjectId = candidate.ModelId,
                    Kind = separator[1] == "FaceEdge"
                        ? DrawingPartCandidateAnchorKind.FaceEdge
                        : DrawingPartCandidateAnchorKind.Vertex,
                    Id = separator[2]
                }
            });
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<DrawingPartCandidatePoint>)pair.Value);
    }
}
