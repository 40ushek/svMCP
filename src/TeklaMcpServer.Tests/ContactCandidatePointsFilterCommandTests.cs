using System.IO;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The optional modelIds filter on `get_contact_candidate_points`, pinned as a source-text
/// contract for the same reason other command wiring is: a bridge command can silently stop
/// doing what it claims if the handler and the underlying API drift apart, and that would
/// reintroduce exactly the whole-view grep this filter exists to avoid.
/// </summary>
public sealed class ContactCandidatePointsFilterCommandTests
{
    private static string GeometryHandlerSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.Geometry.cs"));

    [Fact]
    public void TheFourthArgumentIsParsedAsModelIdsAndPassedToTheSearch()
    {
        var text = GeometryHandlerSource();

        Assert.Contains("if (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]))", text);
        Assert.Contains("!TryParseModelIds(args[3], out modelIds, out var idError)", text);
        Assert.Contains("modelIds: modelIds", text);
    }

    [Fact]
    public void TheResponseSaysWhetherItWasRestrictedAndWhatWasNotFound()
    {
        var text = GeometryHandlerSource();

        Assert.Contains("restricted = result.Restricted", text);
        Assert.Contains("notVisibleRequestedIds = result.NotVisibleRequestedIds", text);
        Assert.Contains("outsideDepthRequestedIds = result.OutsideDepthRequestedIds", text);
        Assert.Contains("unresolvedDepthRequestedIds = result.UnresolvedDepthRequestedIds", text);
    }

    [Fact]
    public void TheResponseSaysWhetherTheSelectionItselfWasComplete()
    {
        // Restricted/notVisibleRequestedIds alone are not enough - a caller has to be able
        // to gate "this pair does not touch" on more than "the call did not error". Read
        // from `result` (the candidate-point result), not `contacts` (the raw search) - a
        // consumer working from candidate points has no access to `contacts` at all, so if
        // the fields were only ever right on `contacts`, this response would be wrong.
        var text = GeometryHandlerSource();

        Assert.Contains("selectionComplete = result.SelectionComplete", text);
        Assert.Contains("requestedIds = result.RequestedIds", text);
    }

    [Fact]
    public void ASingleIdFilterIsRejectedBeforeAnyTeklaCall()
    {
        // One body can never form a pair, so the search would always come back empty
        // regardless of what that part actually touches - a query with no honest answer,
        // refused rather than answered with a silent false negative. Pinned at the bridge
        // handler; the API's own guard (RejectSingleIdFilter, distinct-id counting included)
        // has its own runnable coverage in ViewContactsResultTests.
        var text = GeometryHandlerSource();
        var start = text.IndexOf("private bool HandleGetContactCandidatePoints(", System.StringComparison.Ordinal);
        Assert.True(start >= 0);
        var readSourceIdentity = text.IndexOf("var identity = ReadSourceIdentity(viewId);", start, System.StringComparison.Ordinal);
        Assert.True(readSourceIdentity > start, "the guard must exist");

        var guard = text[start..readSourceIdentity];
        Assert.Contains("modelIds is { Count: < 2 }", guard);
        Assert.Contains("WriteError(", guard);
    }

    [Fact]
    public void EachShapeCarriesItsContactStateAlongsideItsKind()
    {
        // Skill rule 4b's removal condition names both FaceToFace and Touching. Kind alone
        // cannot distinguish a settled contact from a gap or an overlap, so an LLM checking
        // the condition needs contactState on the shape itself, not just contactKind.
        var text = GeometryHandlerSource();

        Assert.Contains("contactState = shape.State.ToString()", text);
    }
}
