using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutResult
{
    public List<MarkLayoutPlacement> Placements { get; set; } = new();

    public int RemainingOverlaps { get; set; }
}
