using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupSpacingAnalyzerTests
{
    [Fact]
    public void Analyze_HorizontalGroup_ReturnsPositiveGap()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20),
            CreateMember(2, 10, 30, 100, 40)
        ], "horizontal");

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.False(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(10, analysis.MinimumDistance.Value, 3);
        var pair = Assert.Single(analysis.Pairs);
        Assert.Equal(1, pair.FirstDimensionId);
        Assert.Equal(2, pair.SecondDimensionId);
        Assert.Equal(10, pair.Distance, 3);
    }

    [Fact]
    public void Analyze_HorizontalGroup_DetectsOverlap()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25),
            CreateMember(2, 10, 20, 100, 35)
        ], "horizontal");

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.True(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(-5, analysis.MinimumDistance.Value, 3);
        Assert.True(Assert.Single(analysis.Pairs).IsOverlap);
    }

    [Fact]
    public void Analyze_VerticalGroup_UsesXAxisSeparation()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 20, 80),
            CreateMember(2, 30, 10, 40, 80)
        ], "vertical");

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.False(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(10, analysis.MinimumDistance.Value, 3);
    }

    private static DimensionGroup CreateGroup(DimensionGroupMember[] members, string orientation)
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = orientation
        };

        group.Members.AddRange(members);
        group.SortMembers();
        group.RefreshMetrics();
        return group;
    }

    private static DimensionGroupMember CreateMember(int dimensionId, double minX, double minY, double maxX, double maxY)
    {
        return new DimensionGroupMember
        {
            DimensionId = dimensionId,
            SortKey = minX + minY,
            Bounds = new DrawingBoundsInfo
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            },
            Dimension = new DrawingDimensionInfo
            {
                Id = dimensionId,
                Bounds = new DrawingBoundsInfo
                {
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY
                }
            }
        };
    }
}
