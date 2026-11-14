using System.Text.Json.Serialization;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Typed working chain shared by bolt carry and repeat selection. OutputTemplate is
/// opaque transport metadata: algorithms use the fields below, never its serialized properties.</summary>
[JsonConverter(typeof(DimensionPreviewChainConverter))]
internal sealed class DimensionPreviewChain(string key, string kind, string? direction, string attributesFile,
    string[] pointIds, double[] segments, bool incomplete, string? reason, object? outputTemplate = null)
{
    public string Key { get; } = key;
    public string Kind { get; } = kind;
    public string? Direction { get; } = direction;
    public string AttributesFile { get; } = attributesFile;
    public string[] PointIds { get; private set; } = pointIds;
    public double[] Segments { get; private set; } = segments;
    public bool Incomplete { get; private set; } = incomplete;
    public string? Reason { get; private set; } = reason;
    public int? PartId { get; private set; }
    public string? ChainKind { get; private set; }
    public BoltGroupViewClass? ViewClass { get; private set; }
    public DimensionChainSide? PlacementSide { get; private set; }
    public BoltChainSources Sources { get; private set; } = BoltChainSources.Empty;
    public double[][] Points { get; private set; } = [];
    public double Distance { get; private set; }
    public string? Carries { get; private set; }
    public string? RestoredBesides { get; private set; }
    public string? Repeat { get; private set; }
    internal object? OutputTemplate { get; } = outputTemplate;
    internal bool SelectionChanged { get; private set; }
    internal bool PointIdsChanged { get; private set; }

    internal static DimensionPreviewChain Bolt(BoltRowChain chain)
    {
        var candidate = chain.Decision.Candidate;
        var normal = candidate.OutwardNormal;
        var side = candidate.Axis == 0 ? (normal.Y > 0 ? DimensionChainSide.Top : DimensionChainSide.Bottom)
            : (normal.X > 0 ? DimensionChainSide.Right : DimensionChainSide.Left);
        return new DimensionPreviewChain(candidate.Key, BoltChainRule.Kind,
            DimensionPreviewProjection.Direction(normal.X, normal.Y), "standard", [], candidate.Segments.ToArray(),
            !chain.Create, chain.Reason, chain.Project()) {
            PartId = candidate.PartId, ChainKind = candidate.Kind, ViewClass = candidate.ViewClass,
            PlacementSide = side, Sources = candidate.Sources,
            Points = candidate.Points.Select(point => new[] { point.X, point.Y }).ToArray(), Distance = chain.Distance
        };
    }

    internal static DimensionPreviewChain Carried(DimensionPreviewChain location, int? partId, DimensionChainSide side,
        double[][] points, double[] segments, double distance, IEnumerable<BoltProjectedPosition> sources) =>
        new(location.Key + "-bolts", BoltChainRule.Kind, location.Direction, location.AttributesFile, [], segments, false, null) {
            PartId = partId, ChainKind = "location", ViewClass = BoltGroupViewClass.Plan, PlacementSide = side,
            Points = points, Distance = distance, Sources = BoltChainSources.Captured(sources), Carries = location.Key
        };

    internal DimensionPreviewChain Refused(string reason)
    {
        var copy = (DimensionPreviewChain)MemberwiseClone();
        copy.Incomplete = true;
        copy.Reason = reason;
        copy.SelectionChanged = true;
        return copy;
    }

    internal DimensionPreviewChain Repeated(DimensionChainRepeats.Repeat repeat)
    {
        var reason = repeat.Reason switch {
            "sameView" => $"repeats {repeat.Of} on this view",
            "overallOnSheet" => $"repeats dimension {repeat.DimensionId} on view {repeat.ViewId}",
            _ => $"mirrors dimension {repeat.DimensionId} on view {repeat.ViewId}"
        };
        var copy = Refused(reason);
        copy.Repeat = repeat.Reason;
        return copy;
    }

    internal DimensionPreviewChain Restore(DimensionPreviewChain location, string beside)
    {
        var copy = (DimensionPreviewChain)MemberwiseClone();
        copy.PointIds = location.PointIds;
        copy.Segments = location.Segments;
        copy.Incomplete = false;
        copy.Reason = null;
        copy.SelectionChanged = copy.PointIdsChanged = true;
        copy.RestoredBesides = beside;
        return copy;
    }
}

[JsonConverter(typeof(DimensionPreviewRowConverter))]
internal sealed class DimensionPreviewRow(string side, IEnumerable<DimensionPreviewChain> chains, string? readIncomplete = null)
{
    public string Side { get; } = side;
    public DimensionPreviewChain[] Chains { get; } = chains.ToArray();
    public string? ReadIncomplete { get; } = readIncomplete;
    public DimensionPreviewRow WithChains(IEnumerable<DimensionPreviewChain> chains) => new(Side, chains, ReadIncomplete);
}
