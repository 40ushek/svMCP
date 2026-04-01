using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed class DrawingFitConflict
{
    public int ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string AttemptedZone { get; set; } = string.Empty;
    public double? BBoxMinX { get; set; }
    public double? BBoxMinY { get; set; }
    public double? BBoxMaxX { get; set; }
    public double? BBoxMaxY { get; set; }
    public List<DrawingFitConflictItem> Conflicts { get; set; } = new();
}

public sealed class DrawingFitConflictItem
{
    public string Type { get; set; } = string.Empty;
    public int? OtherViewId { get; set; }
    public string Target { get; set; } = string.Empty;
}

public sealed class DrawingFitFailedException : System.InvalidOperationException
{
    public DrawingFitFailedException(string message, IReadOnlyList<DrawingFitConflict>? conflicts = null)
        : base(message)
    {
        Conflicts = conflicts ?? System.Array.Empty<DrawingFitConflict>();
    }

    public IReadOnlyList<DrawingFitConflict> Conflicts { get; }
}

