using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionPointObjectMapperTests
{
    [Fact]
    public void Map_MatchesPointToNearestCandidate()
    {
        var mapper = new DimensionPointObjectMapper();
        var mappings = mapper.Map(
            [new DrawingPointInfo { X = 0, Y = 0, Order = 0 }],
            [
                CreateCandidate("dimensionSet", 101, 1, [0, 0]),
                CreateCandidate("dimensionSet", 102, 2, [50, 0])
            ],
            new Dictionary<int, IReadOnlyList<string>>());

        var mapping = Assert.Single(mappings);
        Assert.Equal(DimensionPointObjectMappingStatus.Matched, mapping.Status);
        Assert.Equal(101, mapping.MatchedCandidate?.ModelId);
        Assert.Equal(0d, mapping.DistanceToGeometry);
    }

    [Fact]
    public void Map_ReturnsAmbiguousWhenTopCandidatesAreTooClose()
    {
        var mapper = new DimensionPointObjectMapper();
        var mappings = mapper.Map(
            [new DrawingPointInfo { X = 10, Y = 0, Order = 0 }],
            [
                CreateCandidate("dimensionSet", 101, 1, [10, 0]),
                CreateCandidate("dimensionSet", 102, 2, [10.5, 0])
            ],
            new Dictionary<int, IReadOnlyList<string>>());

        var mapping = Assert.Single(mappings);
        Assert.Equal(DimensionPointObjectMappingStatus.Ambiguous, mapping.Status);
        Assert.Equal("ambiguous_nearest_candidate", mapping.Warning);
    }

    [Fact]
    public void Map_ReturnsNoGeometryWhenPoolHasOnlyGeometrylessCandidates()
    {
        var mapper = new DimensionPointObjectMapper();
        var mappings = mapper.Map(
            [new DrawingPointInfo { X = 0, Y = 0, Order = 0 }],
            [new DimensionSourceCandidateInfo { Owner = "dimensionSet", ModelId = 101, SourceKind = "Part" }],
            new Dictionary<int, IReadOnlyList<string>>());

        var mapping = Assert.Single(mappings);
        Assert.Equal(DimensionPointObjectMappingStatus.NoGeometry, mapping.Status);
        Assert.Equal(1, mapping.CandidateCount);
    }

    [Fact]
    public void Map_ReturnsNoCandidatesWhenPoolIsEmpty()
    {
        var mapper = new DimensionPointObjectMapper();
        var mappings = mapper.Map(
            [new DrawingPointInfo { X = 0, Y = 0, Order = 0 }],
            [],
            new Dictionary<int, IReadOnlyList<string>>());

        var mapping = Assert.Single(mappings);
        Assert.Equal(DimensionPointObjectMappingStatus.NoCandidates, mapping.Status);
        Assert.Equal(0, mapping.CandidateCount);
    }

    [Fact]
    public void Map_PrefersSegmentLocalCandidatesOverDimensionSetCandidates()
    {
        var mapper = new DimensionPointObjectMapper();
        var mappings = mapper.Map(
            [new DrawingPointInfo { X = 100, Y = 0, Order = 1 }],
            [
                CreateCandidate("dimensionSet", 101, 1, [100, 0]),
                CreateCandidate("segment:42", 202, 2, [100.2, 0])
            ],
            new Dictionary<int, IReadOnlyList<string>>
            {
                [1] = ["segment:42"]
            });

        var mapping = Assert.Single(mappings);
        Assert.Equal(DimensionPointObjectMappingStatus.Matched, mapping.Status);
        Assert.Equal("segment:42", mapping.MatchedCandidate?.Owner);
        Assert.Equal(1, mapping.CandidateCount);
    }

    private static DimensionSourceCandidateInfo CreateCandidate(string owner, int modelId, int drawingObjectId, params double[][] points)
    {
        var candidate = new DimensionSourceCandidateInfo
        {
            Owner = owner,
            ModelId = modelId,
            DrawingObjectId = drawingObjectId,
            SourceKind = "Part",
            Type = "Part",
            HasGeometry = points.Length > 0,
            GeometryPointCount = points.Length
        };

        candidate.GeometryPoints.AddRange(points.Select((point, index) => new DrawingPointInfo
        {
            X = point[0],
            Y = point[1],
            Order = index
        }));

        return candidate;
    }
}
