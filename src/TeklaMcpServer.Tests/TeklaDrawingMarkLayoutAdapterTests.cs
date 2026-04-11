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
