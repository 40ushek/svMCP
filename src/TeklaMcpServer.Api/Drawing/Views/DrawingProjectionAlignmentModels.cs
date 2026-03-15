using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class ProjectionAlignmentResult
{
    public string Mode { get; set; } = "none";
    public int AppliedMoves { get; set; }
    public int SkippedMoves { get; set; }
    public List<string> Diagnostics { get; } = new();
}

internal sealed class ProjectionViewState
{
    public ProjectionViewState(
        int viewId,
        double originX,
        double originY,
        double scale,
        double width,
        double height,
        double frameOffsetSheetX,
        double frameOffsetSheetY)
    {
        ViewId = viewId;
        OriginX = originX;
        OriginY = originY;
        Scale = scale;
        Width = width;
        Height = height;
        FrameOffsetSheetX = frameOffsetSheetX;
        FrameOffsetSheetY = frameOffsetSheetY;
    }

    public int ViewId { get; }
    public double OriginX { get; }
    public double OriginY { get; }
    public double Scale { get; }
    public double Width { get; }
    public double Height { get; }
    public double FrameOffsetSheetX { get; }
    public double FrameOffsetSheetY { get; }
}

internal sealed class ProjectionRect
{
    public ProjectionRect(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}
