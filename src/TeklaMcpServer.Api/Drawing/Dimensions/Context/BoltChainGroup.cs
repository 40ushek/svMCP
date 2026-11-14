namespace TeklaMcpServer.Api.Drawing;

/// <summary>A captured group with typed projection evidence and its dimensioned part roles.</summary>
internal sealed class BoltChainGroup(int id, BoltGroupViewClass viewClass, int[] dimensioned, BoltDimensionChainPreview.StepChain[] chains,
        string? refusal, double[][] positions, BoltGroupGeometry? geometry, int[] parts)
{
    public int Id { get; } = id;
    /// <summary>Every included part the group bolts, from the model links of the group.</summary>
    public int[] Parts { get; } = parts;
    public BoltGroupViewClass ViewClass { get; } = viewClass;
    public int[] Dimensioned { get; } = dimensioned;
    public BoltDimensionChainPreview.StepChain[] Chains { get; } = chains;
    public string? Refusal { get; } = refusal;
    public double[][] Positions { get; } = positions;
    public BoltGroupGeometry? Geometry { get; } = geometry;
    public bool Unbuilt => Dimensioned.Length == 0 || (Chains.Length == 0 && Refusal != null);
    /// <summary>Every center is admitted on this view; a blocked group neither merges nor makes a twin.</summary>
    public bool Admitted { get; private set; }

    internal static BoltChainGroup Read(BoltDimensionChainPreview.Group group, IReadOnlyCollection<int> mainPartIds)
    {
        var parts = group.PartCandidates;
        // A group joining the main part and secondary parts is dimensioned on every secondary part;
        // the main part takes it only when nothing else is bolted.
        var secondary = parts.Where(id => !mainPartIds.Contains(id)).OrderBy(id => id).ToArray();
        return new BoltChainGroup(group.Geometry.ModelId, group.ViewClass,
            secondary.Length > 0 ? secondary : parts.OrderBy(id => id).ToArray(), group.Chains, group.Reason,
            group.Points.Select(point => new[] { point.Point[0], point.Point[1] }).ToArray(), group.Geometry, parts) {
            Admitted = group.Points.Length > 0 && group.Points.All(point => !point.Blocked)
        };
    }
}
