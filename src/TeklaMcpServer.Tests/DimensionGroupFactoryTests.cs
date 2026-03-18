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

    [Fact]
    public void BuildGroups_DoesNotMergeParallelDimensionsWithoutCollinearLeadLines()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5),
            CreateDimension(2, 10, "FrontView", "PartLongitudinal", "horizontal", 45, 1, 0, -1, 0, -1, 20, 120, 20, 15, 120, 25)
        ]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void BuildGroups_MergesParallelDimensionsWhenLeadLinesAreCollinear()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5),
            CreateDimension(2, 10, "FrontView", "PartLongitudinal", "horizontal", 45, 1, 0, -1, 0, -1, 100, 200, 100, 15, 200, 25)
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_DoesNotSplitOnlyBecauseTeklaDimensionTypeDiffers()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5),
            CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 45, 1, 0, -1, 0, -1, 100, 200, 100, 15, 200, 25)
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_SplitsSingleSetIntoLineLevelMembers()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "PartLongitudinal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5);
        dimension.Segments.Add(new DimensionSegmentInfo
        {
            Id = 1002,
            StartX = 0,
            StartY = 20,
            EndX = 100,
            EndY = 20,
            Distance = 40,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = 0,
                StartY = 60,
                EndX = 100,
                EndY = 60
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 0,
                StartY = 20,
                EndX = 0,
                EndY = 60
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 20,
                EndX = 100,
                EndY = 60
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = 0,
                MinY = 60,
                MaxX = 100,
                MaxY = 60
            }
        });

        var groups = DimensionGroupFactory.BuildGroups([dimension]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Single(group.Members));
        Assert.Contains(groups, group => Assert.Equal(1002, Assert.Single(group.Members).SegmentId));
    }

    [Fact]
    public void BuildGroups_MergesChainSegmentsThatShareMeasuredPoint()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 220, 100, 0, 220, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 55 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 220, StartY = 0, EndX = 220, EndY = 55 };

        var groups = DimensionGroupFactory.BuildGroups([first, second]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_MergesTransitiveChainOfSegments()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 220, 100, 0, 220, 5);
        var third = CreateDimension(3, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 220, 360, 220, 0, 360, 5);

        var groups = DimensionGroupFactory.BuildGroups([first, second, third]);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Members.Count);
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
