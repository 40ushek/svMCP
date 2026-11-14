using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>The three supported source representations. Each owns its output contract;
/// precision never depends on the runtime collection type supplied by a builder.</summary>
internal abstract class BoltChainSources
{
    public static BoltChainSources Empty { get; } = Points([]);
    public static BoltChainSources Captured(IEnumerable<BoltPartChainPreview.ProjectedPosition> positions) => new CapturedParts(positions);
    public static BoltChainSources Points(IEnumerable<double[]> points) => new ChainPoints(points);
    public static BoltChainSources Indices(IEnumerable<int[]> indices) => new SpacingIndices(indices);
    internal abstract object Project();

    private sealed class CapturedParts(IEnumerable<BoltPartChainPreview.ProjectedPosition> positions) : BoltChainSources
    {
        private readonly BoltPartChainPreview.ProjectedPosition[] _positions = positions.ToArray();
        // Captured source coordinates bypass the display converter, as in the original response.
        internal override object Project() => JsonSerializer.SerializeToElement(_positions.Select(position => position.Project()).ToArray());
    }

    private sealed class ChainPoints(IEnumerable<double[]> points) : BoltChainSources
    {
        private readonly double[][] _points = points.ToArray();
        internal override object Project() => _points.Select(point => new { point }).ToArray();
    }

    private sealed class SpacingIndices(IEnumerable<int[]> indices) : BoltChainSources
    {
        private readonly int[][] _indices = indices.ToArray();
        internal override object Project() => _indices;
    }
}
