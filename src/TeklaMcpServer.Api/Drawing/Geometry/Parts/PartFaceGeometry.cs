using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartFaceGeometry
{
    public int Index { get; set; }
    /// <summary>Face normal in view coordinates, or null when Tekla does not provide one.</summary>
    public double[]? Normal { get; set; }
    public List<PartLoopGeometry> Loops { get; set; } = new();
}
