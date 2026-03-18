using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupSpacingAnalyzerTests
{
    [Fact]
    public void Analyze_UsesReferenceLineSpacingForHorizontalGroup()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 20, 0, 10, 100, 10),
            CreateMember(2, 10, 30, 100, 40, 0, 25, 100, 25)
        ], "horizontal", "PartLongitudinal", (1, 0), -1);

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.False(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(15, analysis.MinimumDistance.Value, 3);
        var pair = Assert.Single(analysis.Pairs);
        Assert.Equal(1, pair.FirstDimensionId);
        Assert.Equal(2, pair.SecondDimensionId);
        Assert.Equal(15, pair.Distance, 3);
    }

    [Fact]
    public void Analyze_CoincidentReferenceLinesProduceZeroGap()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 100, 25, 0, 10, 100, 10),
            CreateMember(2, 10, 20, 100, 35, 0, 10, 100, 10)
        ], "horizontal", "PartLongitudinal", (1, 0), -1);

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.False(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(0, analysis.MinimumDistance.Value, 3);
        Assert.False(Assert.Single(analysis.Pairs).IsOverlap);
    }

    [Fact]
    public void Analyze_VerticalGroup_UsesReferenceLineXAxis()
    {
        var group = CreateGroup(
        [
            CreateMember(1, 10, 10, 20, 80, 10, 0, 10, 80),
            CreateMember(2, 30, 10, 40, 80, 25, 0, 25, 80)
        ], "vertical", "PartTransversal", (0, 1), 1);

        var analysis = DimensionGroupSpacingAnalyzer.Analyze(group);

        Assert.False(analysis.HasOverlaps);
        Assert.NotNull(analysis.MinimumDistance);
        Assert.Equal(15, analysis.MinimumDistance.Value, 3);
    }

    private static DimensionGroup CreateGroup(
        DimensionGroupMember[] members,
        string orientation,
        string dimensionType,
        (double X, double Y) direction,
        int topDirection)
    {
        var group = new DimensionGroup
        {
            ViewId = 10,
            ViewType = "FrontView",
            Orientation = orientation,
            DimensionType = dimensionType,
            Direction = direction,
            TopDirection = topDirection
        };

        group.Members.AddRange(members);
        group.SortMembers();
        group.RefreshMetrics();
        return group;
    }

    private static DimensionGroupMember CreateMember(
        int dimensionId,
        double minX,
        double minY,
        double maxX,
        double maxY,
        double lineStartX,
        double lineStartY,
        double lineEndX,
        double lineEndY)
    {
        return new DimensionGroupMember
        {
            DimensionId = dimensionId,
            SortKey = lineStartX + lineStartY,
            Bounds = new DrawingBoundsInfo
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            },
            ReferenceLine = new DrawingLineInfo
            {
                StartX = lineStartX,
                StartY = lineStartY,
                EndX = lineEndX,
                EndY = lineEndY
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
