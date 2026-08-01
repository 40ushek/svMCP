using TeklaMcpServer.Api.Drawing;
using Tekla.Structures.Geometry3d;
using Xunit;

namespace TeklaMcpServer.Tests;

public class DimensionCommandPlacementHelperTests
{
    [Theory]
    [InlineData("horizontal", 0, 1, 0)]
    [InlineData("h", 0, 1, 0)]
    [InlineData("vertical", 1, 0, 0)]
    [InlineData("v", 1, 0, 0)]
    [InlineData("vertical-left", -1, 0, 0)]
    [InlineData("v-", -1, 0, 0)]
    [InlineData("horizontal-down", 0, -1, 0)]
    [InlineData("h-", 0, -1, 0)]
    [InlineData("0.6,-0.8,0", 0.6, -0.8, 0)]
    public void ResolveDirection_UsesExpectedDefaults(
        string direction,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        var actual = DimensionCreatePlacementHelper.ResolveDirection(direction);

        Assert.Equal(expectedX, actual.X, 6);
        Assert.Equal(expectedY, actual.Y, 6);
        Assert.Equal(expectedZ, actual.Z, 6);
    }

    [Fact]
    public void ResolveDirection_ParsesExplicitVector()
    {
        var actual = DimensionCreatePlacementHelper.ResolveDirection("1,2,3");

        Assert.Equal(1, actual.X, 6);
        Assert.Equal(2, actual.Y, 6);
        Assert.Equal(3, actual.Z, 6);
    }

    [Fact]
    public void NormalizeCreateAttributesFile_TrimsAndAllowsEmpty()
    {
        Assert.Null(DimensionCreatePlacementHelper.NormalizeAttributesFile(" "));
        Assert.Equal("custom", DimensionCreatePlacementHelper.NormalizeAttributesFile(" custom "));
    }

    [Fact]
    public void NormalizeDiagonalAttributesFile_UsesStandardFallback()
    {
        Assert.Equal("standard", DimensionDiagonalPlacementHelper.NormalizeAttributesFile(" "));
        Assert.Equal("custom", DimensionDiagonalPlacementHelper.NormalizeAttributesFile(" custom "));
    }

    [Theory]
    [InlineData(false, 0, 50, 50)]
    [InlineData(true, 0, 50, 50)]
    [InlineData(false, 1, 50, 50)]
    [InlineData(true, 1, 50, 100)]
    public void ResolveDiagonalDistance_KeepsExistingSecondDiagonalRule(
        bool diagonalsIntersect,
        int diagonalIndex,
        double requestedDistance,
        double expected)
    {
        var actual = DimensionDiagonalPlacementHelper.ResolveDistance(requestedDistance, diagonalIndex, diagonalsIntersect);

        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void BuildDiagonalOffsetDirection_PreservesWrapperBehavior()
    {
        var actual = TeklaDrawingDimensionsApi.BuildDiagonalOffsetDirection(
            new Point(0, 0, 0),
            new Point(10, 0, 0));

        Assert.Equal(0, actual.X, 6);
        Assert.Equal(1, actual.Y, 6);
        Assert.Equal(0, actual.Z, 6);
    }

    [Fact]
    public void NormalizeBottomToTop_SwapsDescendingPair()
    {
        var pair = DimensionDiagonalPlacementHelper.NormalizeBottomToTop((
            new Point(0, 10, 0),
            new Point(5, 0, 0)));

        Assert.Equal(0, pair.Start.Y, 6);
        Assert.Equal(10, pair.End.Y, 6);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1,2")]
    [InlineData("1;2;3")]
    public void ResolveDirection_RejectsUnknownInput(string? direction)
    {
        // Guessing horizontal used to hide typos, and a wrong axis silently rebuilds a vertical
        // chain as a horizontal one — loud failure is the only safe answer.
        Assert.Throws<System.ArgumentException>(() => DimensionCreatePlacementHelper.ResolveDirection(direction));
        Assert.Null(DimensionCreatePlacementHelper.TryResolveDirection(direction));
    }

    [Fact]
    public void TryParseVector_UsesInvariantDecimalSeparator()
    {
        // The value arrives as a command-line argument and must not depend on machine culture.
        var parsed = DimensionCreatePlacementHelper.TryParseVector("0.5,-0.5,0");

        Assert.NotNull(parsed);
        Assert.Equal(0.5, parsed!.X, 6);
        Assert.Equal(-0.5, parsed.Y, 6);
    }
}
