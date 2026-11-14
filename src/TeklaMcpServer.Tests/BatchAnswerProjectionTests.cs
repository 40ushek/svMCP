using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BatchAnswerProjectionTests
{
    private static CreateDimensionsBatchResult Result() => new() {
        ViewId = 7,
        Chains = [
            new() { Key = "Bottom-location", Status = "created", DimensionId = 1, RenderedLineStatus = "not verified" },
            new() { Key = "Bottom-overall", Status = "created", DimensionId = 2, RenderedLineStatus = "matched", AttributesFileUsed = "standard" },
            new() { Key = "Right-location", Status = "merged", DimensionId = 3, MergedIntoDimensionId = 9, RenderedLineStatus = "mismatch" },
            new() { Key = "bolt-a", Status = "failed", Error = "write refused", BoltGeometryVerification = "provisional" },
            new() { Key = "bolt-b", Status = "skipped" }
        ],
        FinalDimensions = [new() { Id = 1, Side = "Bottom", Segments = [100] }],
        FinalReadError = "final read failed"
    };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void ShortAnswerKeepsEveryKeyStatusAndNonEmptyDiagnostic()
    {
        var answer = Json(BatchAnswerProjection.Short(Result()));
        var chains = answer.GetProperty("chains").EnumerateArray().ToArray();
        Assert.Equal(new[] { "created", "created", "merged", "failed", "skipped" }, chains.Select(chain => chain.GetProperty("status").GetString()));
        Assert.Equal(new[] { "key", "status", "dimensionId" }, chains[0].EnumerateObject().Select(field => field.Name));
        Assert.Equal("standard", chains[1].GetProperty("attributesFileUsed").GetString());
        Assert.False(chains[1].TryGetProperty("renderedLineStatus", out _));
        Assert.Equal(9, chains[2].GetProperty("mergedIntoDimensionId").GetInt32());
        Assert.Equal("mismatch", chains[2].GetProperty("renderedLineStatus").GetString());
        Assert.Equal("write refused", chains[3].GetProperty("error").GetString());
        Assert.Equal("provisional", chains[3].GetProperty("boltGeometryVerification").GetString());
        Assert.Equal(new[] { "key", "status" }, chains[4].EnumerateObject().Select(field => field.Name));
        Assert.Equal(1, answer.GetProperty("renderedLineNotVerified").GetInt32());
        Assert.Equal("final read failed", answer.GetProperty("finalReadError").GetString());
        Assert.False(answer.TryGetProperty("finalDimensions", out _));
    }

    [Fact]
    public void ShortAnswerOmitsAbsentFinalReadErrorAndFullAnswerKeepsEverything()
    {
        var result = Result();
        result.FinalReadError = null;
        Assert.False(Json(BatchAnswerProjection.Short(result)).TryGetProperty("finalReadError", out _));
        var full = Json(BatchAnswerProjection.Full(Result()));
        Assert.Single(full.GetProperty("finalDimensions").EnumerateArray());
        Assert.Equal("standard", full.GetProperty("chains")[1].GetProperty("attributesFileUsed").GetString());
        Assert.Equal("final read failed", full.GetProperty("finalReadError").GetString());
    }
}
