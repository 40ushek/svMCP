using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingMarkLayoutAdapterTests
{
    [Fact]
    public void TryCreateLayoutItem_ConvertsViewLocalCornersToCenterRelativeLocalCorners()
    {
        var markContext = CreateMarkContext();
        var marksViewContext = CreateMarksViewContext();

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext: null, out var item);

        Assert.True(created);
        Assert.Equal(100, item.CurrentX);
        Assert.Equal(200, item.CurrentY);
        Assert.Equal(
            new[]
            {
                new[] { -20.0, -10.0 },
                new[] { 20.0, -10.0 },
                new[] { 20.0, 10.0 },
                new[] { -20.0, 10.0 },
            },
            item.LocalCorners,
            CornerComparer.Instance);
    }

    [Fact]
    public void TryCreateLayoutItem_UsesViewBoundsWithLayoutMargin()
    {
        var markContext = CreateMarkContext();
        var marksViewContext = CreateMarksViewContext();

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext: null, out var item);

        Assert.True(created);
        Assert.Equal(-70, item.BoundsMinX);
        Assert.Equal(70, item.BoundsMaxX);
        Assert.Equal(-50, item.BoundsMinY);
        Assert.Equal(50, item.BoundsMaxY);
    }

    [Fact]
    public void TryCreateLayoutItem_UsesBaselineAxisFromMarkContext()
    {
        var markContext = CreateMarkContext();
        var marksViewContext = CreateMarksViewContext();

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext: null, out var item);

        Assert.True(created);
        Assert.True(item.HasAxis);
        Assert.Equal(1, item.AxisDx, 6);
        Assert.Equal(0, item.AxisDy, 6);
    }

    [Fact]
    public void TryCreateLayoutItem_DoesNotExposeAxisForNonBaselineMarks()
    {
        var markContext = CreateMarkContext();
        markContext.PlacingType = "LeaderLinePlacing";
        var marksViewContext = CreateMarksViewContext();

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext: null, out var item);

        Assert.True(created);
        Assert.False(item.HasAxis);
        Assert.Equal(0, item.AxisDx, 6);
        Assert.Equal(0, item.AxisDy, 6);
    }

    [Fact]
    public void TryCreateLayoutItem_UsesMarkContextSourceForPartCenterLookup()
    {
        var markContext = CreateMarkContext();
        markContext.SourceKind = nameof(MarkLayoutSourceKind.Part);
        markContext.ModelId = 42;
        var marksViewContext = CreateMarksViewContext();
        var viewContext = new DrawingViewContext();
        viewContext.Parts.Add(new PartGeometryInViewResult
        {
            Success = true,
            ModelId = 42,
            SolidVertices = [new[] { 120.0, 220.0 }, new[] { 140.0, 220.0 }, new[] { 140.0, 240.0 }, new[] { 120.0, 240.0 }],
            BboxMin = [120.0, 220.0],
            BboxMax = [140.0, 240.0]
        });

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext, out var item);

        Assert.True(created);
        Assert.Equal(MarkLayoutSourceKind.Part, item.SourceKind);
        Assert.Equal(42, item.SourceModelId);
        Assert.NotNull(item.SourceCenterX);
        Assert.NotNull(item.SourceCenterY);
        Assert.Equal(130, item.SourceCenterX.Value, 6);
        Assert.Equal(230, item.SourceCenterY.Value, 6);
    }

    [Fact]
    public void TryCreateLayoutItem_ReturnsFalseForDegenerateGeometry()
    {
        var markContext = CreateMarkContext();
        markContext.Geometry.Width = 0.05;
        markContext.Geometry.Height = 0.05;
        var marksViewContext = CreateMarksViewContext();

        var created = TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(markContext, marksViewContext, viewContext: null, out _);

        Assert.False(created);
    }

    [Fact]
    public void TryCreateLayoutItem_BaselinePlacementPreservesAxisBoundOverlapResolution()
    {
        var resolver = new MarkOverlapResolver();
        var marksViewContext = CreateMarksViewContext();
        var firstContext = CreateMarkContext();
        var secondContext = CreateMarkContext();
        secondContext.MarkId = 8;
        secondContext.Anchor = new DrawingPointInfo { X = 110, Y = 200 };
        secondContext.CurrentCenter = new DrawingPointInfo { X = 120, Y = 200 };
        secondContext.Geometry.Center = new DrawingPointInfo { X = 120, Y = 200 };
        secondContext.Geometry.Bounds = new DrawingBoundsInfo
        {
            MinX = 100,
            MinY = 190,
            MaxX = 140,
            MaxY = 210
        };
        secondContext.Geometry.Corners.Clear();
        secondContext.Geometry.Corners.AddRange(
        [
            new DrawingPointInfo { X = 100, Y = 190, Order = 0 },
            new DrawingPointInfo { X = 140, Y = 190, Order = 1 },
            new DrawingPointInfo { X = 140, Y = 210, Order = 2 },
            new DrawingPointInfo { X = 100, Y = 210, Order = 3 }
        ]);

        Assert.True(TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(firstContext, marksViewContext, viewContext: null, out var firstItem));
        Assert.True(TeklaDrawingMarkLayoutAdapter.TryCreateLayoutItem(secondContext, marksViewContext, viewContext: null, out var secondItem));

        var placements = new[]
        {
            CreatePlacement(firstItem),
            CreatePlacement(secondItem)
        };

        var resolved = resolver.ResolvePlacedMarks(
            placements,
            new MarkLayoutOptions
            {
                Gap = 2.0,
                MaxResolverIterations = 24,
                MaxDistanceFromAnchor = 40.0
            },
            out _);

        var first = resolved.Single(item => item.Id == firstItem.Id);
        var second = resolved.Single(item => item.Id == secondItem.Id);

        Assert.Equal(0, resolver.CountOverlaps(resolved));
        Assert.Equal(firstItem.CurrentY, first.Y, 6);
        Assert.Equal(secondItem.CurrentY, second.Y, 6);
        Assert.True(first.X < firstItem.CurrentX);
        Assert.True(second.X > secondItem.CurrentX);
    }

    [Fact]
    public void TryResolvePreferredLeaderAnchorTarget_UsesBestCandidateFromLeaderSnapshot()
    {
        var polygon = TeklaDrawingMarkLayoutAdapterTestFactory.CreateRectangle(width: 100.0, height: 50.0);
        var markContext = TeklaDrawingMarkLayoutAdapterTestFactory.CreateLeaderMarkContext(
            anchorX: 90.0,
            anchorY: 2.0,
            leaderEndX: 140.0,
            leaderEndY: 12.0);

        var resolved = TeklaDrawingMarkLayoutAdapter.TryResolvePreferredLeaderAnchorTarget(
            markContext,
            centerX: 140.0,
            centerY: 2.0,
            viewScale: 1.0,
            polygon,
            out var targetX,
            out var targetY);

        Assert.True(resolved);
        Assert.Equal(90.0, targetX, 6);
        Assert.Equal(12.0, targetY, 6);
    }

    [Fact]
    public void TryResolvePreferredLeaderAnchorTarget_FallsBackToDirectResolver_WhenLeaderSnapshotMissing()
    {
        var polygon = TeklaDrawingMarkLayoutAdapterTestFactory.CreateRectangle(width: 100.0, height: 50.0);
        var markContext = TeklaDrawingMarkLayoutAdapterTestFactory.CreateLeaderMarkContext(
            anchorX: 90.0,
            anchorY: 25.0,
            leaderEndX: 140.0,
            leaderEndY: 25.0);
        markContext.LeaderSnapshot = null;

        var resolved = TeklaDrawingMarkLayoutAdapter.TryResolvePreferredLeaderAnchorTarget(
            markContext,
            centerX: 140.0,
            centerY: 25.0,
            viewScale: 1.0,
            polygon,
            out var targetX,
            out var targetY);

        Assert.True(resolved);
        Assert.Equal(90.0, targetX, 6);
        Assert.Equal(25.0, targetY, 6);
    }

    [Fact]
    public void TryResolvePreferredLeaderAnchorTarget_SkipsCandidateThatWouldLengthenLine()
    {
        var polygon = TeklaDrawingMarkLayoutAdapterTestFactory.CreateRectangle(width: 100.0, height: 50.0);
        var markContext = TeklaDrawingMarkLayoutAdapterTestFactory.CreateLeaderMarkContext(
            anchorX: 97.0,
            anchorY: 25.0,
            leaderEndX: 95.0,
            leaderEndY: 25.0);

        var resolved = TeklaDrawingMarkLayoutAdapter.TryResolvePreferredLeaderAnchorTarget(
            markContext,
            centerX: 140.0,
            centerY: 25.0,
            viewScale: 1.0,
            polygon,
            out _,
            out _);

        Assert.False(resolved);
    }

    private static MarksViewContext CreateMarksViewContext() => new()
    {
        ViewId = 11,
        ViewBounds = new DrawingBoundsInfo
        {
            MinX = -60,
            MinY = -40,
            MaxX = 60,
            MaxY = 40
        }
    };

    private static MarkLayoutPlacement CreatePlacement(MarkLayoutItem item) => new()
    {
        Id = item.Id,
        X = item.CurrentX,
        Y = item.CurrentY,
        Width = item.Width,
        Height = item.Height,
        AnchorX = item.AnchorX,
        AnchorY = item.AnchorY,
        HasLeaderLine = item.HasLeaderLine,
        HasAxis = item.HasAxis,
        AxisDx = item.AxisDx,
        AxisDy = item.AxisDy,
        CanMove = true,
        LocalCorners = item.LocalCorners.Select(corner => new[] { corner[0], corner[1] }).ToList()
    };

    private static MarkContext CreateMarkContext() => new MarkContext
    {
        MarkId = 7,
        SourceKind = nameof(MarkLayoutSourceKind.Unknown),
        ModelId = 42,
        ViewId = 11,
        ViewScale = 1.0,
        PlacingType = "BaseLinePlacing",
        Anchor = new DrawingPointInfo { X = 90, Y = 200 },
        CurrentCenter = new DrawingPointInfo { X = 100, Y = 200 },
        HasLeaderLine = false,
        CanMove = true,
        Geometry = new MarkGeometryContext
        {
            Bounds = new DrawingBoundsInfo
            {
                MinX = 80,
                MinY = 190,
                MaxX = 120,
                MaxY = 210
            },
            Center = new DrawingPointInfo { X = 100, Y = 200 },
            Width = 40,
            Height = 20,
            IsReliable = true
        },
        Axis = new MarkAxisContext
        {
            Direction = new DrawingVectorInfo { X = 1, Y = 0 }
        }
    }.WithCorners(
        new DrawingPointInfo { X = 80, Y = 190, Order = 0 },
        new DrawingPointInfo { X = 120, Y = 190, Order = 1 },
        new DrawingPointInfo { X = 120, Y = 210, Order = 2 },
        new DrawingPointInfo { X = 80, Y = 210, Order = 3 });
}

internal static class TeklaDrawingMarkLayoutAdapterTestExtensions
{
    internal static MarkContext WithCorners(this MarkContext markContext, params DrawingPointInfo[] corners)
    {
        markContext.Geometry.Corners.AddRange(corners);
        return markContext;
    }
}

internal static partial class TeklaDrawingMarkLayoutAdapterTestFactory
{
    internal static MarkContext CreateLeaderMarkContext(
        double anchorX,
        double anchorY,
        double leaderEndX,
        double leaderEndY)
    {
        return new MarkContext
        {
            MarkId = 17,
            SourceKind = nameof(MarkLayoutSourceKind.Part),
            ModelId = 42,
            ViewId = 11,
            ViewScale = 1.0,
            PlacingType = "LeaderLinePlacing",
            Anchor = new DrawingPointInfo { X = anchorX, Y = anchorY },
            CurrentCenter = new DrawingPointInfo { X = 140.0, Y = leaderEndY },
            HasLeaderLine = true,
            CanMove = true,
            Geometry = new MarkGeometryContext
            {
                Bounds = new DrawingBoundsInfo
                {
                    MinX = 120.0,
                    MinY = leaderEndY - 10.0,
                    MaxX = 160.0,
                    MaxY = leaderEndY + 10.0
                },
                Center = new DrawingPointInfo { X = 140.0, Y = leaderEndY },
                Width = 40.0,
                Height = 20.0,
                IsReliable = true
            },
            LeaderSnapshot = new LeaderSnapshot
            {
                MarkId = 17,
                AnchorPoint = new DrawingPointInfo { X = anchorX, Y = anchorY },
                LeaderEndPoint = new DrawingPointInfo { X = leaderEndX, Y = leaderEndY },
                InsertionPoint = new DrawingPointInfo { X = 160.0, Y = leaderEndY + 5.0 },
                LeaderLength = System.Math.Sqrt(System.Math.Pow(anchorX - leaderEndX, 2) + System.Math.Pow(anchorY - leaderEndY, 2)),
                Delta = new DrawingVectorInfo { X = 20.0, Y = 5.0 }
            }
        }.WithCorners(
            new DrawingPointInfo { X = 120.0, Y = leaderEndY - 10.0, Order = 0 },
            new DrawingPointInfo { X = 160.0, Y = leaderEndY - 10.0, Order = 1 },
            new DrawingPointInfo { X = 160.0, Y = leaderEndY + 10.0, Order = 2 },
            new DrawingPointInfo { X = 120.0, Y = leaderEndY + 10.0, Order = 3 });
    }

    internal static double[][] CreateRectangle(double width, double height)
    {
        return
        [
            [0.0, 0.0],
            [width, 0.0],
            [width, height],
            [0.0, height],
        ];
    }
}

internal sealed class CornerComparer : IEqualityComparer<double[]>
{
    public static CornerComparer Instance { get; } = new();

    public bool Equals(double[]? x, double[]? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x == null || y == null || x.Length != y.Length)
            return false;

        return x.Zip(y).All(pair => System.Math.Abs(pair.First - pair.Second) < 0.000001);
    }

    public int GetHashCode(double[] obj) => 0;
}
