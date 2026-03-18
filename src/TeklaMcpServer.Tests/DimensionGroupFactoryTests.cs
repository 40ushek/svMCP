using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupFactoryTests
{
    [Fact]
    public void BuildGroups_GroupsByViewAndOrientation()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "horizontal", 40, 0, 10, 100, 10, 5, 8, 105, 12),
            CreateDimension(2, 10, "FrontView", "horizontal", 45, 0, 20, 120, 20, 15, 18, 125, 22),
            CreateDimension(3, 10, "FrontView", "vertical", 30, 30, 0, 30, 80, 25, 0, 35, 85),
            CreateDimension(4, 11, "TopView", "horizontal", 20, 0, 35, 90, 35, 35, 33, 95, 40)
        ]);

        Assert.Equal(3, groups.Count);

        var frontHorizontal = groups.Single(g => g.ViewId == 10 && g.Orientation == "horizontal");
        Assert.Equal(2, frontHorizontal.Members.Count);
        Assert.Equal(45, frontHorizontal.MaximumDistance, 3);
    }

    [Fact]
    public void BuildGroups_SortsHorizontalMembersByMinY()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "horizontal", 40, 0, 35, 100, 35, 25, 35, 105, 42),
            CreateDimension(2, 10, "FrontView", "horizontal", 45, 0, 15, 100, 15, 5, 15, 105, 22)
        ]);

        var group = Assert.Single(groups);
        Assert.Collection(
            group.Members,
            member => Assert.Equal(2, member.DimensionId),
            member => Assert.Equal(1, member.DimensionId));
    }

    [Fact]
    public void BuildGroups_ComputesDirectionReferenceLineAndBounds()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "vertical", 12, 5, 10, 5, 60, 10, 10, 15, 70)
        ]);

        var group = Assert.Single(groups);
        Assert.NotNull(group.Direction);
        Assert.NotNull(group.ReferenceLine);
        Assert.NotNull(group.Bounds);
        Assert.Equal(0, group.Direction!.Value.X, 6);
        Assert.Equal(1, group.Direction!.Value.Y, 6);
        Assert.Equal(5, group.ReferenceLine!.StartX, 3);
        Assert.Equal(10, group.ReferenceLine.StartY, 3);
        Assert.Equal(10, group.Bounds!.MinX, 3);
        Assert.Equal(10, group.Bounds.MinY, 3);
        Assert.Equal(15, group.Bounds.MaxX, 3);
        Assert.Equal(70, group.Bounds.MaxY, 3);
    }

    [Fact]
    public void BuildGroups_AcceptsGetDimensionsResult()
    {
        var result = new GetDimensionsResult
        {
            Total = 2,
            Dimensions =
            [
                CreateDimension(1, 10, "FrontView", "horizontal", 40, 0, 10, 100, 10, 5, 8, 105, 12),
                CreateDimension(2, 10, "FrontView", "horizontal", 45, 0, 20, 120, 20, 15, 18, 125, 22)
            ]
        };

        var groups = DimensionGroupFactory.BuildGroups(result);

        var group = Assert.Single(groups);
        Assert.Equal(10, group.ViewId);
        Assert.Equal("horizontal", group.Orientation);
        Assert.Equal(2, group.Members.Count);
    }

    private static DrawingDimensionInfo CreateDimension(
        int id,
        int? viewId,
        string viewType,
        string orientation,
        double distance,
        double startX,
        double startY,
        double endX,
        double endY,
        double boundsMinX,
        double boundsMinY,
        double boundsMaxX,
        double boundsMaxY)
    {
        return new DrawingDimensionInfo
        {
            Id = id,
            ViewId = viewId,
            ViewType = viewType,
            Orientation = orientation,
            Distance = distance,
            Bounds = new DrawingBoundsInfo
            {
                MinX = boundsMinX,
                MinY = boundsMinY,
                MaxX = boundsMaxX,
                MaxY = boundsMaxY
            },
            Segments =
            [
                new DimensionSegmentInfo
                {
                    StartX = startX,
                    StartY = startY,
                    EndX = endX,
                    EndY = endY
                }
            ]
        };
    }
}
