using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// `get_structural_chain_positions` is the coordinate source the dimensioning skill now names:
/// everything downstream picks positions out of its answer rather than rebuilding them from
/// solids. That makes two things worth pinning that a compiler cannot see.
///
/// The first is routing. The command is wired at two independent points — the outer command-group
/// switch and the inner geometry switch — and forgetting either still compiles: the call falls
/// through the outer default and the command simply does not exist at runtime.
///
/// The second is the answer's shape. The command runs only against a live Tekla view, so no test
/// here can call it; what can be held is that its two exits stay distinguishable. A caller has to
/// be able to tell "the geometry is incomplete" from "the calculation failed" from "here are the
/// positions", and an incomplete read must never arrive looking like a successful one — that is
/// the completeness contract the skill leans on when it refuses to place dimensions.
/// </summary>
public sealed class StructuralChainPositionsCommandTests
{
    private const string Command = "get_structural_chain_positions";

    private static string HandlerSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.Geometry.cs"));

    [Fact]
    public void OuterCommandGroupSwitchRoutesToGeometryCommands()
    {
        var path = Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.cs");
        var text = File.ReadAllText(path);

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return TryHandleGeometryCommands(command, args);", text);
    }

    [Fact]
    public void InnerGeometrySwitchHandlesTheCommand()
    {
        var text = HandlerSource();

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return HandleGetStructuralChainPositions(args);", text);
        Assert.Contains("private bool HandleGetStructuralChainPositions(", text);
    }

    [Fact]
    public void AMissingOrUnparsableViewIdIsRejectedBeforeTeklaIsTouched()
    {
        // Without the guard a missing argument indexes past the end of args, and a non-numeric one
        // reaches Tekla as view 0 — both surface as an exception rather than as an answer saying
        // what the caller got wrong.
        Assert.Contains("args.Length < 2 || !int.TryParse(args[1], out var viewId)", HandlerSource());
        Assert.Contains($"WriteError(\"{Command} requires viewId argument\")", HandlerSource());
    }

    [Fact]
    public void TheAnswerAlwaysCarriesCompletenessBesideItsPositions()
    {
        var response = ViewDimensionContextTests.Context(incomplete: true).ChainPositions();
        Assert.False(response.GetProperty("isComplete").GetBoolean());
        Assert.NotEmpty(response.GetProperty("issues").EnumerateArray());
        Assert.Equal(4, response.GetProperty("sides").GetArrayLength());
        Assert.Equal(260, response.GetProperty("extent").GetProperty("maxX").GetDouble());
    }

    [Fact]
    public void EverySupportNamesItsPartAndOnlyAProvenAxisAlignedExtent()
    {
        var response = ViewDimensionContextTests.Context().ChainPositions(true);
        foreach (var side in response.GetProperty("sides").EnumerateArray())
        foreach (var position in side.GetProperty("positions").EnumerateArray())
        foreach (var support in position.GetProperty("supports").EnumerateArray())
        {
            Assert.True(support.TryGetProperty("modelId", out _));
            Assert.True(support.TryGetProperty("partExtentAlongChain", out _));
        }
        Assert.True(response.GetProperty("partSpanMatchToleranceMm").GetDouble() > 0);
    }

    [Fact]
    public void AFailedCalculationStaysDistinguishableFromAnIncompleteRead()
    {
        var outline = new TeklaMcpServer.Api.Drawing.StructuralOutline(
            new TeklaMcpServer.Api.Drawing.ViewAssemblyOutlineResult(7, new Clipper2Lib.PolyTreeD(),
                new Dictionary<int, Clipper2Lib.PolyTreeD>(), [], error: "source unavailable"), [], [], []);
        var context = new TeklaMcpServer.Api.Drawing.ViewDimensionContext(7, 10, outline, [], new { }, new { });
        var response = context.ChainPositions();
        Assert.False(response.GetProperty("success").GetBoolean());
        Assert.False(response.GetProperty("isComplete").GetBoolean());
        Assert.Equal("source unavailable", response.GetProperty("error").GetString());
        Assert.Contains("no planar points", response.GetProperty("calculationError").GetString());
    }

    [Fact]
    public void ReadingPositionsNeverDrawsOnTheDrawing()
    {
        // The drawing sibling exists precisely so that studying a drawing does not edit it. If this
        // handler ever grew an overlay call, every capture taken with it would be a capture of a
        // drawing the capture itself had changed.
        var text = HandlerSource();
        var start = text.IndexOf("private bool HandleGetStructuralChainPositions(", StringComparison.Ordinal);
        var end = text.IndexOf("private bool HandleDrawStructuralChainPositions(", StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "both structural-chain handlers must exist, read before draw");

        var body = text[start..end];
        Assert.DoesNotContain("GetDebugOverlayApi()", body);
        Assert.DoesNotContain("DrawOverlay", body);
    }
}
