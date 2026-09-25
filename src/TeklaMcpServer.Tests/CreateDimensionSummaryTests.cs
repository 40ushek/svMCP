using System.Text.Json;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class CreateDimensionSummaryTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void CleanVerifiedWriteIsOneShortLine()
    {
        var text = ModelTools.SummarizeCreatedDimension(Json("""
            {"created":true,"distanceUsed":80,"placement":{"TargetLineCoordinate":-265},
             "writeState":{"Verified":true,"NewDimensionRemoved":false,"CleanupError":null,"ErrorDetail":null,
                           "RenderedLine":{"Status":"matched"}}}
            """), 6928, 6);
        Assert.Equal("Created dimension 6928, 6 points, line at -265 (distance 80), rendered line matched.", text);
    }

    [Fact]
    public void NotVerifiedRenderedLineKeepsItsReason()
    {
        var text = ModelTools.SummarizeCreatedDimension(Json("""
            {"created":true,"distanceUsed":80,
             "writeState":{"Verified":true,"RenderedLine":{"Status":"not verified","Reason":"View shortening is enabled"}}}
            """), 1, 2);
        Assert.Contains("not verified (View shortening is enabled)", text);
    }

    [Theory]
    [InlineData("""{"writeState":{"Verified":false}}""")]
    [InlineData("""{"writeState":{"Verified":true,"CleanupError":"x"}}""")]
    [InlineData("""{"writeState":{"Verified":true,"RenderedLine":{"Status":"mismatch"}}}""")]
    [InlineData("""{"writeState":{"Verified":true,"NewDimensionRemoved":true}}""")]
    [InlineData("""{}""")]
    public void AnythingUnusualReturnsNullSoTheWholeStateIsShown(string json) =>
        Assert.Null(ModelTools.SummarizeCreatedDimension(Json(json), 1, 2));
}

public sealed class CompactDimensionsTests
{
    [Fact]
    public void OneRowPerDimensionWithSideLineAndSegments()
    {
        var root = JsonDocument.Parse("""
            {"drawingDimensionCount":3,"groups":[{"dimensionType":"Horizontal","topDirection":-1,"items":[
              {"id":6851,"viewId":2340,"dimensionType":"Horizontal",
               "referenceLine":{"startX":-20,"startY":-250,"endX":5204.6,"endY":-250},
               "pointList":[{"x":5204.6,"y":-68,"order":0},{"x":0,"y":-76,"order":1},{"x":-20,"y":-170,"order":2}]},
              {"id":6923,"viewId":2340,"dimensionType":"Vertical","topDirection":1,
               "referenceLine":{"startX":-100,"startY":-170,"endX":-100,"endY":170},
               "pointList":[{"x":-20,"y":170,"order":0},{"x":0,"y":76,"order":1},{"x":-20,"y":-170,"order":2}]}]}]}
            """).RootElement;
        var result = JsonDocument.Parse(ModelTools.CompactDimensions(root)).RootElement;
        Assert.Equal(2, result.GetProperty("total").GetInt32());
        Assert.Equal(1, result.GetProperty("merged").GetInt32());
        var first = result.GetProperty("dimensions")[0];
        Assert.Equal("Bottom", first.GetProperty("side").GetString());
        Assert.Equal(-250, first.GetProperty("at").GetDouble());
        Assert.Equal(new[] { 20d, 5204.6 }, first.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()).ToArray());
        var second = result.GetProperty("dimensions")[1];
        Assert.Equal("Left", second.GetProperty("side").GetString());
        Assert.Equal(-100, second.GetProperty("at").GetDouble());
        Assert.DoesNotContain("\n", ModelTools.CompactDimensions(root));
    }
}
