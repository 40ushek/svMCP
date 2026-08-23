using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>Pure depth-window rules: no Tekla model is needed to test these boundaries.</summary>
public sealed class ViewDepthWindowTests
{
    private static readonly DepthBox Window = new(0, 0, 0, 100, 100, 100);

    [Fact]
    public void ABoxWithClearSpaceFromTheWindowIsDefinitelyOutside()
    {
        var part = new DepthBox(100.01, 10, 10, 200, 90, 90);

        Assert.Equal(DepthBoxRelation.Disjoint, Window.Classify(part));
    }

    [Fact]
    public void ABoxThatOnlyTouchesTheWindowIsAmbiguousNotIncluded()
    {
        var part = new DepthBox(100, 10, 10, 200, 90, 90);

        Assert.Equal(DepthBoxRelation.BoundaryTouch, Window.Classify(part));
    }

    [Fact]
    public void APositiveVolumeOverlapIsIncludedByTheViewAlignedBroadPhase()
    {
        var part = new DepthBox(99, 10, 10, 200, 90, 90);

        Assert.Equal(DepthBoxRelation.Overlaps, Window.Classify(part));
    }

    [Fact]
    public void ALongMemberSpanningTheWindowIsNotRejectedForHavingEndpointsOutsideIt()
    {
        // A section through the middle of a beam has both end vertices outside the
        // depth window. Its view-aligned solid box still has positive volume overlap.
        var longMember = new DepthBox(10, 10, -1_000, 90, 90, 1_000);

        Assert.Equal(DepthBoxRelation.Overlaps, Window.Classify(longMember));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 100, 100)]
    [InlineData(0, 0, 0, 100, 100, 0)]
    [InlineData(double.NaN, 0, 0, 100, 100, 100)]
    public void DegenerateOrNonFiniteWindowsAreNeverUsedForSelection(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        var invalid = new DepthBox(minX, minY, minZ, maxX, maxY, maxZ);

        Assert.False(invalid.IsValid);
        Assert.Equal(DepthBoxRelation.Invalid, Window.Classify(invalid));
    }
}
