using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionGroupFactoryTests
{
    [Fact]
    public void BuildGroups_GroupsByViewTypeDirectionAndTopSide()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12),
            CreateDimension(2, 10, "FrontView", "PartLongitudinal", "horizontal", 45, 1, 0, -1, 0, -1, 0, 120, 15, 18, 125, 22),
            CreateDimension(3, 10, "FrontView", "PartLongitudinal", "horizontal", 30, 1, 0, 1, 0, 1, 0, 100, 25, 28, 105, 32),
            CreateDimension(4, 11, "TopView", "PartLongitudinal", "horizontal", 20, 1, 0, -1, 0, -1, -10, 90, 35, 33, 95, 40)
        ]);

        Assert.Equal(3, groups.Count);

        var frontTop = groups.Single(g =>
            g.ViewId == 10 &&
            g.ViewType == "FrontView" &&
            g.DimensionType == "PartLongitudinal" &&
            g.TopDirection == -1);
        Assert.Equal(2, frontTop.Members.Count);
        Assert.Equal(45, frontTop.MaximumDistance, 3);
    }

    [Fact]
    public void BuildGroups_SortsMembersByReferenceLineOffset()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 25, 35, 105, 42),
            CreateDimension(2, 10, "FrontView", "PartLongitudinal", "horizontal", 45, 1, 0, -1, 0, -1, 0, 100, 5, 15, 105, 22)
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
            CreateDimension(1, 10, "FrontView", "PartTransversal", "vertical", 12, 0, 1, 1, 1, 0, 5, 5, 10, 10, 15, 70)
        ]);

        var group = Assert.Single(groups);
        Assert.NotNull(group.Direction);
        Assert.NotNull(group.ReferenceLine);
        Assert.NotNull(group.Bounds);
        Assert.Equal(0, group.Direction!.Value.X, 6);
        Assert.Equal(1, group.Direction!.Value.Y, 6);
        Assert.Equal(17, group.ReferenceLine!.StartX, 3);
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
                CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12),
                CreateDimension(2, 10, "FrontView", "PartLongitudinal", "horizontal", 45, 1, 0, -1, 0, -1, 0, 120, 15, 18, 125, 22)
            ]
        };

        var groups = DimensionGroupFactory.BuildGroups(result);

        var group = Assert.Single(groups);
        Assert.Equal(10, group.ViewId);
        Assert.Equal("PartLongitudinal", group.DimensionType);
        Assert.Equal(2, group.Members.Count);
    }

    private static DrawingDimensionInfo CreateDimension(
        int id,
        int? viewId,
        string viewType,
        string dimensionType,
        string orientation,
        double distance,
        double directionX,
        double directionY,
        int topDirection,
        double upX,
        double upY,
        double startY,
        double startX,
        double endX,
        double boundsMinX,
        double boundsMinY,
        double boundsMaxX,
        double boundsMaxY)
    {
        var endY = orientation == "vertical" ? 60.0 : startY;
        var offsetStartX = startX + (upX * distance);
        var offsetStartY = startY + (upY * distance);
        var offsetEndX = endX + (upX * distance);
        var offsetEndY = endY + (upY * distance);

        return new DrawingDimensionInfo
        {
            Id = id,
            ViewId = viewId,
            ViewType = viewType,
            DimensionType = dimensionType,
            Orientation = orientation,
            Distance = distance,
            DirectionX = directionX,
            DirectionY = directionY,
            TopDirection = topDirection,
            Bounds = new DrawingBoundsInfo
            {
                MinX = boundsMinX,
                MinY = boundsMinY,
                MaxX = boundsMaxX,
                MaxY = boundsMaxY
            },
            ReferenceLine = new DrawingLineInfo
            {
                StartX = offsetStartX,
                StartY = offsetStartY,
                EndX = offsetEndX,
                EndY = offsetEndY
            },
            Segments =
            [
                new DimensionSegmentInfo
                {
                    StartX = startX,
                    StartY = startY,
                    EndX = endX,
                    EndY = endY,
                    Distance = distance,
                    DirectionX = directionX,
                    DirectionY = directionY,
                    TopDirection = topDirection,
                    DimensionLine = new DrawingLineInfo
                    {
                        StartX = offsetStartX,
                        StartY = offsetStartY,
                        EndX = offsetEndX,
                        EndY = offsetEndY
                    },
                    LeadLineMain = new DrawingLineInfo
                    {
                        StartX = startX,
                        StartY = startY,
                        EndX = offsetStartX,
                        EndY = offsetStartY
                    },
                    LeadLineSecond = new DrawingLineInfo
                    {
                        StartX = endX,
                        StartY = endY,
                        EndX = offsetEndX,
                        EndY = offsetEndY
                    }
                }
            ]
        };
    }
}
