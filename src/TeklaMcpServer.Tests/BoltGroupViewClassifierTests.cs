using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltGroupViewClassifierTests
{
    [Theory]
    [InlineData(0, 0, 1, "Plan")]
    [InlineData(0, 0, -1, "Plan")]
    [InlineData(1, 0, 0, "Side")]
    [InlineData(0, -1, 0, "Side")]
    [InlineData(0, 0.5, 0.8660254037844386, "Skewed")]
    public void ClassFollowsTheAngleToTheViewNormal(double x, double y, double z, string expected) =>
        Assert.Equal(expected, BoltGroupViewClassifier.Classify([x, y, z], 1).ToString());

    [Fact]
    public void BothLimitsAreInclusiveAndSymmetric()
    {
        Assert.Equal(BoltGroupViewClass.Plan, BoltGroupViewClassifier.Classify(Tilted(9.999999), 10));
        Assert.Equal(BoltGroupViewClass.Skewed, BoltGroupViewClassifier.Classify(Tilted(10.01), 10));
        Assert.Equal(BoltGroupViewClass.Side, BoltGroupViewClassifier.Classify(Tilted(80.000001), 10));
        Assert.Equal(BoltGroupViewClass.Skewed, BoltGroupViewClassifier.Classify(Tilted(79.99), 10));
        Assert.Equal(BoltGroupViewClass.Plan, BoltGroupViewClassifier.Classify([0, 0, 1], 0));
        Assert.Equal(BoltGroupViewClass.Side, BoltGroupViewClassifier.Classify([1, 0, 0], 0));
    }

    [Fact]
    public void AnAxisNeedNotBeUnitLength() =>
        Assert.Equal(BoltGroupViewClass.Plan, BoltGroupViewClassifier.Classify([0, 0, 5], 1));

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new double[] { 0, 0 })]
    [InlineData(new double[] { 0, 0, 0 })]
    [InlineData(new double[] { double.NaN, 0, 1 })]
    [InlineData(new double[] { 0, double.PositiveInfinity, 1 })]
    public void MissingOrInvalidAxisIsUnresolved(double[] axis) =>
        Assert.Equal(BoltGroupViewClass.Unresolved, BoltGroupViewClassifier.Classify(axis, 1));

    [Fact]
    public void NullAxisIsUnresolved() =>
        Assert.Equal(BoltGroupViewClass.Unresolved, BoltGroupViewClassifier.Classify(null, 1));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(45)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidToleranceIsRefused(double tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoltGroupViewClassifier.Classify([0, 0, 1], tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltChainSettings(tolerance));
    }

    private static double[] Tilted(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return [Math.Sin(radians), 0, Math.Cos(radians)];
    }
}
