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

        Assert.Contains("restricted = contacts.Restricted", text);
        Assert.Contains("notVisibleRequestedIds = contacts.NotVisibleRequestedIds", text);
    }

    [Fact]
    public void TheResponseSaysWhetherTheSelectionItselfWasComplete()
    {
        // Restricted/notVisibleRequestedIds alone are not enough - a caller has to be able
        // to gate "this pair does not touch" on more than "the call did not error".
        var text = GeometryHandlerSource();

        Assert.Contains("selectionComplete = contacts.SelectionComplete", text);
        Assert.Contains("requestedIds = contacts.RequestedIds", text);
    }

    [Fact]
    public void ASingleIdFilterIsRejectedBeforeAnyTeklaCall()
    {
        // One body can never form a pair, so the search would always come back empty
        // regardless of what that part actually touches - a query with no honest answer,
        // refused rather than answered with a silent false negative.
        var text = GeometryHandlerSource();
        var start = text.IndexOf("private bool HandleGetContactCandidatePoints(", System.StringComparison.Ordinal);
        Assert.True(start >= 0);
        var readSourceIdentity = text.IndexOf("var identity = ReadSourceIdentity(viewId);", start, System.StringComparison.Ordinal);
        Assert.True(readSourceIdentity > start, "the guard must exist");

        var guard = text[start..readSourceIdentity];
        Assert.Contains("modelIds is { Count: < 2 }", guard);
        Assert.Contains("WriteError(", guard);
    }
}
