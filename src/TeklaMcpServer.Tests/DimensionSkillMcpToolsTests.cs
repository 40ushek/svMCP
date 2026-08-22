using System.IO;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The dimensioning skill names these bridge commands as its normal read/debug loop. These
/// source checks keep the public MCP surface from drifting back to a subset of that loop.
/// </summary>
public sealed class DimensionSkillMcpToolsTests
{
    private static string GeometryToolsSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaMcpServer", "Tools", "Drawing", "ModelTools.Drawing.Geometry.cs"));

    private static string DimensionsToolsSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaMcpServer", "Tools", "Drawing", "ModelTools.Drawing.Dimensions.cs"));

    [Fact]
    public void StructuralChainPositionsIsPublishedAsAnMcpTool()
    {
        var text = GeometryToolsSource();

        Assert.Contains("public static string GetStructuralChainPositions(", text);
        Assert.Contains("\"get_structural_chain_positions\"", text);
    }

    [Fact]
    public void StructuralReadsPassTheirOptionalExclusionFilterToTheBridge()
    {
        // Nothing is excluded unless the caller says so: mark prefixes and material names
        // are each plant's own convention, in its own language, and none of them belong in
        // this code. A tool that dropped the arguments would silently measure over
        // insulation again.
        var text = GeometryToolsSource();

        Assert.Contains("string excludePrefixes = \"\"", text);
        Assert.Contains("string excludeMaterials = \"\"", text);
        Assert.Contains("excludePrefixes ?? string.Empty", text);
        Assert.Contains("excludeMaterials ?? string.Empty", text);
    }

    [Fact]
    public void StructuralChainOverlayIsPublishedAsAnMcpTool()
    {
        var text = GeometryToolsSource();

        Assert.Contains("public static string DrawStructuralChainPositions(", text);
        Assert.Contains("\"draw_structural_chain_positions\"", text);
    }

    [Theory]
    [InlineData("HandleGetStructuralOutline")]
    [InlineData("HandleGetStructuralChainPositions")]
    [InlineData("HandleDrawStructuralChainPositions")]
    public void EveryCommandThatAppliesAnExclusionFilterAlsoReportsIt(string handler)
    {
        // The filter is how a short chain is explained: a caller comparing an overlay
        // against a rule set has no other way to see which parts were taken out. draw_*
        // applied one silently until this was caught in review.
        //
        // Scoped to the handler's own body rather than counted across the file, so that a
        // comment or a fourth command cannot make it pass or fail by accident. It still
        // reads source: the command runs only against a live Tekla view, so no test here
        // can call it and check the JSON it wrote.
        var body = HandlerBody(handler);

        Assert.Contains("ReadExclusions(args", body);
        Assert.Contains("exclusions = exclusions", body);
    }

    [Fact]
    public void BothExitsOfTheOverlayCommandReportTheFilter()
    {
        // The error exit is the one that was missing, and it is the exit a caller reaches
        // when the geometry could not be calculated - exactly when the exclusions are the
        // likeliest cause.
        var body = HandlerBody("HandleDrawStructuralChainPositions");

        Assert.Equal(2, CountOf(body, "exclusions = exclusions"));
    }

    [Fact]
    public void ThePartsToolReportsThePrefixAnExclusionIsWrittenFrom()
    {
        // The skill picks its rule set from get_drawing_parts. Without the prefix the
        // reader would have to split it out of PART_POS, which is guessing at a mark
        // format nobody promised. DrawingPartInfoBuilderTests holds the read itself -
        // that it is fetched before it is read, and that unreadable is not empty.
        Assert.Contains("PART_PREFIX", File.ReadAllText(Path.Combine(
            BridgeTestHelpers.FindRepoRoot(), "src", "TeklaMcpServer.Api", "Drawing", "Parts", "DrawingPartInfoBuilder.cs")));
        Assert.Contains("partPrefix = p.PartPrefix", HandlerSource());
        Assert.Contains("partPrefixKnown = p.PartPrefixKnown", HandlerSource());
    }

    [Fact]
    public void TheMainPartAnswerSeparatesNoFromNotKnown()
    {
        // Both arrive as isMainPart=false. The steel rule set measures from the main part,
        // so it has to refuse a drawing whose assembly could not be asked rather than
        // measure from what is left.
        var body = HandlerBody("HandleGetStructuralChainPositions");

        Assert.Contains("mainPartModelIds", body);
        Assert.Contains("mainPartUnresolvedModelIds", body);
        Assert.Contains("!part.IsMainPartKnown", body);
    }

    private static string HandlerSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.Geometry.cs"));

    /// <summary>
    /// One handler method's body, from its signature to the next one. Crude, and enough:
    /// it keeps an assertion about one command from being satisfied by another command's
    /// code somewhere else in the file.
    /// </summary>
    private static string HandlerBody(string handler)
    {
        var text = HandlerSource();
        var start = text.IndexOf("private bool " + handler + "(", System.StringComparison.Ordinal);
        Assert.True(start >= 0, handler + " is not in the geometry command handler");

        var next = text.IndexOf("    private ", start + 1, System.StringComparison.Ordinal);
        return next < 0 ? text.Substring(start) : text.Substring(start, next - start);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, System.StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, System.StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void ContactCandidatesPassTheirOptionalModelIdFilterToTheBridge()
    {
        var text = GeometryToolsSource();

        Assert.Contains("string modelIds = \"\"", text);
        Assert.Contains("string.IsNullOrWhiteSpace(modelIds)", text);
        Assert.Contains("draw ? \"true\" : \"false\", modelIds", text);
    }

    [Fact]
    public void DimensionChainCoverageIsPublishedAsAnMcpTool()
    {
        var text = DimensionsToolsSource();

        Assert.Contains("public static string GetDimensionChainCoverage(", text);
        Assert.Contains("RunBridge(\n            \"get_dimension_chain_coverage\"", text);
    }
}
