using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingViewContext
{
    public int? ViewId { get; set; }
    public double ViewScale { get; set; }
    public List<PartGeometryInViewResult> Parts { get; } = [];
    public DrawingBoundsInfo? PartsBounds { get; set; }
    public List<DrawingPointInfo> PartsHull { get; } = [];
    public List<BoltGroupGeometry> Bolts { get; } = [];
    public List<string> GridIds { get; } = [];
    public List<DrawingTextBox> DimensionTextBoxes { get; } = [];
    public List<DrawingTextBox> MarkTextBoxes { get; } = [];

    /// <summary>
    /// Reports which shortening conversion was actually applied to <see cref="DimensionTextBoxes"/>
    /// when this context was built. Reflects effective mode (e.g. "toVisual" requested but
    /// mapper had no shortening becomes "none"). Used by PerfTrace to distinguish smoke runs.
    /// </summary>
    public string AppliedDimensionTextBoxShorteningMode { get; set; } = "none";
    public List<string> Warnings { get; } = [];

    public bool IsEmpty => Parts.Count == 0 && Bolts.Count == 0 && GridIds.Count == 0;
}
