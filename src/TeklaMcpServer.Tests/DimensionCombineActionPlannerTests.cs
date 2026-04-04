using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionCombineActionPlannerTests
{
    [Fact]
    public void BuildCandidates_AllowsCompatiblePacket()
    {
        var debug = DimensionGroupFactory.BuildGroupsWithReductionDebug(
        [
            CreateDimension(1, DimensionSourceKind.Part, 40),
            CreateDimension(2, DimensionSourceKind.Part, 40, startX: 100, endX: 200)
        ]);

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug));
        Assert.True(candidate.CanCombine);
        Assert.Equal(2, candidate.BaseDimensionId);
        Assert.NotNull(candidate.Preview);
        Assert.Equal(new[] { 1, 2 }, candidate.DimensionIds.OrderBy(static id => id).ToArray());
    }

    [Fact]
    public void BuildCandidates_BlocksPacketWithSourceKindMismatch()
    {
        var debug = DimensionGroupFactory.BuildGroupsWithReductionDebug(
            [CreateDimension(1, DimensionSourceKind.Part, 40), CreateDimension(2, DimensionSourceKind.Grid, 40, startX: 100, endX: 200)],
            combinePolicy: new DimensionCombinePolicy
            {
                RequireSameSourceKind = true
            });

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug));
        Assert.False(candidate.CanCombine);
        Assert.Equal("different_source_kind", candidate.Reason);
        Assert.Contains("different_source_kind", candidate.BlockingReasons);
    }

    [Fact]
    public void BuildCandidates_RequiresWholePacketInsideTargetFilter()
    {
        var debug = DimensionGroupFactory.BuildGroupsWithReductionDebug(
        [
            CreateDimension(1, DimensionSourceKind.Part, 40),
            CreateDimension(2, DimensionSourceKind.Part, 40, startX: 100, endX: 200)
        ]);

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug, [1]));

        Assert.False(candidate.CanCombine);
        Assert.Equal("target_filter_mismatch", candidate.Reason);
    }

    [Fact]
    public void BuildCandidates_RequiresUsablePreviewPointList()
    {
        var debug = new DimensionReductionDebugResult();
        var group = new DimensionGroupReductionDebugInfo
        {
            RawGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal },
            ReducedGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal }
        };
        group.CombineCandidates.Add(new DimensionCombineCandidateDebugInfo
        {
            IsCombineCandidate = true,
            CombineConnectivityMode = "shared_point_chain",
            CombinePreview = new DimensionCombinePreviewDebugInfo
            {
                BaseDimensionId = 42,
                Distance = 40
            }
        });
        group.CombineCandidates[0].DimensionIds.Add(41);
        group.CombineCandidates[0].DimensionIds.Add(42);
        debug.Groups.Add(group);

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug));

        Assert.False(candidate.CanCombine);
        Assert.Equal("combine_preview_has_too_few_points", candidate.Reason);
    }

    [Fact]
    public void BuildCandidates_DeduplicatesEquivalentCombineCandidates()
    {
        var debug = new DimensionReductionDebugResult();
        var group = new DimensionGroupReductionDebugInfo
        {
            RawGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal },
            ReducedGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal }
        };

        group.CombineCandidates.Add(CreateCombineCandidate([41, 42], true, "shared_point_neighbor_set"));
        group.CombineCandidates.Add(CreateCombineCandidate([42, 41], true, "shared_point_neighbor_set"));
        debug.Groups.Add(group);

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug));

        Assert.True(candidate.CanCombine);
        Assert.Equal(new[] { 41, 42 }, candidate.DimensionIds.ToArray());
    }

    [Fact]
    public void BuildCandidates_RequiresWholeCombineCandidateInsideTargetFilter()
    {
        var debug = new DimensionReductionDebugResult();
        var group = new DimensionGroupReductionDebugInfo
        {
            RawGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal },
            ReducedGroup = new DimensionGroup { ViewId = 10, ViewType = "FrontView", DomainDimensionType = DimensionType.Horizontal }
        };

        group.CombineCandidates.Add(CreateCombineCandidate([41, 42], true, "shared_point_neighbor_set"));
        debug.Groups.Add(group);

        var candidate = Assert.Single(DimensionCombineActionPlanner.BuildCandidates(debug, [41]));

        Assert.False(candidate.CanCombine);
        Assert.Equal("target_filter_mismatch", candidate.Reason);
    }

    private static DimensionCombineCandidateDebugInfo CreateCombineCandidate(
        int[] dimensionIds,
        bool isCombineCandidate,
        string connectivityMode)
    {
        var candidate = new DimensionCombineCandidateDebugInfo
        {
            IsCombineCandidate = isCombineCandidate,
            CombineConnectivityMode = connectivityMode,
            CombinePreview = new DimensionCombinePreviewDebugInfo
            {
                BaseDimensionId = dimensionIds[0],
                Distance = 40
            }
        };

        foreach (var dimensionId in dimensionIds)
            candidate.DimensionIds.Add(dimensionId);

        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 0, Y = 0, Order = 0 });
        candidate.CombinePreview.PointList.Add(new DrawingPointInfo { X = 100, Y = 0, Order = 1 });
        return candidate;
    }

    private static DrawingDimensionInfo CreateDimension(
        int id,
        DimensionSourceKind sourceKind,
        double distance,
        double startX = 0,
        double endX = 100)
    {
        return new DrawingDimensionInfo
        {
            Id = id,
            ViewId = 10,
            ViewType = "FrontView",
            DimensionType = "Relative",
            Orientation = "horizontal",
            Distance = distance,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            SourceKind = sourceKind,
            GeometryKind = DimensionGeometryKind.Horizontal,
            ClassifiedDimensionType = DimensionType.Horizontal,
            ReferenceLine = new DrawingLineInfo
            {
                StartX = startX,
                StartY = -distance,
                EndX = endX,
                EndY = -distance
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = startX,
                MinY = 0,
                MaxX = endX,
                MaxY = 5
            },
            Segments =
            [
                new DimensionSegmentInfo
                {
                    Id = id * 100,
                    StartX = startX,
                    StartY = 0,
                    EndX = endX,
                    EndY = 0,
                    Distance = distance,
                    DirectionX = 1,
                    DirectionY = 0,
                    TopDirection = -1,
                    DimensionLine = new DrawingLineInfo
                    {
                        StartX = startX,
                        StartY = -distance,
                        EndX = endX,
                        EndY = -distance
                    },
                    LeadLineMain = new DrawingLineInfo
                    {
                        StartX = startX,
                        StartY = 0,
                        EndX = startX,
                        EndY = -distance
                    },
                    LeadLineSecond = new DrawingLineInfo
                    {
                        StartX = endX,
                        StartY = 0,
                        EndX = endX,
                        EndY = -distance
                    }
                }
            ]
        };
    }
}
