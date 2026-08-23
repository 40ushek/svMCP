using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Pure parsing rules: no Tekla model is needed to test these. Kept separate from the bridge
/// dispatcher precisely so this has runnable coverage instead of only a source-text grep.
/// </summary>
public sealed class ModelIdListParserTests
{
    [Fact]
    public void ABlankArgumentMeansNoFilterWasNamed()
    {
        Assert.True(ModelIdListParser.TryParse("", out var modelIds, out var error));
        Assert.Null(modelIds);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(" , ")]
    [InlineData(",,,")]
    public void ContentThatIsOnlyDelimitersIsAMalformedFilterNotAnEmptyOne(string argument)
    {
        // The blank case above must widen to "search the whole view"; this one must not -
        // it is a caller trying to name ids and getting the syntax wrong.
        Assert.False(ModelIdListParser.TryParse(argument, out var modelIds, out var error));
        Assert.Null(modelIds);
        Assert.NotNull(error);
    }

    [Fact]
    public void RepeatedDelimitersBetweenValidTokensAreToleratedAndDeduplicated()
    {
        Assert.True(ModelIdListParser.TryParse("1,,2", out var modelIds, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { 1, 2 }, modelIds);
    }

    [Fact]
    public void ANonNumericTokenIsRefusedRatherThanSkipped()
    {
        Assert.False(ModelIdListParser.TryParse("one", out var modelIds, out var error));
        Assert.Null(modelIds);
        Assert.Contains("one", error);
    }

    [Fact]
    public void ANonNumericTokenAmongValidOnesStillFailsTheWholeParse()
    {
        // Skipping the bad token would silently narrow the caller's intended filter instead
        // of refusing it - the same wrong shortcut a blank fallback would take.
        Assert.False(ModelIdListParser.TryParse("1,two,3", out var modelIds, out var error));
        Assert.Null(modelIds);
        Assert.Contains("two", error);
    }
}
