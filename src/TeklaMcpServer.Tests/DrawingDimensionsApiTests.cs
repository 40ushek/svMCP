using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
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

    [Theory]
    [InlineData("horizontal", 1)]
    [InlineData("vertical", 2)]
    [InlineData("angled", 3)]
    [InlineData("", 0)]
    public void ResolveDimensionGeometryKind_MapsOrientationToFreeModel(
        string orientation,
        int expected)
    {
        var geometryKind = TeklaDrawingDimensionsApi.ResolveDimensionGeometryKind(orientation);

        Assert.Equal((DimensionGeometryKind)expected, geometryKind);
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
            GroupCount = 1,
            Groups =
            [
                new DimensionGroupInfo
                {
                    ViewId = 20,
                    ViewType = "FrontView",
                    DimensionType = "Horizontal",
                    TeklaDimensionType = "Absolute",
                    Direction = new DrawingVectorInfo { X = 1, Y = 0 },
                    TopDirection = -1,
                    ReferenceLine = new DrawingLineInfo { StartX = 1, StartY = 14.5, EndX = 6, EndY = 14.5 },
                    LeadLineMain = new DrawingLineInfo { StartX = 1, StartY = 2, EndX = 1, EndY = 14.5 },
                    LeadLineSecond = new DrawingLineInfo { StartX = 6, StartY = 2, EndX = 6, EndY = 14.5 },
                    MaximumDistance = 12.5,
                    Items =
                    [
                        new DimensionItemInfo
                        {
                            Id = 10,
                            SegmentIds = [11],
                            ViewId = 20,
                            DimensionType = "Horizontal",
                            TeklaDimensionType = "Absolute",
                            Distance = 12.5,
                            ReferenceLine = new DrawingLineInfo { StartX = 1, StartY = 14.5, EndX = 6, EndY = 14.5 },
                            StartPoint = new DrawingPointInfo { X = 1, Y = 2, Order = 0 },
                            EndPoint = new DrawingPointInfo { X = 6, Y = 2, Order = 1 },
                            CenterPoint = new DrawingPointInfo { X = 3.5, Y = 2, Order = -1 },
                            PointList =
                            [
                                new DrawingPointInfo { X = 1, Y = 2, Order = 0 },
                                new DrawingPointInfo { X = 6, Y = 2, Order = 1 }
                            ],
                            LengthList = [5],
                            RealLengthList = [5]
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("\"Id\":10", json);
        Assert.Contains("\"GroupCount\":1", json);
        Assert.Contains("\"Distance\":12.5", json);
        Assert.Contains("\"DimensionType\":\"Horizontal\"", json);
        Assert.Contains("\"ViewId\":20", json);
        Assert.Contains("\"ViewType\":\"FrontView\"", json);
        Assert.Contains("\"TeklaDimensionType\":\"Absolute\"", json);
        Assert.Contains("\"Direction\":", json);
        Assert.Contains("\"TopDirection\":-1", json);
        Assert.Contains("\"ReferenceLine\":", json);
        Assert.Contains("\"PointList\":", json);
        Assert.Contains("\"LeadLineMain\":", json);
        Assert.Contains("\"MaximumDistance\":12.5", json);
        Assert.Contains("\"SegmentIds\":[11]", json);
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

    [Fact]
    public void ApplyFrameSizeCorrection_UsesLegacyWidthBasedCoefficients()
    {
        var corrected = TeklaDrawingDimensionsApi.ApplyFrameSizeCorrection(100, 20, FrameTypes.Rectangular);

        Assert.Equal(118, corrected.Width, 3);
        Assert.Equal(31, corrected.Height, 3);
    }

    [Fact]
    public void ApplyFrameSizeCorrection_UsesLegacyDiagonalBasedCoefficients()
    {
        var corrected = TeklaDrawingDimensionsApi.ApplyFrameSizeCorrection(100, 20, FrameTypes.Circle);
        var diagonal = Math.Sqrt((100 * 100) + (20 * 20));

        Assert.Equal(diagonal * 1.15, corrected.Width, 3);
        Assert.Equal(diagonal * 1.15, corrected.Height, 3);
    }

    [Fact]
    public void ApplyFrameSizeCorrectionToPolygon_PreservesCenterWhileExpandingBox()
    {
        var corrected = TeklaDrawingDimensionsApi.ApplyFrameSizeCorrectionToPolygon(
        [
            [0d, 0d],
            [0d, 20d],
            [100d, 20d],
            [100d, 0d]
        ], FrameTypes.Line);

        Assert.Collection(
            corrected,
            p => { Assert.Equal(-6, p[0], 3); Assert.Equal(-4, p[1], 3); },
            p => { Assert.Equal(-6, p[0], 3); Assert.Equal(24, p[1], 3); },
            p => { Assert.Equal(106, p[0], 3); Assert.Equal(24, p[1], 3); },
            p => { Assert.Equal(106, p[0], 3); Assert.Equal(-4, p[1], 3); });
    }

    [Fact]
    public void NormalizeTemporaryDimensionValue_StripsLegacyWrapper()
    {
        var normalized = TeklaDrawingDimensionsApi.NormalizeTemporaryDimensionValue(
            "[[1250]]",
            DimensionSetBaseAttributes.DimensionValueUnits.Millimeter,
            negative: false);

        Assert.Equal("1250", normalized);
    }

    [Fact]
    public void NormalizeTemporaryDimensionValue_AppendsInchSuffixWhenNeeded()
    {
        var normalized = TeklaDrawingDimensionsApi.NormalizeTemporaryDimensionValue(
            "[[1-1\\\\2]]",
            DimensionSetBaseAttributes.DimensionValueUnits.Inch,
            negative: false);

        Assert.Equal("1-1\\\\2\"", normalized);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(-1, 1, -1)]
    [InlineData(1, -1, -1)]
    [InlineData(-1, -1, 1)]
    [InlineData(0, 1, 1)]
    public void ResolveDimensionTextSideSign_UsesTopDirectionAndPlacingDirection(
        int topDirection,
        int placingDirectionSign,
        int expected)
    {
        var sideSign = TeklaDrawingDimensionsApi.ResolveDimensionTextSideSign(topDirection, placingDirectionSign);

        Assert.Equal(expected, sideSign);
    }

    [Fact]
    public void ApplyDimensionTextLineOffsets_TrimsLineSpanUsingNativeOffsets()
    {
        var trimmed = TeklaDrawingDimensionsApi.ApplyDimensionTextLineOffsets(
            new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 100, EndY = 0 },
            startOffset: 20,
            endOffset: 10);

        Assert.Equal(20, trimmed.StartX, 3);
        Assert.Equal(0, trimmed.StartY, 3);
        Assert.Equal(90, trimmed.EndX, 3);
        Assert.Equal(0, trimmed.EndY, 3);
    }

    [Fact]
    public void TryGetDimStyleLineVector_UsesLegacyOrientationRule()
    {
        var ok = TeklaDrawingDimensionsApi.TryGetDimStyleLineVector(
            new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 100, EndY = 0 },
            out var lineVector);

        Assert.True(ok);
        Assert.Equal(1, lineVector.X, 6);
        Assert.Equal(0, lineVector.Y, 6);
    }

    [Fact]
    public void CreateDimStyleTextPolygon_ShiftsCenterByQuarterScaleAlongLine()
    {
        var polygon = TeklaDrawingDimensionsApi.CreateDimStyleTextPolygon(
            new DrawingLineInfo { StartX = 0, StartY = 0, EndX = 100, EndY = 0 },
            widthAlongLine: 20,
            heightPerpendicularToLine: 10,
            viewScale: 8);

        Assert.NotNull(polygon);
        Assert.Collection(
            polygon!,
            p => { Assert.Equal(42, p[0], 3); Assert.Equal(-5, p[1], 3); },
            p => { Assert.Equal(62, p[0], 3); Assert.Equal(-5, p[1], 3); },
            p => { Assert.Equal(62, p[0], 3); Assert.Equal(5, p[1], 3); },
            p => { Assert.Equal(42, p[0], 3); Assert.Equal(5, p[1], 3); });
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, -10, true)]
    [InlineData(-10, 10, false)]
    public void ComparePointsLeftToRight_MatchesLegacyDimRule(
        double leftX,
        double leftY,
        bool expected)
    {
        var result = TeklaDrawingDimensionsApi.ComparePointsLeftToRight(
            (leftX, leftY),
            (0, 0));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FilterOutlierPoints_RemovesFarOriginLikeOutlier()
    {
        var points = new List<Point>
        {
            new(100, 100, 0),
            new(102, 99, 0),
            new(101, 103, 0),
            new(98, 101, 0),
            new(5000, 5000, 0)
        };

        var filtered = TeklaDrawingDimensionsApi.FilterOutlierPoints(points);

        Assert.Equal(4, filtered.Count);
        Assert.DoesNotContain(filtered, p => System.Math.Abs(p.X - 5000) < 0.001 && System.Math.Abs(p.Y - 5000) < 0.001);
    }

    [Fact]
    public void FilterOutlierPoints_PreservesRectangleExtremes()
    {
        var points = new List<Point>
        {
            new(0, 0, 0),
            new(100, 0, 0),
            new(100, 50, 0),
            new(0, 50, 0),
            new(50, 25, 0)
        };

        var filtered = TeklaDrawingDimensionsApi.FilterOutlierPoints(points);

        Assert.Equal(5, filtered.Count);
        Assert.Contains(filtered, p => System.Math.Abs(p.X) < 0.001 && System.Math.Abs(p.Y) < 0.001);
        Assert.Contains(filtered, p => System.Math.Abs(p.X - 100) < 0.001 && System.Math.Abs(p.Y) < 0.001);
        Assert.Contains(filtered, p => System.Math.Abs(p.X - 100) < 0.001 && System.Math.Abs(p.Y - 50) < 0.001);
        Assert.Contains(filtered, p => System.Math.Abs(p.X) < 0.001 && System.Math.Abs(p.Y - 50) < 0.001);
    }

    [Fact]
    public void SimplifyHull_RemovesNearCollinearSnapVertex()
    {
        var hull = new List<Point>
        {
            new(0, 0, 0),
            new(50, 0.4, 0),
            new(100, 0, 0),
            new(100, 50, 0),
            new(0, 50, 0)
        };

        var simplified = TeklaDrawingDimensionsApi.SimplifyHull(hull);

        Assert.Equal(4, simplified.Count);
        Assert.DoesNotContain(simplified, p => System.Math.Abs(p.X - 50) < 0.001 && System.Math.Abs(p.Y - 0.4) < 0.001);
    }

    [Fact]
    public void SimplifyHull_PreservesRectangleCorners()
    {
        var hull = new List<Point>
        {
            new(0, 0, 0),
            new(100, 0, 0),
            new(100, 50, 0),
            new(0, 50, 0)
        };

        var simplified = TeklaDrawingDimensionsApi.SimplifyHull(hull);

        Assert.Equal(4, simplified.Count);
    }
}
