using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContext
{
    public int DimensionId { get; set; }
    public DimensionItem Item { get; set; } = new();
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public double ViewScale { get; set; }
    public DimensionSourceKind SourceKind { get; set; }
    public DimensionContextRole Role { get; set; }
    public DimensionContextSourceSummary Source { get; set; } = new();
    public DimensionContextGeometry Geometry { get; set; } = new();

    public DrawingLineInfo? ReferenceLine => Geometry.ReferenceLine;
    public DrawingLineInfo? LeadLineMain => Geometry.LeadLineMain;
    public DrawingLineInfo? LeadLineSecond => Geometry.LeadLineSecond;
    public IReadOnlyList<DrawingPointInfo> PointList => Geometry.PointList;
    public IReadOnlyList<double> LengthList => Geometry.LengthList;
    public IReadOnlyList<double> RealLengthList => Geometry.RealLengthList;
    public double Distance => Geometry.Distance;
    public bool HasSourceGeometry => Geometry.LocalBounds != null;
    public IReadOnlyList<int> SourceObjectIds => Source.SourceObjectIds;
    public DrawingBoundsInfo? LocalBounds => Geometry.LocalBounds;
    public IReadOnlyList<string> GeometryWarnings => Geometry.Warnings;
}

internal sealed class DimensionContextSourceSummary
{
    public DimensionSourceKind SourceKind { get; set; }
    public List<int> SourceObjectIds { get; } = [];
}

internal sealed class DimensionContextGeometry
{
    public DrawingLineInfo? ReferenceLine { get; set; }
    public DrawingLineInfo? LeadLineMain { get; set; }
    public DrawingLineInfo? LeadLineSecond { get; set; }
    public List<DrawingPointInfo> PointList { get; } = [];
    public List<double> LengthList { get; } = [];
    public List<double> RealLengthList { get; } = [];
    public double Distance { get; set; }
    public DrawingBoundsInfo? LocalBounds { get; set; }
    public List<string> Warnings { get; } = [];
}

internal sealed class DimensionContextBuildResult
{
    public List<DimensionContext> Contexts { get; } = [];
    public List<string> Warnings { get; } = [];

    public DimensionContext? FindForItem(DimensionItem item) =>
        Contexts.FirstOrDefault(context => ReferenceEquals(context.Item, item));
}
