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
        Assert.Contains("RunBridge(\"get_structural_chain_positions\"", text);
    }

    [Fact]
    public void StructuralChainOverlayIsPublishedAsAnMcpTool()
    {
        var text = GeometryToolsSource();

        Assert.Contains("public static string DrawStructuralChainPositions(", text);
        Assert.Contains("\"draw_structural_chain_positions\"", text);
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
