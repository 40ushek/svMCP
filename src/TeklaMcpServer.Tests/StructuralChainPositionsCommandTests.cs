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
        // isComplete and issues travel with the data, not instead of it. A caller that reads only
        // `sides` and never looks at `isComplete` would place dimensions on a hypothesis; the
        // fields being present on the success path is what makes that a choice rather than an
        // impossibility.
        var text = HandlerSource();

        Assert.Contains("isComplete = group.Completeness.IsComplete", text);
        Assert.Contains("issues = group.Completeness.Issues.Select(issue => new { id = issue.Id, reason = issue.Reason })", text);
        Assert.Contains("sides = group.DimensionChains!.Chains.Select(chain => new", text);
        Assert.Contains("extent = new { minX = extent.MinX", text);
    }

    [Fact]
    public void EverySupportNamesItsPartAndOnlyAProvenAxisAlignedExtent()
    {
        var text = HandlerSource();

        Assert.Contains("modelId = support.ModelId", text);
        Assert.Contains("partSpanMatchToleranceMm = CalcDimensionChains.PartSpanMatchToleranceMm", text);
        Assert.Contains("partExtentAlongChain = ExtentAlong(chain.Side, support.AxisAlignedModelExtent)", text);
        Assert.Contains("partExtentAlongChain = ExtentAlong(side, support.AxisAlignedModelExtent)", text);
    }

    [Fact]
    public void AFailedCalculationStaysDistinguishableFromAnIncompleteRead()
    {
        // Two different failures reach the same catch: the outline could not be read, or the chain
        // calculation threw on geometry it could read. Collapsing them into one `error` would tell
        // a caller to go and classify a part when the real fault was in the calculation, so the
        // source reason is preferred for `error` while the exception is kept alongside it.
        var text = HandlerSource();

        Assert.Contains("catch (InvalidOperationException exception)", text);
        Assert.Contains("issue.Id == \"structural-outline\"", text);
        Assert.Contains("error = sourceError ?? exception.Message", text);
        Assert.Contains("calculationError = exception.Message", text);
        Assert.Contains("success = false", text);
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
