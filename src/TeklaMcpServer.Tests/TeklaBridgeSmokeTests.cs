using System.Text.Json;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaBridgeSmokeTests
{
    private static readonly JsonSerializerOptions ProtocolJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEnumerable<object[]> LegacyCommandCases()
    {
        yield return new object[] { "check_connection", Array.Empty<string>(), "status" };
        yield return new object[] { "get_drawing_views", Array.Empty<string>(), "views" };
        yield return new object[] { "get_drawing_parts", Array.Empty<string>(), "parts" };
        yield return new object[] { "get_part_geometry_in_view", new[] { "1", "1" }, "success" };
        yield return new object[] { "get_drawing_marks", Array.Empty<string>(), "marks" };
        yield return new object[] { "create_dimension", new[] { "1", "[0,0,0,1000,0,0]", "horizontal", "50", "standard" }, "created" };
    }

    [Theory]
    [MemberData(nameof(LegacyCommandCases))]
    public void LegacyOneShotCommandsReturnValidJson(string command, string[] args, string successField)
    {
        using var document = BridgeTestHelpers.RunBridgeOneShotJson(command, args);
        BridgeTestHelpers.AssertJsonPayloadShape(document.RootElement, successField);
    }

    [Fact]
    public void LoopModeHandlesSequentialCommandsWithoutProtocolDesync()
    {
        using var loop = BridgeTestHelpers.StartLoopSession();

        var checkConnection = ParseLoopResponse(loop.Send("check_connection"));
        Assert.True(checkConnection.Ok);
        using (var payload = JsonDocument.Parse(checkConnection.Result!))
            BridgeTestHelpers.AssertJsonPayloadShape(payload.RootElement, "status");

        var drawingViews = ParseLoopResponse(loop.Send("get_drawing_views"));
        Assert.True(drawingViews.Ok);
        using (var payload = JsonDocument.Parse(drawingViews.Result!))
            BridgeTestHelpers.AssertJsonPayloadShape(payload.RootElement, "views");

        var drawingMarks = ParseLoopResponse(loop.Send("get_drawing_marks"));
        Assert.True(drawingMarks.Ok);
        using (var payload = JsonDocument.Parse(drawingMarks.Result!))
            BridgeTestHelpers.AssertJsonPayloadShape(payload.RootElement, "marks");
    }

    private static LoopResponse ParseLoopResponse(string json)
    {
        return JsonSerializer.Deserialize<LoopResponse>(json, ProtocolJsonOptions)
            ?? throw new InvalidOperationException("Failed to parse loop response JSON.");
    }

    private sealed class LoopResponse
    {
        public int Id { get; set; }
        public bool Ok { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
    }
}
