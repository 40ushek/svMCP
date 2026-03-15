using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class SetMarkContentRequest
{
    public IReadOnlyCollection<int> TargetIds { get; set; } = new List<int>();
    public IReadOnlyCollection<string> RequestedContentElements { get; set; } = new List<string>();
    public bool UpdateContent { get; set; }
    public bool UpdateFontName { get; set; }
    public string FontName { get; set; } = string.Empty;
    public bool UpdateFontColor { get; set; }
    public int FontColorValue { get; set; }
    public bool UpdateFontHeight { get; set; }
    public double FontHeight { get; set; }
}

public sealed class SetMarkContentResult
{
    public List<int> UpdatedObjectIds { get; set; } = new();
    public List<int> FailedObjectIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
