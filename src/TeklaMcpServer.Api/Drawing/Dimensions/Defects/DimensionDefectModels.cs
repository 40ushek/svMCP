using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.Dimensions.Defects;

/// <summary>
/// Classes of defect the detector can name. Each one is a mechanical test over geometry —
/// nothing here encodes a preference about how a drawing ought to be laid out.
/// </summary>
public enum DimensionDefectKind
{
    /// <summary>
    /// Parts and dimensions are not in the same coordinate space, so no point can be matched to
    /// any part. Every other check depends on that matching, so this one invalidates the report.
    /// </summary>
    CoordinateSpaceMismatch,

    /// <summary>
    /// Two adjacent points sit on one part and the span between them equals that part's own
    /// extent along the chain — the chain is restating a size that is fixed at fabrication.
    /// </summary>
    RedundantPartSizeSpan,

    /// <summary>
    /// The point matched only a bounding-box corner. On a raked or cut member that corner is in
    /// empty space, so the point is anchored to nothing the fitter can put a tape on.
    /// </summary>
    PhantomAnchor,

    /// <summary>
    /// The point matched only a bounding-box corner, but the parts behind those matches have
    /// incomplete solid geometry — so there were no face or vertex candidates to match against in
    /// the first place. Says the anchor needs looking at, NOT that it is wrong.
    /// </summary>
    AnchorUnverified,

    /// <summary>
    /// The point matched no candidate at all — not even a box corner. Usually the corner of the
    /// whole assembly's bounding box, which belongs to no single part.
    /// </summary>
    UnanchoredPoint,

    /// <summary>
    /// Coverage found more than one equally good candidate and the point's associated part is not
    /// among them, or several parts tie for the anchor. Not wrong by itself — a junction where a
    /// stud meets its plate is ambiguous on every wall — but which part the number belongs to
    /// cannot be read off the geometry alone.
    /// </summary>
    AmbiguousAnchor,

    /// <summary>
    /// The point lies far across the view from its own chain's line, dragging an extension line
    /// over the drawing.
    /// </summary>
    PointFarFromChain,

    /// <summary>
    /// Every position this chain measures is also measured by another chain, so it carries
    /// nothing of its own. Overall dimensions are exempt and are never reported here.
    /// </summary>
    ContainedChain,
}

/// <summary>
/// How much trust the class has earned. The detector reports it so a caller can act on the
/// settled classes and leave the rest to a person, without having to remember which is which.
/// </summary>
public enum DimensionDefectConfidence
{
    /// <summary>Mechanical and settled: the test has no tunable threshold in it.</summary>
    Mechanical,

    /// <summary>
    /// The test carries a threshold that was fitted to very few drawings. Report it, do not act
    /// on it automatically until it has been graded against the captured cases.
    /// </summary>
    Provisional,
}

public sealed class DimensionDefect
{
    public DimensionDefectKind Kind { get; set; }
    public DimensionDefectConfidence Confidence { get; set; }
    public int DimensionId { get; set; }

    /// <summary>The offending point, [x, y] in view coordinates. Null for chain-level defects.</summary>
    public double[]? Point { get; set; }

    /// <summary>The other end of an offending span. Set only by <see cref="DimensionDefectKind.RedundantPartSizeSpan"/>.</summary>
    public double[]? SpanEnd { get; set; }

    /// <summary>The part the finding is about, when one part is responsible for it.</summary>
    public int? ModelObjectId { get; set; }

    /// <summary>Its mark, e.g. "B-267" — the only way to point at a part in an answer to a person.</summary>
    public string? PartPos { get; set; }

    /// <summary>The chain that makes this one redundant. Set only by <see cref="DimensionDefectKind.ContainedChain"/>.</summary>
    public int? ContainedIn { get; set; }

    /// <summary>One line, in numbers, saying what the test found. Not advice about what to do.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// What one chain measures, which is all a caller needs to judge it. This is the part that
/// replaces the read model: a chain costs a few numbers here instead of its full geometry.
/// </summary>
public sealed class DimensionChainSummary
{
    public int DimensionId { get; set; }
    public string Orientation { get; set; } = string.Empty;

    /// <summary>Which rows the sheet prints: Relative, Absolute, or both.</summary>
    public string TeklaDimensionType { get; set; } = string.Empty;

    public double Distance { get; set; }
    public int PointCount { get; set; }

    /// <summary>Gaps between adjacent points along the chain. This row is what the sheet prints.</summary>
    public List<double> RelativeRow { get; set; } = new();

    /// <summary>
    /// Running totals — but measured from the first point in READ order, which Tekla normalises
    /// (top-to-bottom, right-to-left). The order passed at creation fixes the real zero and is
    /// not recoverable, so this agrees with the sheet only when the two happen to coincide.
    /// Never report it as "what the chain prints".
    /// </summary>
    public List<double> AbsoluteRowFromReadOrder { get; set; } = new();

    /// <summary>
    /// True when the chain has two points spanning the full extent of the parts along its axis.
    /// Overall dimensions are exempt from the containment check and are never thinned.
    /// </summary>
    public bool IsOverall { get; set; }
}

public sealed class DimensionDefectReport
{
    public bool Success { get; set; }
    public int ViewId { get; set; }

    /// <summary>
    /// False when a chain measures far more than every visible part together spans — a symptom,
    /// not a proven fact: no direct "are these the same coordinate system" signal is read from
    /// Tekla. See the accompanying <see cref="DimensionDefectKind.CoordinateSpaceMismatch"/>
    /// entry in <see cref="Defects"/>, reported at <see cref="DimensionDefectConfidence.Provisional"/>.
    /// Treated as serious enough to gate the rest of the report regardless: every other finding
    /// depends on matching points to parts, and that match is meaningless if the suspicion holds.
    /// </summary>
    public bool CoordinateSpaceOk { get; set; }

    public List<DimensionChainSummary> Chains { get; set; } = new();
    public List<DimensionDefect> Defects { get; set; } = new();

    /// <summary>Checks that could not run, and why — a skipped check must never look like a clean one.</summary>
    public List<string> Warnings { get; set; } = new();

    public string? Error { get; set; }
}
