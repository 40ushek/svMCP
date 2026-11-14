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
    /// <summary>Every bolt column along the main part from its start (Tekla's Position bolts to: Main part).</summary>
    public const string PositionKind = "position";

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
    /// <summary>Bolt and hole of every group the chain measures.</summary>
    public object Holes { get; }
    /// <summary>
    /// For a secondary part chain: the key of a main-part chain whose bolts stand on every one of
    /// its bolts. It is left out only when that chain is finally created.
    /// </summary>
    public string? OverMainPartChain { get; }
    /// <summary>Every bolt group the chain measures.</summary>
    public IReadOnlyList<int> GroupIds { get; }
    /// <summary>Something to know about a chain that is created all the same (a chain left open).</summary>
    public string? Remark { get; }
    /// <summary>The key of a chain this one is created only with (the position chain for a chain across from the main part).</summary>
    public string? RequiresChain { get; }

    public BoltChainCandidate(string key, string kind, int groupId, int? partId, bool mainPart,
        BoltGroupViewClass viewClass, int axis, IEnumerable<DimensionRulePoint> points, IEnumerable<double> segments,
        string? builtRefusal, bool edgeConfirmed, DimensionDirection outwardNormal, double baseOffset, object sources, object? holes = null,
        string? overMainPartChain = null, IEnumerable<int>? groupIds = null, string? remark = null, string? requiresChain = null)
    {
        RequiresChain = requiresChain;
        OverMainPartChain = overMainPartChain;
        Remark = remark;
        GroupIds = (groupIds ?? [groupId]).ToArray();
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
        Holes = holes ?? Array.Empty<object>();
    }

    /// <summary>The same chain, linked to the main-part chain standing on its bolts.</summary>
    public BoltChainCandidate Over(string mainPartChain) => new(Key, Kind, GroupId, PartId, MainPart, ViewClass, Axis,
        Points, Segments, BuiltRefusal, EdgeConfirmed, OutwardNormal, BaseOffset, Sources, Holes, mainPartChain, GroupIds, Remark,
        RequiresChain);
}

/// <summary>What the bolt rule needs: the chains built on the view; the settings come with the rule.</summary>
internal sealed class BoltChainInput(IEnumerable<BoltChainCandidate> candidates)
{
    public IReadOnlyList<BoltChainCandidate> Candidates { get; } = candidates.ToArray();
}
