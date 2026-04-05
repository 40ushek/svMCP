using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionViewContext
{
    public int? ViewId { get; set; }
    public double ViewScale { get; set; }
    public List<PartGeometryInViewResult> Parts { get; } = [];
    public DrawingBoundsInfo? PartsBounds { get; set; }
    public List<DrawingPointInfo> PartsHull { get; } = [];
    public List<BoltGroupGeometry> Bolts { get; } = [];
    public List<string> GridIds { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool IsEmpty => Parts.Count == 0 && Bolts.Count == 0 && GridIds.Count == 0;
}
