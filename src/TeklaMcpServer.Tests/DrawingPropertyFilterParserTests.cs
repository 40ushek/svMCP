using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingPropertyFilterParserTests
{
    [Fact]
    public void Parse_DefaultsOperatorToEquals()
    {
        var filters = DrawingPropertyFilterParser.Parse("[{\"property\":\"status\",\"value\":\"UpToDate\"}]");

        var filter = Assert.Single(filters);
        Assert.Equal("status", filter.Property);
        Assert.Equal("equals", filter.Operator);
        Assert.Equal("UpToDate", filter.Value);
    }

    [Fact]
    public void Parse_ReadsOperatorAlias()
    {
        var filters = DrawingPropertyFilterParser.Parse("[{\"property\":\"type\",\"op\":\"contains\",\"value\":\"Part\"}]");

        var filter = Assert.Single(filters);
        Assert.Equal("type", filter.Property);
        Assert.Equal("contains", filter.Operator);
        Assert.Equal("Part", filter.Value);
    }
}
