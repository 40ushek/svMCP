using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// How well the candidate-point layer covers one already-corrected dimension chain.
/// This is acceptance material for the placement planner, not a placement decision:
/// it never picks a winner among several matches.
/// </summary>
public sealed class DimensionChainCoverageResult
{
    public bool Success { get; set; }
    public int ViewId { get; set; }
    public int DimensionId { get; set; }

    /// <summary>Match radius in view coordinates (mm).</summary>
    public double Tolerance { get; set; }

    /// <summary>Model ids whose candidates were searched.</summary>
    public List<int> SearchedModelIds { get; set; } = new();

    public string? Error { get; set; }
    public List<DimensionPointCoverage> Points { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class DimensionPointCoverage
{
    public int DimensionId { get; set; }

    /// <summary>Segments of the chain that start or end at this point.</summary>
    public List<int> SegmentIds { get; set; } = new();

    public int PointOrder { get; set; }
    public double[] Point { get; set; } = [];

    /// <summary>Model object the existing dimension context associated with this point.</summary>
    public int? AssociatedModelId { get; set; }

    /// <summary>Association status reported by the existing dimension context.</summary>
    public string AssociationStatus { get; set; } = string.Empty;

    public DimensionCoverageMatchStage Stage { get; set; }

    /// <summary>
    /// Every candidate within tolerance, closest first. Deliberately not reduced to one:
    /// which of these a plan should prefer is the rule 2b has to derive.
    /// </summary>
    public List<DimensionCoverageMatch> Matches { get; set; } = new();

    public DimensionCoverageStatus Status { get; set; }

    /// <summary>
    /// True when the point matched something, but only a hull vertex or a bounding-box corner —
    /// no face edge and no solid vertex. **Requires adjudication; it is not proof of an error.**
    ///
    /// Two quite different situations produce it. The point may genuinely sit in empty space: on
    /// a raked member the box corner is nowhere on the part, which is what a real drawing turned
    /// out to contain. Or the part's solid could not be traversed, in which case the candidate
    /// layer offers nothing but the axis and the box, and a perfectly good point has no better
    /// evidence available. Check `SolidGeometryComplete` for the parts involved before reading
    /// this as a defect.
    /// </summary>
    public bool FallbackOnly { get; set; }

    /// <summary>
    /// Highest-quality evidence among the matches, so a consumer can rank without re-reading
    /// every match.
    /// </summary>
    public string BestConfidence { get; set; } = string.Empty;
}

public sealed class DimensionCoverageMatch
{
    public int ModelObjectId { get; set; }
    public string AnchorKey { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public double[] Point { get; set; } = [];
    public double[]? InPlaneNormal { get; set; }

    /// <summary>Distance in the view XY plane from the dimension point to this candidate (mm).</summary>
    public double Distance { get; set; }
}

/// <summary>
/// `Matched` means one position, which may legitimately carry several anchor keys: a face edge
/// is shared by two faces, and a vertex coincides with its edge ends in projection. `Ambiguous`
/// is reserved for matches that sit at genuinely different places.
/// </summary>
public enum DimensionCoverageStatus
{
    Matched,
    Ambiguous,
    Missing
}

/// <summary>
/// Whether the associated part was among the parts that matched. Every part is always searched,
/// so this narrows nothing — it only says whether the existing association was borne out.
/// </summary>
public enum DimensionCoverageMatchStage
{
    None,
    SamePart,
    NearestByDistance
}
