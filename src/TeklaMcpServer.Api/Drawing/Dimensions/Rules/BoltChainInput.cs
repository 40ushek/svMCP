namespace TeklaMcpServer.Api.Drawing;

/// <summary>A bolt chain already built from the view, before the decision to create it.</summary>
internal sealed class BoltChainCandidate
{
    /// <summary>Edge - bolts - edge of one part and group (Tekla's All).</summary>
    public const string PartKind = "part";
    /// <summary>Steps of one projected row of one group (Tekla's Internal).</summary>
    public const string InternalKind = "internal";
    /// <summary>A group no chain could be built for; carries only its refusal.</summary>
    public const string GroupKind = "group";

    public string Key { get; }
    public string Kind { get; }
    public int GroupId { get; }
    public int? PartId { get; }
    public bool MainPart { get; }
    public BoltGroupViewClass ViewClass { get; }
    /// <summary>0 for a chain along view X, 1 along view Y.</summary>
    public int Axis { get; }
    public IReadOnlyList<DimensionRulePoint> Points { get; }
    public IReadOnlyList<double> Segments { get; }
    /// <summary>Why the chain could not be built; null when it was.</summary>
    public string? BuiltRefusal { get; }
    /// <summary>For a part chain: whether its part edges are confirmed on this view.</summary>
    public bool EdgeConfirmed { get; }
    /// <summary>From the points toward the dimension line.</summary>
    public DimensionDirection OutwardNormal { get; }
    /// <summary>View units from the first point to the part's outer bound along the normal.</summary>
    public double BaseOffset { get; }
    /// <summary>Source bolt group and bolt index of every point.</summary>
    public object Sources { get; }

    public BoltChainCandidate(string key, string kind, int groupId, int? partId, bool mainPart,
        BoltGroupViewClass viewClass, int axis, IEnumerable<DimensionRulePoint> points, IEnumerable<double> segments,
        string? builtRefusal, bool edgeConfirmed, DimensionDirection outwardNormal, double baseOffset, object sources)
    {
        Key = key;
        Kind = kind;
        GroupId = groupId;
        PartId = partId;
        MainPart = mainPart;
        ViewClass = viewClass;
        Axis = axis;
        Points = points.ToArray();
        Segments = segments.ToArray();
        BuiltRefusal = builtRefusal;
        EdgeConfirmed = edgeConfirmed;
        OutwardNormal = outwardNormal;
        BaseOffset = baseOffset;
        Sources = sources;
    }
}

/// <summary>What the bolt rule needs: the chains built on the view; the settings come with the rule.</summary>
internal sealed class BoltChainInput(IEnumerable<BoltChainCandidate> candidates)
{
    public IReadOnlyList<BoltChainCandidate> Candidates { get; } = candidates.ToArray();
}
