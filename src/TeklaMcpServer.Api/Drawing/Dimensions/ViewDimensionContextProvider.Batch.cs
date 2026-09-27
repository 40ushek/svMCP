using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public sealed class CreateDimensionsBatchRequest
{
    public int ViewId { get; set; }
    public string ContextId { get; set; } = string.Empty;
    public List<BatchDimensionChain> Chains { get; set; } = [];
}

public sealed class BatchDimensionChain
{
    public string Key { get; set; } = string.Empty;
    public string[] PointIds { get; set; } = [];
    public string Direction { get; set; } = string.Empty;
    public double? Distance { get; set; }
    public double? PaperGapMm { get; set; }
    public string AttributesFile { get; set; } = "standard";
}

public sealed class BatchDimensionItemResult
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DimensionId { get; set; }
    public int? MergedIntoDimensionId { get; set; }
    public string? RenderedLineStatus { get; set; }
    public string? Error { get; set; }
}

public sealed class BatchDimensionReadback
{
    public int Id { get; set; }
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
        if (batch.Chains.Any(chain => string.IsNullOrWhiteSpace(chain.Key)) ||
            batch.Chains.Select(chain => chain.Key).Distinct(StringComparer.Ordinal).Count() != batch.Chains.Count)
            throw new ArgumentException("each chain needs a unique non-empty key");

        // Resolve every point and placement before the first drawing mutation.
        var prepared = batch.Chains.Select(chain => PrepareBatchChain(batch, chain)).ToArray();
        var before = _readDimensions(batch.ViewId);
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
            var remainingIds = after.Groups.SelectMany(group => group.Items)
                .Select(item => item.Id).ToHashSet();
            for (var i = 0; i < prepared.Length; i++)
            {
                var item = result.Chains[i];
                if (item.Status is not ("pendingReadback" or "retained")) continue;
                if (item.DimensionId.HasValue && remainingIds.Contains(item.DimensionId.Value))
                {
                    if (item.Status == "pendingReadback") item.Status = "created";
                    continue;
                }
                var merged = FindMatching(after, prepared[i].Write, contained: true);
                if (merged != null)
                {
                    item.Status = "merged";
                    item.MergedIntoDimensionId = merged.Value.Item.Id;
                    item.Error = "No separate dimension remains after Tekla merged this chain";
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

    private PreparedBatchChain PrepareBatchChain(CreateDimensionsBatchRequest batch, BatchDimensionChain chain)
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
        _validateAttributes(chain.AttributesFile);
        var request = new CreateDimensionRequest {
            ViewId = batch.ViewId, ContextId = batch.ContextId,
            PointIds = chain.PointIds, Direction = chain.Direction,
            Distance = chain.Distance, PaperGapMm = chain.PaperGapMm,
            AttributesFile = chain.AttributesFile
        };
        var write = Prepare(request);
        var error = DimensionWriteProtocol.Validate(request.Points, write.Distance);
        if (error != null) throw new ArgumentException($"Chain '{chain.Key}': {error}");
        return new PreparedBatchChain(chain, write);
    }

    private sealed class PreparedBatchChain
    {
        public BatchDimensionChain Chain { get; }
        public PreparedDimensionWrite Write { get; }

        public PreparedBatchChain(BatchDimensionChain chain, PreparedDimensionWrite write)
        {
            Chain = chain;
            Write = write;
        }
    }

    private static (DimensionGroupInfo Group, DimensionItemInfo Item)? FindMatching(
        GetDimensionsResult snapshot, PreparedDimensionWrite write, bool contained)
    {
        var axis = Axis(write.Request.Direction);
        if (axis == null) return null;
        foreach (var group in snapshot.Groups)
        {
            if (!string.Equals(group.DimensionType, axis, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var item in group.Items)
            {
                if (!SamePoints(write.Request.Points, item.PointList, contained)) continue;
                if (!SameSide(write, item, axis)) continue;
                if (!contained && Math.Abs(item.Distance - write.Distance) > 1) continue;
                return (group, item);
            }
        }
        return null;
    }

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
                Id = item.Id, Side = side,
                LineAt = item.ReferenceLine == null ? null
                    : horizontal ? item.ReferenceLine.StartY : vertical ? item.ReferenceLine.StartX : null,
                From = coordinates.Length == 0 || (!horizontal && !vertical) ? null : coordinates.Min(),
                To = coordinates.Length == 0 || (!horizontal && !vertical) ? null : coordinates.Max(),
                Segments = item.LengthList.ToArray()
            };
        })).ToList();
    }
}
