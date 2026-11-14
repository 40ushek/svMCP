namespace TeklaMcpServer.Api.Drawing;

/// <summary>Shared calculation data. These models contain no preview serialization or Tekla access.</summary>
internal sealed class BoltGroupProjection(BoltGroupGeometry geometry, BoltGroupViewClass viewClass, int[] relatedPartIds,
    int[] partCandidates, string? reason, string? stepReason, BoltProjectedCenter[] points,
    BoltSpacingChain[] chains, BoltEdgeDistanceGeometry[] edgeChains)
{
    public BoltGroupGeometry Geometry { get; } = geometry;
    public BoltGroupViewClass ViewClass { get; } = viewClass;
    public int[] RelatedPartIds { get; } = relatedPartIds;
    public int[] PartCandidates { get; } = partCandidates;
    public string? Reason { get; } = reason;
    public string? StepReason { get; } = stepReason;
    public BoltProjectedCenter[] Points { get; } = points;
    public BoltSpacingChain[] Chains { get; } = chains;
    public BoltEdgeDistanceGeometry[] EdgeChains { get; } = edgeChains;
}

internal sealed class BoltProjectedCenter(int index, double[] point, string state, string reason, int? representativeIndex = null)
{
    public int Index { get; } = index;
    public double[] Point { get; } = point;
    public string State { get; } = state;
    public string Reason { get; } = reason;
    public int? RepresentativeIndex { get; } = representativeIndex;
    public bool Blocked => State == "Blocked";
}

internal sealed class BoltSpacingPoint(double[] point, int[] sourceIndices)
{
    public double[] Point { get; } = point;
    public int[] SourceIndices { get; } = sourceIndices;
}

internal sealed class BoltSpacingChain(string key, int axis, int[] blockedIndices, int startIndex,
    BoltSpacingPoint[] points, double[] segments)
{
    public string Key { get; } = key;
    public int Axis { get; } = axis;
    public int[] BlockedIndices { get; } = blockedIndices;
    public int StartIndex { get; } = startIndex;
    public bool Candidate => BlockedIndices.Length == 0;
    public string Reason => Candidate ? "ordered adjacent bolt centers; policy and visibility are not verified"
        : "restriction evidence does not admit every source; the chain is not shortened";
    public BoltSpacingPoint[] Points { get; } = points;
    public double[] Segments { get; } = segments;
}

internal sealed class BoltPartChainGeometry(string key, int partId, int axis, DimensionChainSide side, string? reason,
    BoltBlockedSource[] blockedSources, double[][] points, double[] segments, double[]? minEnd, double[]? maxEnd,
    string? minEndKind, string? maxEndKind, string? boltRefusal, BoltProjectedPosition[] projectedPositions)
{
    public string Key { get; } = key;
    public int PartId { get; } = partId;
    public int Axis { get; } = axis;
    public DimensionChainSide Side { get; } = side;
    public string? Reason { get; } = reason;
    public BoltBlockedSource[] BlockedSources { get; } = blockedSources;
    public double[][] Points { get; } = points;
    public double[] Segments { get; } = segments;
    public double[]? MinEnd { get; } = minEnd;
    public double[]? MaxEnd { get; } = maxEnd;
    public string? MinEndKind { get; } = minEndKind;
    public string? MaxEndKind { get; } = maxEndKind;
    public string? BoltRefusal { get; } = boltRefusal;
    public BoltProjectedPosition[] ProjectedPositions { get; } = projectedPositions;
}

internal sealed class BoltSourceReference(int groupId, int index)
{
    public int GroupId { get; } = groupId;
    public int Index { get; } = index;
}

internal sealed class BoltBlockedSource(int groupId, int index, string? reason)
{
    public int GroupId { get; } = groupId;
    public int Index { get; } = index;
    public string? Reason { get; } = reason;
}

internal sealed class BoltProjectedPosition(double[] point, BoltSourceReference[] sources)
{
    public double[] Point { get; } = point;
    public BoltSourceReference[] Sources { get; } = sources;
}

internal sealed class BoltEdgeDistanceGeometry(string key, int groupId, int partId, int axis, int sign,
    string? reason, int[] sourceIndices, double[] center, double[]? edgePoint, double? distance, int contourIndex)
{
    public string Key { get; } = key;
    public int GroupId { get; } = groupId;
    public int PartId { get; } = partId;
    public int Axis { get; } = axis;
    public int Sign { get; } = sign;
    public string? Reason { get; } = reason;
    public int[] SourceIndices { get; } = sourceIndices;
    public double[] Center { get; } = center;
    public double[]? EdgePoint { get; } = edgePoint;
    public double? Distance { get; } = distance;
    public int ContourIndex { get; } = contourIndex;
}

internal sealed class BoltHoleGeometry
{
    public int GroupId { get; }
    public int PartId { get; }
    public string? Standard { get; }
    public double BoltSize { get; }
    public double HoleDiameter { get; }
    public bool HoleOnly { get; }
    public bool SpecialHole { get; }
    public string? HoleType { get; }
    public double SlottedHoleX { get; }
    public double SlottedHoleY { get; }

    public BoltHoleGeometry(BoltGroupGeometry group, int partId)
    {
        var hole = group.HoleIn(partId);
        GroupId = group.ModelId;
        PartId = partId;
        Standard = group.BoltStandard;
        BoltSize = group.BoltSize;
        HoleDiameter = group.HoleDiameter;
        HoleOnly = group.HoleOnly;
        SpecialHole = hole.Special;
        HoleType = hole.HoleType;
        SlottedHoleX = hole.SlottedHoleX;
        SlottedHoleY = hole.SlottedHoleY;
    }
}
