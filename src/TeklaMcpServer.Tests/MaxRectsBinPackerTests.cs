using TeklaMcpServer.Api.Algorithms.Packing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MaxRectsBinPackerTests
{
    [Fact]
    public void TryInsert_RespectsBlockedRectangles()
    {
        var blocked = new[]
        {
            new PackedRectangle(35, 35, 30, 30)
        };
        var packer = new MaxRectsBinPacker(100, 100, allowRotation: false, blockedRectangles: blocked);

        var insertedFirst = packer.TryInsert(30, 30, MaxRectsHeuristic.BestAreaFit, out var first);
        var insertedSecond = packer.TryInsert(30, 30, MaxRectsHeuristic.BestAreaFit, out var second);

        Assert.True(insertedFirst);
        Assert.True(insertedSecond);
        Assert.False(Intersects(first, blocked[0]));
        Assert.False(Intersects(second, blocked[0]));
        Assert.False(Intersects(first, second));
    }

    [Fact]
    public void TryInsert_ReturnsFalse_WhenBlockedRectangleConsumesWholeBin()
    {
        var blocked = new[]
        {
            new PackedRectangle(0, 0, 100, 100)
        };
        var packer = new MaxRectsBinPacker(100, 100, allowRotation: false, blockedRectangles: blocked);

        var inserted = packer.TryInsert(10, 10, MaxRectsHeuristic.BestAreaFit, out _);

        Assert.False(inserted);
    }

    [Fact]
    public void Constructor_HandlesOverlappingBlockedRectanglesWithoutFragmentExplosion()
    {
        var blocked = Enumerable.Range(0, 14)
            .Select(index => new PackedRectangle(
                10 + (index * 4),
                10 + ((index % 4) * 12),
                55,
                48))
            .ToArray();

        var packer = new MaxRectsBinPacker(
            120,
            90,
            allowRotation: false,
            blockedRectangles: blocked);

        var freeRectangles = packer.GetFreeRectanglesSnapshot();
        Assert.All(freeRectangles, free =>
        {
            Assert.True(free.Width > 0);
            Assert.True(free.Height > 0);
            Assert.DoesNotContain(blocked, blockedRect => Intersects(free, blockedRect));
        });
    }

    private static bool Intersects(PackedRectangle left, PackedRectangle right)
    {
        return !(left.X + left.Width <= right.X
            || right.X + right.Width <= left.X
            || left.Y + left.Height <= right.Y
            || right.Y + right.Height <= left.Y);
    }
}
