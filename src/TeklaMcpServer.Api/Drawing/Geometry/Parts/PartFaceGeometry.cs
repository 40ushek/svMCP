using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartFaceGeometry
{
    public int Index { get; set; }
    public double[] Normal { get; set; } = [];
    public List<PartLoopGeometry> Loops { get; set; } = new();
}
