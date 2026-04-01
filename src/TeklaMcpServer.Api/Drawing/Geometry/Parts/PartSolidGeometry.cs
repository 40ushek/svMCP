using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartSolidGeometry
{
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public List<PartVertexGeometry> Vertices { get; set; } = new();
    public List<PartFaceGeometry> Faces { get; set; } = new();
}
