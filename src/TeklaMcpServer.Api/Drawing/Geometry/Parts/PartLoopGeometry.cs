using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartLoopGeometry
{
    public int Index { get; set; }
    public List<int> VertexIndexes { get; set; } = new();
}
