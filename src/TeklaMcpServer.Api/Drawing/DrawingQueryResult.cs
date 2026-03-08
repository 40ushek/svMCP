using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class OpenDrawingResult
{
    public bool Found { get; set; }
    public bool Opened { get; set; }
    public string RequestedGuid { get; set; } = string.Empty;
    public DrawingInfo Drawing { get; set; } = new();
}

public sealed class CloseDrawingResult
{
    public bool HasActiveDrawing { get; set; }
    public bool Closed { get; set; }
    public DrawingInfo Drawing { get; set; } = new();
}

public sealed class ExportDrawingsPdfResult
{
    public List<string> ExportedFiles { get; set; } = new();
    public List<string> FailedToExport { get; set; } = new();
    public List<string> MissingGuids { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
}
