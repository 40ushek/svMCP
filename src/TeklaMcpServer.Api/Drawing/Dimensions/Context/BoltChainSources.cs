namespace TeklaMcpServer.Api.Drawing;

/// <summary>Closed source-data variants. Serialization belongs to BoltChainPreviewProjection.</summary>
internal abstract class BoltChainSources
{
    private BoltChainSources() { }
    public static BoltChainSources Empty { get; } = Points([]);
    public static BoltChainSources Captured(IEnumerable<BoltProjectedPosition> positions) => new CapturedParts(positions);
    public static BoltChainSources Points(IEnumerable<double[]> points) => new ChainPoints(points);
    public static BoltChainSources Indices(IEnumerable<int[]> indices) => new SpacingIndices(indices);

    public virtual IReadOnlyList<BoltProjectedPosition> CapturedPositions => Array.Empty<BoltProjectedPosition>();

    internal sealed class CapturedParts(IEnumerable<BoltProjectedPosition> positions) : BoltChainSources
    {
        public IReadOnlyList<BoltProjectedPosition> Positions { get; } = positions.ToArray();
        public override IReadOnlyList<BoltProjectedPosition> CapturedPositions => Positions;
    }

    internal sealed class ChainPoints(IEnumerable<double[]> points) : BoltChainSources
    {
        public IReadOnlyList<double[]> Positions { get; } = points.ToArray();
    }

    internal sealed class SpacingIndices(IEnumerable<int[]> indices) : BoltChainSources
    {
        public IReadOnlyList<int[]> Indices { get; } = indices.ToArray();
    }
}
