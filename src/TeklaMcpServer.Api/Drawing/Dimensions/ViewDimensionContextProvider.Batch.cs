using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class CreateDimensionsBatchRequest
{
    public int ViewId { get; set; }
    public string ContextId { get; set; } = string.Empty;
    // Required (steel or panel) when an entry uses "preview": the same chain key exists in both
    // rule sets with different points, so a forgotten value must not silently resolve to steel.
    public string RuleSet { get; set; } = string.Empty;
    public string ChainView { get; set; } = "chain";
    public List<BatchDimensionChain> Chains { get; set; } = [];
}

public sealed class BatchDimensionChain
{
    // Accepted for older callers; batch placement assigns rows per side, ignoring this input.
    public int Row { get; set; } = 1;
    public string Key { get; set; } = string.Empty;
    public string? Preview { get; set; }
    private string[]? _pointIds;
    internal bool PointIdsSpecified { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? PointIds {
        get => _pointIds;
        set { PointIdsSpecified = true; _pointIds = value; }
    }
    public string Direction { get; set; } = string.Empty;
    public double? Distance { get; set; }
    public double? PaperGapMm { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AttributesFile { get; set; }
    public string? DimensionType { get; set; }
}

public sealed class BatchDimensionItemResult
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DimensionId { get; set; }
    public int? MergedIntoDimensionId { get; set; }
    public int? MatchingDimensionId { get; set; }
    public string? ActualDimensionType { get; set; }
    public string? RenderedLineStatus { get; set; }
    public string? Error { get; set; }
}

public sealed class BatchDimensionReadback
{
    public int Id { get; set; }
    public string TeklaDimensionType { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public double? LineAt { get; set; }
    public double? From { get; set; }
    public double? To { get; set; }
    public double[] Segments { get; set; } = [];
}

public sealed class CreateDimensionsBatchResult
{
    public int ViewId { get; set; }
    public List<BatchDimensionItemResult> Chains { get; set; } = [];
    public List<BatchDimensionReadback> FinalDimensions { get; set; } = [];
    public string? FinalReadError { get; set; }
}

public sealed partial class ViewDimensionContextProvider
{
    public CreateDimensionsBatchResult CreateBatch(CreateDimensionsBatchRequest batch)
    {
        if (batch.ViewId <= 0 || string.IsNullOrWhiteSpace(batch.ContextId))
            throw new ArgumentException("viewId and contextId are required");
        if (batch.Chains is not { Count: > 0 and <= 32 })
            throw new ArgumentException("chains must contain 1 to 32 entries");
        var chains = ResolveBatchChains(batch);
        if (chains.Any(chain => string.IsNullOrWhiteSpace(chain.Key)) ||
            chains.Select(chain => chain.Key).Distinct(StringComparer.Ordinal).Count() != chains.Length)
            throw new ArgumentException("each chain needs a unique non-empty key");

        // Resolve every point and placement before the first drawing mutation.
        var prepared = chains.Select(chain => PrepareBatchChain(batch, chain, 1)).ToArray();
        var before = _readDimensions(batch.ViewId);
        var rows = new Dictionary<string, int>();
        for (var i = 0; i < prepared.Length; i++)
        {
            var entry = prepared[i];
            var side = Side(entry.Chain.Direction);
            rows.TryGetValue(side, out var row);
            rows[side] = ++row;
            if (entry.Chain.Distance.HasValue || entry.Chain.PaperGapMm.HasValue) continue;

            var existing = FindMatching(before, entry.Write, contained: false,
                ignoreDistance: true, requireUnique: true);
            if (existing != null)
            {
                entry.Write = new PreparedDimensionWrite(entry.Write.Request, existing.Value.Item.Distance, null);
                continue;
            }
            if (before.Groups.Any(group => GroupSide(group) == side && group.Items.Count > 0))
                throw new ArgumentException($"Chain '{entry.Chain.Key}': new chains on occupied {side} require paperGapMm or distance");
            prepared[i] = PrepareBatchChain(batch, entry.Chain, row);
        }
        var result = new CreateDimensionsBatchResult { ViewId = batch.ViewId };
        var stopped = false;

        foreach (var entry in prepared)
        {
            var item = new BatchDimensionItemResult { Key = entry.Chain.Key };
            result.Chains.Add(item);
            if (stopped)
            {
                item.Status = "skipped";
                item.Error = "An earlier chain failed; inspect the drawing before retrying";
                continue;
            }

            var existing = FindMatching(before, entry.Write, contained: false);
            if (existing != null)
            {
                item.Status = "retained";
                item.DimensionId = existing.Value.Item.Id;
                continue;
            }

            try
            {
                var write = _write(entry.Write.Request, entry.Write.Distance);
                item.DimensionId = write.DimensionId > 0 ? write.DimensionId : null;
                item.RenderedLineStatus = write.WriteState?.RenderedLine?.Status;
                if (!write.Created)
                {
                    item.Status = "failed";
                    item.Error = write.Error ?? "Dimension creation did not complete";
                    stopped = true;
                }
                else item.Status = "pendingReadback";
            }
            catch (Exception ex)
            {
                item.Status = "failed";
                item.Error = ex.Message;
                stopped = true;
            }
        }

        try
        {
            var after = _readDimensions(batch.ViewId);
            result.FinalDimensions = ProjectReadback(after);
            var finalItems = after.Groups.SelectMany(group => group.Items).ToList();
            for (var i = 0; i < prepared.Length; i++)
            {
                var item = result.Chains[i];
                if (item.Status is not ("pendingReadback" or "retained")) continue;
                var survivingItem = item.DimensionId.HasValue
                    ? finalItems.FirstOrDefault(final => final.Id == item.DimensionId.Value) : null;
                if (survivingItem != null)
                {
                    item.ActualDimensionType = survivingItem.TeklaDimensionType;
                    var requestedType = DimensionCreatePlacementHelper
                        .ParseDimensionType(prepared[i].Write.Request.DimensionType)?.ToString();
                    if (requestedType != null && !string.Equals(requestedType,
                        survivingItem.TeklaDimensionType, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = "uncertain";
                        item.Error = "Dimension remains, but its Tekla dimension type differs from the requested type";
                    }
                    else if (item.Status == "pendingReadback") item.Status = "created";
                    continue;
                }
                var merged = FindMatching(after, prepared[i].Write, contained: true);
                if (merged != null)
                {
                    item.Status = "merged";
                    item.MergedIntoDimensionId = merged.Value.Item.Id;
                    item.ActualDimensionType = merged.Value.Item.TeklaDimensionType;
                    item.Error = "No separate dimension remains after Tekla merged this chain";
                }
                else
                {
                    var otherType = FindMatching(after, prepared[i].Write, contained: true,
                        requireRequestedType: false);
                    if (otherType != null)
                    {
                        item.Status = "uncertain";
                        item.MatchingDimensionId = otherType.Value.Item.Id;
                        item.ActualDimensionType = otherType.Value.Item.TeklaDimensionType;
                        item.Error = "No separate dimension remains; matching geometry has a different " +
                            "Tekla dimension type. Inspect the view to confirm whether Tekla merged this chain";
                    }
                    else
                    {
                        item.Status = Axis(prepared[i].Write.Request.Direction) == null
                            ? "uncertain" : "failed";
                        item.Error = item.Status == "uncertain"
                            ? "Inclined dimension has no separate ID in the final read-back; inspect the view before retrying"
                            : "Dimension is absent from the final drawing read-back";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.FinalReadError = ex.Message;
            foreach (var item in result.Chains.Where(item => item.Status == "pendingReadback"))
            {
                item.Status = "uncertain";
                item.Error = "Final drawing read-back failed; inspect the view before retrying";
            }
        }
        return result;
    }

    private BatchDimensionChain[] ResolveBatchChains(CreateDimensionsBatchRequest batch)
    {
        var references = batch.Chains.Where(chain => chain?.Preview != null).ToArray();
        if (references.Length > 0 && string.IsNullOrWhiteSpace(batch.RuleSet))
            throw new ArgumentException("ruleSet (steel or panel) is required when an entry uses preview; pass the one used to read the preview");
        var ruleSet = TeklaMcpServer.Shared.DimensionPreviewQuestions.NormalizeRuleSet(
            string.IsNullOrWhiteSpace(batch.RuleSet) ? "steel" : batch.RuleSet);
        var chainView = TeklaMcpServer.Shared.DimensionPreviewQuestions.NormalizeChainView(batch.ChainView);
        if (batch.Chains.Any(chain => chain == null))
            throw new ArgumentException("chains cannot contain null entries");
        if (references.Any(chain => string.IsNullOrWhiteSpace(chain.Preview) || chain.PointIdsSpecified ||
                !string.IsNullOrEmpty(chain.Direction)))
            throw new ArgumentException("A preview reference cannot include pointIds or direction and needs a non-empty preview key");
        if (references.Select(chain => chain.Preview).Distinct(StringComparer.Ordinal).Count() != references.Length)
            throw new ArgumentException("Each preview chain may only be referenced once");
        if (references.Length == 0) return batch.Chains.ToArray();

        var context = RequireContext(batch.ViewId, batch.ContextId);
        var answer = context.Query(chainView, "all", ruleSet: ruleSet);
        if (!answer.TryGetProperty("chainPreview", out var rows))
            throw new ArgumentException("Cannot resolve preview chains from this context");
        var preview = rows.EnumerateArray().SelectMany(row => row.GetProperty("chains").EnumerateArray())
            .ToDictionary(chain => chain.GetProperty("key").GetString()!, StringComparer.Ordinal);
        return batch.Chains.Select(chain => {
            if (chain.Preview == null) return chain;
            if (!preview.TryGetValue(chain.Preview, out var selected))
                throw new ArgumentException($"Unknown preview chain '{chain.Preview}' for ruleSet={ruleSet}, chainView={chainView}");
            var ids = selected.GetProperty("pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
            if (ids.Length < 2)
                throw new ArgumentException($"Preview chain '{chain.Preview}' needs at least two pointIds" +
                    (selected.TryGetProperty("incompleteReason", out var reason) ? ": " + reason.GetString() : ""));
            return new BatchDimensionChain {
                Key = string.IsNullOrWhiteSpace(chain.Key) ? chain.Preview : chain.Key,
                PointIds = ids, Direction = selected.GetProperty("direction").GetString()!,
                AttributesFile = chain.AttributesFile ?? selected.GetProperty("attributesFile").GetString()!,
                Distance = chain.Distance, PaperGapMm = chain.PaperGapMm, DimensionType = chain.DimensionType
            };
        }).ToArray();
    }

    private PreparedBatchChain PrepareBatchChain(CreateDimensionsBatchRequest batch, BatchDimensionChain chain, int row)
    {
        if (chain.PointIds is not { Length: >= 2 } || chain.PointIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Chain '{chain.Key}' needs at least two pointIds");
        if (string.IsNullOrWhiteSpace(chain.Direction))
            throw new ArgumentException($"Chain '{chain.Key}' needs a direction");
        if (chain.Distance.HasValue && chain.PaperGapMm.HasValue)
            throw new ArgumentException($"Chain '{chain.Key}' must use distance or paperGapMm, not both");
        if (chain.PaperGapMm.HasValue &&
            (!DimensionWriteProtocol.Finite(chain.PaperGapMm.Value) || chain.PaperGapMm.Value < 0))
            throw new ArgumentException($"Chain '{chain.Key}' has an invalid paperGapMm");

        DimensionCreatePlacementHelper.ResolveDirection(chain.Direction);
        _ = DimensionCreatePlacementHelper.ParseDimensionType(chain.DimensionType);
        _validateAttributes(chain.AttributesFile ?? "standard");
        var request = new CreateDimensionRequest {
            ViewId = batch.ViewId, ContextId = batch.ContextId,
            PointIds = chain.PointIds, Direction = chain.Direction,
            Distance = chain.Distance, PaperGapMm = chain.PaperGapMm,
            AttributesFile = chain.AttributesFile ?? "standard", DimensionType = chain.DimensionType, Row = row
        };
        var write = Prepare(request);
        var error = DimensionWriteProtocol.Validate(request.Points, write.Distance);
        if (error != null) throw new ArgumentException($"Chain '{chain.Key}': {error}");
        return new PreparedBatchChain(chain, write);
    }

    private sealed class PreparedBatchChain
    {
        public BatchDimensionChain Chain { get; }
        public PreparedDimensionWrite Write { get; set; }

        public PreparedBatchChain(BatchDimensionChain chain, PreparedDimensionWrite write)
        {
            Chain = chain;
            Write = write;
        }
    }

    private static (DimensionGroupInfo Group, DimensionItemInfo Item)? FindMatching(
        GetDimensionsResult snapshot, PreparedDimensionWrite write, bool contained,
        bool requireRequestedType = true, bool ignoreDistance = false, bool requireUnique = false)
    {
        (DimensionGroupInfo Group, DimensionItemInfo Item)? match = null;
        var axis = Axis(write.Request.Direction);
        if (axis == null) return null;
        var requestedTeklaType = DimensionCreatePlacementHelper.ParseDimensionType(write.Request.DimensionType)?.ToString();
        foreach (var group in snapshot.Groups)
        {
            if (!string.Equals(group.DimensionType, axis, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var item in group.Items)
            {
                if (!SamePoints(write.Request.Points, item.PointList, contained)) continue;
                if (!SameSide(write, item, axis)) continue;
                if (requireRequestedType && requestedTeklaType != null && !string.Equals(item.TeklaDimensionType,
                    requestedTeklaType, StringComparison.OrdinalIgnoreCase)) continue;
                if (!contained && !ignoreDistance && Math.Abs(item.Distance - write.Distance) > 1) continue;
                if (requireUnique && match != null)
                    throw new ArgumentException("Several existing dimensions match this chain; specify paperGapMm or distance");
                match = (group, item);
                if (!requireUnique) return match;
            }
        }
        return match;
    }

    private static string Side(string direction)
    {
        var vector = DimensionCreatePlacementHelper.ResolveDirection(direction);
        return Axis(direction) == "Horizontal" ? (vector.Y > 0 ? "Top" : "Bottom")
            : Axis(direction) == "Vertical" ? (vector.X > 0 ? "Right" : "Left") : "Unknown";
    }

    private static string GroupSide(DimensionGroupInfo group) =>
        group.DimensionType == "Horizontal" ? (group.TopDirection >= 0 ? "Top" : "Bottom")
        : group.DimensionType == "Vertical" ? (group.TopDirection >= 0 ? "Left" : "Right") : "Unknown";

    private static string? Axis(string direction)
    {
        var vector = DimensionCreatePlacementHelper.ResolveDirection(direction);
        if (Math.Abs(vector.X) < 1e-9 && Math.Abs(vector.Y) > 1e-9) return "Horizontal";
        if (Math.Abs(vector.Y) < 1e-9 && Math.Abs(vector.X) > 1e-9) return "Vertical";
        return null;
    }

    private static bool SamePoints(double[] requested, IReadOnlyList<DrawingPointInfo> actual, bool contained)
    {
        var count = requested.Length / 3;
        if (actual.Count < count || (!contained && actual.Count != count)) return false;
        var used = new bool[actual.Count];
        for (var i = 0; i < requested.Length; i += 3)
        {
            var match = -1;
            for (var j = 0; j < actual.Count; j++)
                if (!used[j] && Math.Abs(requested[i] - actual[j].X) <= 0.5 &&
                    Math.Abs(requested[i + 1] - actual[j].Y) <= 0.5) { match = j; break; }
            if (match < 0) return false;
            used[match] = true;
        }
        return true;
    }

    private static bool SameSide(PreparedDimensionWrite write, DimensionItemInfo item, string axis)
    {
        if (item.ReferenceLine == null) return false;
        var vector = DimensionCreatePlacementHelper.ResolveDirection(write.Request.Direction);
        var line = axis == "Horizontal" ? item.ReferenceLine.StartY : item.ReferenceLine.StartX;
        var source = Enumerable.Range(0, write.Request.Points.Length / 3)
            .Average(i => write.Request.Points[3 * i + (axis == "Horizontal" ? 1 : 0)]);
        var sign = axis == "Horizontal" ? Math.Sign(vector.Y) : Math.Sign(vector.X);
        return sign * (line - source) > 0;
    }

    private static List<BatchDimensionReadback> ProjectReadback(GetDimensionsResult snapshot)
    {
        return snapshot.Groups.SelectMany(group => group.Items.Select(item => {
            var horizontal = string.Equals(group.DimensionType, "Horizontal", StringComparison.OrdinalIgnoreCase);
            var vertical = string.Equals(group.DimensionType, "Vertical", StringComparison.OrdinalIgnoreCase);
            var coordinates = item.PointList.Select(point => horizontal ? point.X : point.Y).ToArray();
            var side = horizontal ? (group.TopDirection >= 0 ? "Top" : "Bottom")
                : vertical ? (group.TopDirection >= 0 ? "Left" : "Right") : "Unknown";
            return new BatchDimensionReadback {
                Id = item.Id, TeklaDimensionType = item.TeklaDimensionType, Side = side,
                LineAt = item.ReferenceLine == null ? null
                    : horizontal ? item.ReferenceLine.StartY : vertical ? item.ReferenceLine.StartX : null,
                From = coordinates.Length == 0 || (!horizontal && !vertical) ? null : coordinates.Min(),
                To = coordinates.Length == 0 || (!horizontal && !vertical) ? null : coordinates.Max(),
                Segments = item.LengthList.ToArray()
            };
        })).ToList();
    }
}
