using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Serializable projection of one Clipper2 contour and its nested contours.</summary>
public sealed class OutlineTreeNodeResult
{
    public bool IsHole { get; set; }
    public List<double[]> Polygon { get; set; } = new();
    public List<OutlineTreeNodeResult> Children { get; set; } = new();
}
