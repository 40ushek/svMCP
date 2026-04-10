using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class MarkViewContext
{
    public int? ViewId { get; set; }
    public double ViewScale { get; set; }
    public DrawingBoundsInfo? ViewBounds { get; set; }
    public List<MarkContext> Marks { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool IsEmpty => Marks.Count == 0;
}

internal sealed class MarkContext
{
    public int MarkId { get; set; }
    public int? ModelId { get; set; }
    public int? ViewId { get; set; }
    public double ViewScale { get; set; }
    public string PlacingType { get; set; } = string.Empty;
    public string TextAlignment { get; set; } = string.Empty;
    public double RotationAngle { get; set; }
    public DrawingPointInfo? Anchor { get; set; }
    public DrawingPointInfo? CurrentCenter { get; set; }
    public MarkGeometryContext Geometry { get; set; } = new();
    public MarkAxisContext? Axis { get; set; }
    public bool HasLeaderLine { get; set; }
    public bool CanMove { get; set; }
    public List<MarkContextProperty> Properties { get; } = [];
}

internal sealed class MarkGeometryContext
{
    public DrawingBoundsInfo? Bounds { get; set; }
    public DrawingPointInfo? Center { get; set; }
    public List<DrawingPointInfo> Corners { get; } = [];
    public double Width { get; set; }
    public double Height { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsReliable { get; set; }
}

internal sealed class MarkAxisContext
{
    public DrawingPointInfo? Start { get; set; }
    public DrawingPointInfo? End { get; set; }
    public DrawingVectorInfo? Direction { get; set; }
    public double Length { get; set; }
    public double AngleDeg { get; set; }
    public bool IsReliable { get; set; }
}

internal sealed class MarkContextProperty
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class MarkContextBuildResult
{
    public List<MarkContext> Contexts { get; } = [];
    public List<string> Warnings { get; } = [];

    public MarkContext? FindByMarkId(int markId) =>
        Contexts.FirstOrDefault(context => context.MarkId == markId);
}
