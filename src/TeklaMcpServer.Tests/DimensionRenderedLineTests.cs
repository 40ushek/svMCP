using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionRenderedLineTests
{
    [Theory]
    [InlineData(0, 1, 120)]
    [InlineData(0, -1, -80)]
    public void HorizontalBaseIsLeftmostAndNotOutermost(double ux, double uy, double expected)
    {
        var line = DimensionProjectionHelper.TryCreateCommonReferenceLine([(0d, 20d), (100d, 80d)], (ux, uy), 100, out _);
        Assert.NotNull(line); Assert.Equal(expected, line.StartY);
        var reversed = DimensionProjectionHelper.TryCreateCommonReferenceLine([(100d, 80d), (0d, 20d)], (ux, uy), 100, out _);
        Assert.Equal(line.StartY, reversed!.StartY);
    }

    [Theory]
    [InlineData(1, 120)]
    [InlineData(-1, -80)]
    public void VerticalBaseIsLowest(double ux, double expected)
    {
        var line = DimensionProjectionHelper.TryCreateCommonReferenceLine([(20d, 0d), (80d, 100d)], (ux, 0), 100, out _);
        Assert.NotNull(line); Assert.Equal(expected, line.StartX);
    }

    [Fact]
    public void AmbiguousBaseAndDiagonalDoNotInventALine()
    {
        Assert.Null(DimensionProjectionHelper.TryCreateCommonReferenceLine([(0d, 0d), (0d, 20d)], (0, 1), 20, out _));
        Assert.Null(DimensionProjectionHelper.TryCreateCommonReferenceLine([(0d, 0d), (20d, 20d)], (.707, .707), 20, out _));
    }

    [Fact]
    public void PresentationLineIsIdentifiedWithoutKnowingExpectedOffset()
    {
        var lines = new[] { (0d, 55d, 100d, 55d), (0d, 0d, 0d, 60d), (50d, 50d, 55d, 50d) };
        Assert.Equal(55, DimensionRenderedLineVerification.IdentifyLine(lines, (0, 0), (100, 10), (0, 1)));
        Assert.Null(DimensionRenderedLineVerification.IdentifyLine(lines.Append((0d, 65d, 100d, 65d)), (0, 0), (100, 10), (0, 1)));
    }

    [Fact]
    public void MissingPresentationIsNotAMatchAndObservedMismatchIsNotHidden()
    {
        Assert.Equal("matched", DimensionRenderedLineVerification.Compare(100, [100d, 100.5]).Status);
        Assert.Equal("not verified", DimensionRenderedLineVerification.Compare(100, [100d, null]).Status);
        Assert.Equal("not verified", DimensionRenderedLineVerification.Compare(100, []).Status);
        Assert.Equal("mismatch", DimensionRenderedLineVerification.Compare(100, [102d, null]).Status);
    }

    [Fact]
    public void RenderedMismatchPreservesOriginalAndCleansReplacement()
    {
        var originalDeleted = false;
        var replacementDeleted = false;
        var result = DimensionWriteProtocol.Execute(() => 42, () => true,
            _ => DimensionRenderedLineVerification.Compare(100, [120d]).Reason,
            () => originalDeleted = true, () => true,
            _ => replacementDeleted = true, _ => replacementDeleted);
        Assert.False(originalDeleted); Assert.True(result.NewDimensionRemoved); Assert.False(result.Completed);
    }
}
