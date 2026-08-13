using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>The state of a group's working chain set.</summary>
public enum DimensionChainSetStage
{
    /// <summary>Only <see cref="CalcDimensionChains"/> has contributed positions.</summary>
    Calculated,

    /// <summary>A policy or skill has made explicit keep/remove decisions.</summary>
    PolicyApplied
}

/// <summary>What a policy has decided about one calculated chain position.</summary>
public enum DimensionChainPositionDisposition
{
    Calculated,
    Kept,
    Removed
}

/// <summary>Why a position exists in the calculated set.</summary>
public enum DimensionChainPositionSupportKind
{
    GroupExtent,
    AxisAlignedEdge,
    TiltedEdgeCorner,
    SegmentEndpoint,
    PointShape
}

/// <summary>The four initial X/Y placement sides. Skew chains will use their own vectors.</summary>
public enum DimensionChainSide
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>All working chains of one <see cref="GeometryGroup"/>.</summary>
public sealed class DimensionChainSet
{
    public DimensionChainSet(IReadOnlyList<DimensionChain> chains)
    {
        Chains = chains ?? throw new ArgumentNullException(nameof(chains));
    }

    public IReadOnlyList<DimensionChain> Chains { get; }

    public DimensionChainSetStage Stage { get; private set; } = DimensionChainSetStage.Calculated;

    /// <summary>
    /// Marks that a policy or skill has reviewed every working position.
    ///
    /// A stage is not proof of a decision if positions remain <see cref="DimensionChainPositionDisposition.Calculated"/>.
    /// </summary>
    public void MarkPolicyApplied()
    {
        if (Chains.SelectMany(chain => chain.Positions)
            .Any(position => position.Disposition == DimensionChainPositionDisposition.Calculated))
        {
            throw new InvalidOperationException(
                "Every calculated chain position must be kept or removed before the policy stage is complete.");
        }

        Stage = DimensionChainSetStage.PolicyApplied;
    }

    public DimensionChain this[DimensionChainSide side] =>
        Chains.Single(chain => chain.Side == side);
}

/// <summary>
/// One potential dimension chain. Direction is along its dimension line; placement normal
/// points from the group towards the side where the line belongs.
/// </summary>
public sealed class DimensionChain
{
    internal DimensionChain(DimensionChainSide side, Vec3 direction, Vec3 placementNormal)
    {
        Side = side;
        Direction = direction;
        PlacementNormal = placementNormal;
    }

    public DimensionChainSide Side { get; }
    public Vec3 Direction { get; }
    public Vec3 PlacementNormal { get; }

    /// <summary>Ordered positions, including positions a later policy may remove.</summary>
    public IReadOnlyList<DimensionChainPosition> Positions => _positions;

    private readonly List<DimensionChainPosition> _positions = [];

    internal void Add(double coordinate, DimensionChainPositionSupport support)
    {
        _positions.Add(new DimensionChainPosition(coordinate, support));
    }

    /// <summary>Orders candidates once, then coalesces neighbouring equal coordinates.</summary>
    internal void FinalizePositions(double coincidenceTolerance)
    {
        _positions.Sort(static (first, second) => first.Coordinate.CompareTo(second.Coordinate));

        if (_positions.Count < 2)
            return;

        var kept = new List<DimensionChainPosition>(_positions.Count) { _positions[0] };
        for (var index = 1; index < _positions.Count; index++)
        {
            var candidate = _positions[index];
            var previous = kept[kept.Count - 1];
            if (Math.Abs(previous.Coordinate - candidate.Coordinate) <= coincidenceTolerance)
            {
                previous.AddSupports(candidate.Supports);
                continue;
            }

            kept.Add(candidate);
        }

        _positions.Clear();
        _positions.AddRange(kept);
    }
}

/// <summary>
/// One coordinate along a chain direction, not the two-dimensional point eventually passed
/// to Tekla. Several geometric supports may justify the same coordinate.
/// </summary>
public sealed class DimensionChainPosition
{
    internal DimensionChainPosition(double coordinate, DimensionChainPositionSupport support)
    {
        Coordinate = coordinate;
        AddSupport(support);
    }

    public double Coordinate { get; }
    public IReadOnlyList<DimensionChainPositionSupport> Supports => _supports;
    public DimensionChainPositionDisposition Disposition { get; private set; } = DimensionChainPositionDisposition.Calculated;
    public string? DispositionReason { get; private set; }

    private readonly List<DimensionChainPositionSupport> _supports = [];

    public void MarkKept(string reason) => SetDisposition(DimensionChainPositionDisposition.Kept, reason);

    public void MarkRemoved(string reason) => SetDisposition(DimensionChainPositionDisposition.Removed, reason);

    internal void AddSupport(DimensionChainPositionSupport support)
    {
        if (support == null)
            throw new ArgumentNullException(nameof(support));

        _supports.Add(support);
    }

    internal void AddSupports(IEnumerable<DimensionChainPositionSupport> supports)
    {
        foreach (var support in supports)
            AddSupport(support);
    }

    private void SetDisposition(DimensionChainPositionDisposition disposition, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A policy disposition needs a reason.", nameof(reason));

        Disposition = disposition;
        DispositionReason = reason;
    }
}

/// <summary>One factual support for a calculated coordinate.</summary>
public sealed class DimensionChainPositionSupport
{
    internal DimensionChainPositionSupport(
        GeometryGroupShape source,
        DimensionChainPositionSupportKind kind,
        Vec3 point,
        int pointIndex)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Kind = kind;
        Point = point;
        PointIndex = pointIndex;
    }

    public GeometryGroupShape Source { get; }
    public DimensionChainPositionSupportKind Kind { get; }

    /// <summary>The real point of the source shape that supports this coordinate.</summary>
    public Vec3 Point { get; }

    public int PointIndex { get; }
}
