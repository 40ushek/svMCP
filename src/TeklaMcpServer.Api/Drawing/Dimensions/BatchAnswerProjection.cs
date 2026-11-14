using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The answers of a batch write: the full one for repair and debugging, the short one for a
/// placement run. Both come from the same verified result; the short one only drops empty
/// fields and the repeated list of final dimensions.
/// </summary>
public static class BatchAnswerProjection
{
    public static object Full(CreateDimensionsBatchResult result) => new {
        viewId = result.ViewId,
        chains = result.Chains.Select(item => new {
            key = item.Key, status = item.Status, dimensionId = item.DimensionId,
            mergedIntoDimensionId = item.MergedIntoDimensionId,
            renderedLineStatus = item.RenderedLineStatus,
            attributesFileUsed = item.AttributesFileUsed,
            boltGeometryVerification = item.BoltGeometryVerification,
            error = item.Error
        }),
        finalDimensions = result.FinalDimensions.Select(item => new {
            id = item.Id, side = item.Side, lineAt = item.LineAt,
            from = item.From, to = item.To, segments = item.Segments
        }),
        finalReadError = result.FinalReadError
    };

    /// <summary>
    /// One entry per selected key with its status. Kept only when set: the dimension id,
    /// the merge target, the error, a substituted attributes file, the bolt geometry
    /// verification and a rendered line that does not match. Rendered lines that could not
    /// be checked are counted once, and a failed final read is always reported.
    /// </summary>
    public static object Short(CreateDimensionsBatchResult result)
    {
        var answer = new Dictionary<string, object?> {
            ["viewId"] = result.ViewId,
            ["chains"] = result.Chains.Select(Item).ToArray()
        };
        var notVerified = result.Chains.Count(item => item.RenderedLineStatus == "not verified");
        if (notVerified > 0) answer["renderedLineNotVerified"] = notVerified;
        if (!string.IsNullOrEmpty(result.FinalReadError)) answer["finalReadError"] = result.FinalReadError;
        return answer;
    }

    private static Dictionary<string, object?> Item(BatchDimensionItemResult item)
    {
        var entry = new Dictionary<string, object?> { ["key"] = item.Key, ["status"] = item.Status };
        if (item.DimensionId.HasValue) entry["dimensionId"] = item.DimensionId;
        if (item.MergedIntoDimensionId.HasValue) entry["mergedIntoDimensionId"] = item.MergedIntoDimensionId;
        if (!string.IsNullOrEmpty(item.Error)) entry["error"] = item.Error;
        if (!string.IsNullOrEmpty(item.AttributesFileUsed)) entry["attributesFileUsed"] = item.AttributesFileUsed;
        if (!string.IsNullOrEmpty(item.BoltGeometryVerification)) entry["boltGeometryVerification"] = item.BoltGeometryVerification;
        if (item.RenderedLineStatus is { Length: > 0 } line && line != "matched" && line != "not verified")
            entry["renderedLineStatus"] = line;
        return entry;
    }
}
