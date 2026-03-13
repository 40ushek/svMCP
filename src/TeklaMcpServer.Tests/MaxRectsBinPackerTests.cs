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

    private static bool Intersects(PackedRectangle left, PackedRectangle right)
    {
        return !(left.X + left.Width <= right.X
            || right.X + right.Width <= left.X
            || left.Y + left.Height <= right.Y
            || right.Y + right.Height <= left.Y);
    }
}
