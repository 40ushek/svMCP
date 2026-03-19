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
            CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12),
            CreateDimension(2, 10, "FrontView", "Horizontal", "horizontal", 45, 1, 0, -1, 0, -1, 0, 120, 15, 18, 125, 22),
            CreateDimension(3, 10, "FrontView", "Horizontal", "horizontal", 30, 1, 0, 1, 0, 1, 0, 100, 25, 28, 105, 32),
            CreateDimension(4, 11, "TopView", "Horizontal", "horizontal", 20, 1, 0, -1, 0, -1, -10, 90, 35, 33, 95, 40)
        ]);

        Assert.Equal(3, groups.Count);

        var frontTop = groups.Single(g =>
            g.ViewId == 10 &&
            g.ViewType == "FrontView" &&
            g.DimensionType == "Horizontal" &&
            g.TopDirection == -1);
        Assert.Equal(2, frontTop.Members.Count);
        Assert.Equal(45, frontTop.MaximumDistance, 3);
    }

    [Fact]
    public void BuildGroups_SortsMembersByReferenceLineOffset()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 25, 35, 105, 42),
            CreateDimension(2, 10, "FrontView", "Horizontal", "horizontal", 45, 1, 0, -1, 0, -1, 0, 100, 5, 15, 105, 22)
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
            CreateDimension(1, 10, "FrontView", "Vertical", "vertical", 12, 0, 1, 1, 1, 0, 5, 5, 10, 10, 15, 70)
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
    public void BuildItems_CreatesDomainItemWithLeadLinesAndLengths()
    {
        var item = Assert.Single(DimensionGroupFactory.BuildItems(
        [
            CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12)
        ]));

        Assert.Equal(1, item.DimensionId);
        Assert.Equal(DimensionType.Horizontal, item.DomainDimensionType);
        Assert.Equal("Horizontal", item.DimensionType);
        Assert.Single(item.SegmentIds);
        Assert.Equal(10, item.ViewId);
        Assert.NotNull(item.ReferenceLine);
        Assert.NotNull(item.LeadLineMain);
        Assert.NotNull(item.LeadLineSecond);
        Assert.Equal(2, item.PointList.Count);
        Assert.Single(item.LengthList);
        Assert.Single(item.RealLengthList);
    }

    [Fact]
    public void BuildItems_MapsRawTeklaTypeToDomainTypeUsingSourceAndGeometry()
    {
        var item = Assert.Single(DimensionGroupFactory.BuildItems(
        [
            CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12)
        ]));

        Assert.Equal("Absolute", item.TeklaDimensionType);
        Assert.Equal(DimensionType.Horizontal, item.DomainDimensionType);
        Assert.Equal("Horizontal", item.DimensionType);
    }

    [Fact]
    public void BuildItems_MapsFreeGeometryToFreeWhenSourceIsPart()
    {
        var item = Assert.Single(DimensionGroupFactory.BuildItems(
        [
            CreateDimension(1, 10, "FrontView", "Relative", "angled", 40, 0.866, 0.5, -1, 0.5, -0.866, 0, 100, 5, 8, 105, 12)
        ]));

        Assert.Equal(DimensionSourceKind.Part, item.SourceKind);
        Assert.Equal(DimensionGeometryKind.Free, item.GeometryKind);
        Assert.Equal(DimensionType.Free, item.DomainDimensionType);
    }

    [Fact]
    public void BuildItems_MapsFreeGeometryToFreeWhenSourceIsGrid()
    {
        var item = Assert.Single(DimensionGroupFactory.BuildItems(
        [
            CreateDimension(1, 10, "FrontView", "Relative", "angled", 40, 0.866, 0.5, -1, 0.5, -0.866, 0, 100, 5, 8, 105, 12, DimensionSourceKind.Grid)
        ]));

        Assert.Equal(DimensionSourceKind.Grid, item.SourceKind);
        Assert.Equal(DimensionGeometryKind.Free, item.GeometryKind);
        Assert.Equal(DimensionType.Free, item.DomainDimensionType);
    }

    [Fact]
    public void BuildItems_ReturnsUnknownWithoutSourceProof()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 5, 8, 105, 12, DimensionSourceKind.Unknown);
        dimension.ClassifiedDimensionType = DimensionType.Unknown;

        var item = Assert.Single(DimensionGroupFactory.BuildItems([dimension]));

        Assert.Equal(DimensionType.Unknown, item.DomainDimensionType);
    }

    [Fact]
    public void BuildGroups_DoesNotMergeParallelDimensionsWithoutCollinearLeadLines()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5),
            CreateDimension(2, 10, "FrontView", "Horizontal", "horizontal", 45, 1, 0, -1, 0, -1, 20, 120, 20, 15, 120, 25)
        ]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void BuildGroups_MergesParallelDimensionsWhenLeadLinesAreCollinear()
    {
        var groups = DimensionGroupFactory.BuildGroups(
        [
            CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5),
            CreateDimension(2, 10, "FrontView", "Horizontal", "horizontal", 45, 1, 0, -1, 0, -1, 100, 200, 100, 15, 200, 25)
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
        var dimension = CreateDimension(1, 10, "FrontView", "Horizontal", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 0, 0, 100, 5);
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

    [Fact]
    public void BuildGroups_DoesNotMergeSharedPointAcrossDifferentLineBands()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Relative", "horizontal", 60, 1, 0, -1, 0, -1, 0, 100, 220, 100, 0, 220, 5);

        first.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1400, EndX = 100, EndY = -1400 };
        first.Segments[0].DimensionLine = first.ReferenceLine;
        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1400 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -1400 };

        second.ReferenceLine = new DrawingLineInfo { StartX = 100, StartY = -2000, EndX = 220, EndY = -2000 };
        second.Segments[0].DimensionLine = second.ReferenceLine;
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -2000 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 220, StartY = 0, EndX = 220, EndY = -2000 };

        var groups = DimensionGroupFactory.BuildGroups([first, second]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void BuildGroups_MergesSharedPointAcrossLocalBandStepWithinSameDimension()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var secondSegment = new DimensionSegmentInfo
        {
            Id = 1002,
            StartX = 100,
            StartY = 0,
            EndX = 220,
            EndY = 0,
            Distance = 40,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = 100,
                StartY = -1500,
                EndX = 220,
                EndY = -1500
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 0,
                EndX = 100,
                EndY = -1500
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 220,
                StartY = 0,
                EndX = 220,
                EndY = -1500
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = 100,
                MinY = -1500,
                MaxX = 220,
                MaxY = -1500
            }
        };

        dimension.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1440, EndX = 100, EndY = -1440 };
        dimension.Segments[0].DimensionLine = dimension.ReferenceLine;
        dimension.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1440 };
        dimension.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -1440 };
        dimension.Segments.Add(secondSegment);

        var groups = DimensionGroupFactory.BuildGroups([dimension]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_DoesNotMergeSharedPointAcrossFarBandStepWithinSameDimension()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var secondSegment = new DimensionSegmentInfo
        {
            Id = 1002,
            StartX = 100,
            StartY = 0,
            EndX = 220,
            EndY = 0,
            Distance = 40,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = 100,
                StartY = -2000,
                EndX = 220,
                EndY = -2000
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 0,
                EndX = 100,
                EndY = -2000
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 220,
                StartY = 0,
                EndX = 220,
                EndY = -2000
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = 100,
                MinY = -2000,
                MaxX = 220,
                MaxY = -2000
            }
        };

        dimension.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1440, EndX = 100, EndY = -1440 };
        dimension.Segments[0].DimensionLine = dimension.ReferenceLine;
        dimension.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1440 };
        dimension.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -1440 };
        dimension.Segments.Add(secondSegment);

        var groups = DimensionGroupFactory.BuildGroups([dimension]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void BuildGroups_MergesNearbySegmentsOnSameLineBandWithinSameDimension()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "RelativeAndAbsolute", "vertical", 40, 0, 1, 1, -1, 0, 0, 0, 0, 0, 10, 60);
        dimension.Segments.Clear();
        dimension.MeasuredPoints =
        [
            new DrawingPointInfo { X = 0, Y = 1172.5, Order = 0 },
            new DrawingPointInfo { X = 0, Y = 1112.5, Order = 1 },
            new DrawingPointInfo { X = 3250, Y = 1052.5, Order = 2 },
            new DrawingPointInfo { X = 0, Y = 912.5, Order = 3 },
            new DrawingPointInfo { X = 0, Y = 838.5, Order = 4 },
            new DrawingPointInfo { X = 0, Y = -1172.5, Order = 5 }
        ];

        dimension.Segments.Add(CreateVerticalSegment(1001, 0, -1172.5, 0, 838.5, -200));
        dimension.Segments.Add(CreateVerticalSegment(1002, 0, 912.5, 3250, 1052.5, -200));
        dimension.Segments.Add(CreateVerticalSegment(1003, 0, 1112.5, 0, 1172.5, -200));

        var groups = DimensionGroupFactory.BuildGroups([dimension]);

        var group = Assert.Single(groups);
        var member = Assert.Single(group.Members);
        Assert.Equal(0, member.StartX, 3);
        Assert.Equal(1172.5, member.StartY, 3);
        Assert.Equal(0, member.EndX, 3);
        Assert.Equal(-1172.5, member.EndY, 3);
    }

    [Fact]
    public void BuildGroups_UsesMeasuredPointOrderForSameDimensionBandTransition()
    {
        var dimension = CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        dimension.MeasuredPoints =
        [
            new DrawingPointInfo { X = 0, Y = 0, Order = 0 },
            new DrawingPointInfo { X = 100, Y = 0, Order = 1 },
            new DrawingPointInfo { X = 220, Y = 0, Order = 2 }
        ];

        var secondSegment = new DimensionSegmentInfo
        {
            Id = 1002,
            StartX = 100,
            StartY = 0,
            EndX = 220,
            EndY = 0,
            Distance = 40,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = 100,
                StartY = -1500,
                EndX = 220,
                EndY = -1500
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 0,
                EndX = 100,
                EndY = -1500
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 220,
                StartY = 0,
                EndX = 220,
                EndY = -1500
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = 100,
                MinY = -1500,
                MaxX = 220,
                MaxY = -1500
            }
        };

        dimension.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1440, EndX = 100, EndY = -1440 };
        dimension.Segments[0].DimensionLine = dimension.ReferenceLine;
        dimension.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1440 };
        dimension.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -1440 };
        dimension.Segments.Add(secondSegment);

        var groups = DimensionGroupFactory.BuildGroups([dimension]);

        var group = Assert.Single(groups);
        var member = Assert.Single(group.Members);
        Assert.Equal(0, member.StartX, 3);
        Assert.Equal(0, member.StartY, 3);
        Assert.Equal(220, member.EndX, 3);
        Assert.Equal(0, member.EndY, 3);
    }

    [Fact]
    public void BuildGroups_EliminatesSimpleItemCoveredByMoreInformativeChain()
    {
        var chain = CreateDimension(1, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        chain.MeasuredPoints =
        [
            new DrawingPointInfo { X = 0, Y = 0, Order = 0 },
            new DrawingPointInfo { X = 100, Y = 0, Order = 1 },
            new DrawingPointInfo { X = 220, Y = 0, Order = 2 }
        ];
        chain.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1440, EndX = 100, EndY = -1440 };
        chain.Segments[0].DimensionLine = chain.ReferenceLine;
        chain.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1440 };
        chain.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = -1440 };
        chain.Segments.Add(new DimensionSegmentInfo
        {
            Id = 1002,
            StartX = 100,
            StartY = 0,
            EndX = 220,
            EndY = 0,
            Distance = 40,
            DirectionX = 1,
            DirectionY = 0,
            TopDirection = -1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = 100,
                StartY = -1500,
                EndX = 220,
                EndY = -1500
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = 100,
                StartY = 0,
                EndX = 100,
                EndY = -1500
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = 220,
                StartY = 0,
                EndX = 220,
                EndY = -1500
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = 100,
                MinY = -1500,
                MaxX = 220,
                MaxY = -1500
            }
        });

        var overall = CreateDimension(2, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 220, 0, 0, 220, 5);
        overall.MeasuredPoints =
        [
            new DrawingPointInfo { X = 0, Y = 0, Order = 0 },
            new DrawingPointInfo { X = 220, Y = 0, Order = 1 }
        ];
        overall.ReferenceLine = new DrawingLineInfo { StartX = 0, StartY = -1440, EndX = 220, EndY = -1440 };
        overall.Segments[0].DimensionLine = overall.ReferenceLine;
        overall.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = -1440 };
        overall.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 220, StartY = 0, EndX = 220, EndY = -1440 };

        var groups = DimensionGroupFactory.BuildGroups([chain, overall]);

        var group = Assert.Single(groups);
        var member = Assert.Single(group.Members);
        Assert.Equal(1, member.DimensionId);
        Assert.Equal(3, member.PointList.Count);
    }

    [Fact]
    public void BuildGroups_DoesNotEliminateDistinctNeighbourItems()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 220, 100, 0, 220, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 220, StartY = 0, EndX = 220, EndY = 40 };

        var groups = DimensionGroupFactory.BuildGroups([first, second]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_SelectsOneRepresentativeFromNearbyPacket()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 200, 100, 0, 200, 5);
        var third = CreateDimension(3, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 200, 300, 200, 0, 300, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 300, StartY = 0, EndX = 300, EndY = 40 };

        var groups = DimensionGroupFactory.BuildGroups([first, second, third]);

        var group = Assert.Single(groups);
        var member = Assert.Single(group.Members);
        Assert.Equal(2, member.DimensionId);
    }

    [Fact]
    public void BuildGroups_KeepsSeparateRepresentativePacketsWhenGapExceedsMaximumDistance()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 400, 500, 400, 0, 500, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 400, StartY = 0, EndX = 400, EndY = 40 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 500, StartY = 0, EndX = 500, EndY = 40 };

        var groups = DimensionGroupFactory.BuildGroups([first, second]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_CanDisableRepresentativeSelectionViaReductionPolicy()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 200, 100, 0, 200, 5);
        var third = CreateDimension(3, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 200, 300, 200, 0, 300, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 300, StartY = 0, EndX = 300, EndY = 40 };

        var groups = DimensionGroupFactory.BuildGroups(
            [first, second, third],
            reductionPolicy: new DimensionReductionPolicy
            {
                EnableRepresentativeSelection = false
            });

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Members.Count);
    }

    [Fact]
    public void BuildGroups_CanChooseFirstRepresentativeViaReductionPolicy()
    {
        var first = CreateDimension(1, 10, "FrontView", "Relative", "horizontal", 40, 1, 0, -1, 0, -1, 0, 0, 100, 0, 0, 100, 5);
        var second = CreateDimension(2, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 100, 200, 100, 0, 200, 5);
        var third = CreateDimension(3, 10, "FrontView", "Absolute", "horizontal", 40, 1, 0, -1, 0, -1, 0, 200, 300, 200, 0, 300, 5);

        first.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 0, EndY = 40 };
        first.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 100, StartY = 0, EndX = 100, EndY = 40 };
        second.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineMain = new DrawingLineInfo { StartX = 200, StartY = 0, EndX = 200, EndY = 40 };
        third.Segments[0].LeadLineSecond = new DrawingLineInfo { StartX = 300, StartY = 0, EndX = 300, EndY = 40 };

        var groups = DimensionGroupFactory.BuildGroups(
            [first, second, third],
            reductionPolicy: new DimensionReductionPolicy
            {
                RepresentativeSelectionMode = DimensionRepresentativeSelectionMode.FirstInPacket
            });

        var group = Assert.Single(groups);
        var member = Assert.Single(group.Members);
        Assert.Equal(1, member.DimensionId);
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
        double boundsMaxY,
        DimensionSourceKind sourceKind = DimensionSourceKind.Part)
    {
        var endY = orientation == "vertical" ? 60.0 : startY;
        var offsetStartX = startX + (upX * distance);
        var offsetStartY = startY + (upY * distance);
        var offsetEndX = endX + (upX * distance);
        var offsetEndY = endY + (upY * distance);
        var geometryKind = TeklaDrawingDimensionsApi.ResolveDimensionGeometryKind(orientation);
        var classifiedType = System.Enum.TryParse<DimensionType>(dimensionType, ignoreCase: true, out var explicitDomainType)
            ? explicitDomainType
            : DimensionGroupFactory.MapDomainDimensionType(sourceKind, geometryKind);

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
            SourceKind = sourceKind,
            GeometryKind = geometryKind,
            ClassifiedDimensionType = classifiedType,
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

    private static DimensionSegmentInfo CreateVerticalSegment(
        int id,
        double startX,
        double startY,
        double endX,
        double endY,
        double referenceX)
    {
        return new DimensionSegmentInfo
        {
            Id = id,
            StartX = startX,
            StartY = startY,
            EndX = endX,
            EndY = endY,
            Distance = 40,
            DirectionX = 0,
            DirectionY = 1,
            TopDirection = 1,
            DimensionLine = new DrawingLineInfo
            {
                StartX = referenceX,
                StartY = startY,
                EndX = referenceX,
                EndY = endY
            },
            LeadLineMain = new DrawingLineInfo
            {
                StartX = startX,
                StartY = startY,
                EndX = referenceX,
                EndY = startY
            },
            LeadLineSecond = new DrawingLineInfo
            {
                StartX = endX,
                StartY = endY,
                EndX = referenceX,
                EndY = endY
            },
            Bounds = new DrawingBoundsInfo
            {
                MinX = referenceX,
                MinY = System.Math.Min(startY, endY),
                MaxX = referenceX,
                MaxY = System.Math.Max(startY, endY)
            }
        };
    }
}
