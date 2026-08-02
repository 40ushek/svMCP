using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// "get_dimension_defects" has to be wired at three independent points — the outer command
/// group switch, the inner per-command switch, and the MCP tool that calls into the bridge — and
/// none of them fail to compile if one is forgotten; the command would just silently 404 through
/// the outer switch's default case, or the MCP tool would silently call a command nothing
/// handles. This project has lost a second dispatcher to exactly that shape of mistake before, so
/// the routing is pinned here as a source-text contract rather than trusted to code review alone.
/// </summary>
public sealed class DimensionDefectsCommandRoutingTests
{
    private const string Command = "get_dimension_defects";

    [Fact]
    public void OuterCommandGroupSwitchRoutesToDimensionCommands()
    {
        var path = Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.cs");
        var text = File.ReadAllText(path);

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return TryHandleDimensionCommands(command, args);", text);
    }

    [Fact]
    public void InnerDimensionSwitchHandlesTheCommand()
    {
        var path = Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.Dimensions.cs");
        var text = File.ReadAllText(path);

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return HandleGetDimensionDefects(args);", text);
        Assert.Contains("private bool HandleGetDimensionDefects(", text);
    }

    [Fact]
    public void McpToolCallsTheSameCommandName()
    {
        var path = Path.Combine(
            BridgeTestHelpers.FindRepoRoot(), "src", "TeklaMcpServer", "Tools", "Drawing", "ModelTools.Drawing.Dimensions.cs");
        var text = File.ReadAllText(path);

        Assert.Contains($"RunBridge(\"{Command}\"", text);
    }
}
