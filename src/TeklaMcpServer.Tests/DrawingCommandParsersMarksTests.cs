using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingCommandParsersMarksTests
{
    [Fact]
    public void ParseMoveMarkRequest_ParsesValidArguments()
    {
        var result = DrawingCommandParsers.ParseMoveMarkRequest(["move_mark", "40264", "1255.5", "-128.4"]);

        Assert.True(result.IsValid);
        Assert.Equal(40264, result.Request.MarkId);
        Assert.Equal(1255.5, result.Request.InsertionX);
        Assert.Equal(-128.4, result.Request.InsertionY);
    }

    [Fact]
    public void ParseMoveMarkRequest_RejectsMissingArguments()
    {
        var result = DrawingCommandParsers.ParseMoveMarkRequest(["move_mark", "40264"]);

        Assert.False(result.IsValid);
        Assert.Contains("Usage: move_mark", result.Error);
    }
}
