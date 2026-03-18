using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingDimensionsApiTests
{
    [Theory]
    [InlineData(0, 0, 100, 0, "horizontal")]
    [InlineData(0, 0, 0, 100, "vertical")]
    [InlineData(0, 0, 100, 50, "angled")]
    public void DetermineDimensionOrientation_ClassifiesSingleSegment(
        double startX,
        double startY,
        double endX,
        double endY,
        string expected)
    {
        var orientation = TeklaDrawingDimensionsApi.DetermineDimensionOrientation(
        [
            new DimensionSegmentInfo
            {
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY
            }
        ]);

        Assert.Equal(expected, orientation);
    }

    [Fact]
    public void CombineBounds_MergesSegmentBoundsIntoSetBounds()
    {
        var bounds = TeklaDrawingDimensionsApi.CombineBounds(
        [
            new DrawingBoundsInfo { MinX = 10, MinY = 20, MaxX = 30, MaxY = 40 },
            null,
            new DrawingBoundsInfo { MinX = 5, MinY = 12, MaxX = 45, MaxY = 32 }
        ]);

        Assert.NotNull(bounds);
        Assert.Equal(5, bounds!.MinX, 3);
        Assert.Equal(12, bounds.MinY, 3);
        Assert.Equal(45, bounds.MaxX, 3);
        Assert.Equal(40, bounds.MaxY, 3);
        Assert.Equal(40, bounds.Width, 3);
        Assert.Equal(28, bounds.Height, 3);
    }

    [Fact]
    public void ProjectPointToReferenceLine_UsesReferenceDirectionInsteadOfMeasuredPointLine()
    {
        var referenceStart = TeklaDrawingDimensionsApi.CreateReferencePoint(10, 20, (0, 1), 30);
        var projected = TeklaDrawingDimensionsApi.ProjectPointToReferenceLine(40, 60, referenceStart.X, referenceStart.Y, 1, 0);

        Assert.Equal(10, referenceStart.X, 3);
        Assert.Equal(50, referenceStart.Y, 3);
        Assert.Equal(40, projected.X, 3);
        Assert.Equal(50, projected.Y, 3);
    }

    [Fact]
    public void TryCreateCommonReferenceLine_UsesSharedOffsetForWholeDimensionSet()
    {
        var referenceLine = TeklaDrawingDimensionsApi.TryCreateCommonReferenceLine(
            [
                (0d, 0d),
                (100d, 0d),
                (100d, 40d),
                (200d, 40d)
            ],
            (0d, -1d),
            25d,
            out var direction);

        Assert.NotNull(referenceLine);
        Assert.Equal(1, direction.X, 6);
        Assert.Equal(0, direction.Y, 6);
        Assert.Equal(-25, referenceLine!.StartY, 3);
        Assert.Equal(-25, referenceLine.EndY, 3);
    }

    [Fact]
    public void GetDimensionsDto_SerializesOldAndNewFields()
    {
        var result = new GetDimensionsResult
        {
            Total = 1,
            Dimensions =
            [
                new DrawingDimensionInfo
                {
                    Id = 10,
                    Type = "StraightDimensionSet",
                    DimensionType = "PartLongitudinal",
                    ViewId = 20,
                    ViewType = "FrontView",
                    Orientation = "horizontal",
                    Distance = 12.5,
                    DirectionX = 1,
                    DirectionY = 0,
                    TopDirection = -1,
                    Bounds = new DrawingBoundsInfo { MinX = 1, MinY = 2, MaxX = 6, MaxY = 8 },
                    ReferenceLine = new DrawingLineInfo { StartX = 1, StartY = 14.5, EndX = 6, EndY = 14.5 },
                    MeasuredPoints =
                    [
                        new DrawingPointInfo { X = 1, Y = 2, Order = 0 },
                        new DrawingPointInfo { X = 6, Y = 2, Order = 1 }
                    ],
                    Segments =
                    [
                        new DimensionSegmentInfo
                        {
                            Id = 11,
                            StartX = 1,
                            StartY = 2,
                            EndX = 6,
                            EndY = 2,
                            Distance = 12.5,
                            DirectionX = 1,
                            DirectionY = 0,
                            TopDirection = -1,
                            Bounds = new DrawingBoundsInfo { MinX = 1, MinY = 2, MaxX = 6, MaxY = 2 },
                            TextBounds = null,
                            DimensionLine = new DrawingLineInfo { StartX = 1, StartY = 14.5, EndX = 6, EndY = 14.5 },
                            LeadLineMain = new DrawingLineInfo { StartX = 1, StartY = 2, EndX = 1, EndY = 14.5 },
                            LeadLineSecond = new DrawingLineInfo { StartX = 6, StartY = 2, EndX = 6, EndY = 14.5 }
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("\"Id\":10", json);
        Assert.Contains("\"Distance\":12.5", json);
        Assert.Contains("\"DimensionType\":\"PartLongitudinal\"", json);
        Assert.Contains("\"ViewId\":20", json);
        Assert.Contains("\"ViewType\":\"FrontView\"", json);
        Assert.Contains("\"Orientation\":\"horizontal\"", json);
        Assert.Contains("\"DirectionX\":1", json);
        Assert.Contains("\"TopDirection\":-1", json);
        Assert.Contains("\"ReferenceLine\":", json);
        Assert.Contains("\"MeasuredPoints\":", json);
        Assert.Contains("\"Bounds\":", json);
        Assert.Contains("\"DimensionLine\":", json);
        Assert.Contains("\"LeadLineMain\":", json);
        Assert.Contains("\"TextBounds\":null", json);
    }

    [Fact]
    public void BuildMeasuredPointList_OrdersChainPointsAlongTraversedPath()
    {
        var points = TeklaDrawingDimensionsApi.BuildMeasuredPointList(
        [
            new DimensionSegmentInfo { StartX = 0, StartY = 10, EndX = 0, EndY = 20 },
            new DimensionSegmentInfo { StartX = 50, StartY = 30, EndX = 0, EndY = 20 },
            new DimensionSegmentInfo { StartX = 50, StartY = 30, EndX = 50, EndY = 40 }
        ], 0, 1);

        Assert.Equal(4, points.Count);
        Assert.Collection(
            points,
            p => { Assert.Equal(50, p.X, 3); Assert.Equal(40, p.Y, 3); Assert.Equal(0, p.Order); },
            p => { Assert.Equal(50, p.X, 3); Assert.Equal(30, p.Y, 3); Assert.Equal(1, p.Order); },
            p => { Assert.Equal(0, p.X, 3); Assert.Equal(20, p.Y, 3); Assert.Equal(2, p.Order); },
            p => { Assert.Equal(0, p.X, 3); Assert.Equal(10, p.Y, 3); Assert.Equal(3, p.Order); });
    }
}
