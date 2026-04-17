using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal sealed class ForceDirectedMarkItem
{
    public int Id { get; set; }
    public int? OwnModelId { get; set; }
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool CanMove { get; set; }
    public IReadOnlyList<double[]> LocalCorners { get; set; } = [];
    public IReadOnlyList<double[]>? OwnPolygon { get; set; }
}
