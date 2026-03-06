using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class CreateMarksResult
{
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<int> CreatedMarkIds { get; set; } = new List<int>();
    public bool? AttributesLoaded { get; set; }
}
