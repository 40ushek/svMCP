using System.Collections.Generic;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class AssemblyOutlineCommandTests
{
    [Fact]
    public void TheBridgeAnswerCarriesBothDepthSelectionLists()
    {
        // No live Tekla bridge needed: AssemblyOutlineResponse.From builds the exact object
        // the bridge serializes, so this exercises the real JSON, not the handler's source
        // text - a rename or a different way of building the anonymous object would no
        // longer be invisible to this test.
        var result = new ViewAssemblyOutlineResult(
            viewId: 2152,
            new Clipper2Lib.PolyTreeD(),
            new Dictionary<int, Clipper2Lib.PolyTreeD>(),
            unread: [],
            error: null,
            restricted: true,
            visibleCount: 2,
            requestedIds: [10, 20, 30, 40],
            notVisibleRequestedIds: [],
            outsideDepthModelIds: [30],
            unresolvedDepthModelIds: [40]);

        var response = AssemblyOutlineResponse.From(result);
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("outsideDepthModelIds", out var outsideDepth));
        Assert.Equal(new[] { 30 }, ReadIntArray(outsideDepth));

        Assert.True(root.TryGetProperty("unresolvedDepthModelIds", out var unresolvedDepth));
        Assert.Equal(new[] { 40 }, ReadIntArray(unresolvedDepth));

        // requestedIds includes both 30 (outside depth) and 40 (unresolved depth) - each
        // condition on its own must fail selectionComplete, not just one masking the other.
        Assert.False(root.GetProperty("selectionComplete").GetBoolean());
    }

    private static int[] ReadIntArray(JsonElement array)
    {
        var values = new List<int>();
        foreach (var item in array.EnumerateArray())
            values.Add(item.GetInt32());
        return values.ToArray();
    }
}
