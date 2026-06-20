namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// A bin region for one placement operation, described by its corners in sheet
/// coordinates. Single source of truth for the sheet↔packer coordinate flip.
///
/// Coordinate contract (roadmap Phase 7):
///   - Sheet space: X grows right, Y grows up.
///   - Packer space: origin (0,0) is at the TOP-LEFT of the frame
///     (sheet corner MinX, MaxY) and packer-Y grows DOWNWARD.
/// This matches the historical hand-rolled flips, e.g.
///   Details.cs:  usableMaxY - placement.Y - h
///   Planner:     SheetHeight - margin - placement.Y
/// </summary>
public readonly struct PlacementFrame
{
    public PlacementFrame(double minX, double minY, double maxX, double maxY)
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

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    /// <summary>Sheet point → packer point. Packer-Y grows down from MaxY.</summary>
    public (double X, double Y) ToPacker(double sheetX, double sheetY)
        => (sheetX - MinX, MaxY - sheetY);

    /// <summary>Packer point → sheet point. Inverse of <see cref="ToPacker"/>.</summary>
    public (double X, double Y) ToSheet(double packerX, double packerY)
        => (MinX + packerX, MaxY - packerY);

    /// <summary>
    /// Packer-space placed rectangle → sheet-space <see cref="ReservedRect"/>.
    /// A packer rect has its origin at the top-left in packer space; in sheet
    /// space its top edge is MaxY-packerY and its bottom edge is that minus h.
    /// </summary>
    public ReservedRect PackerRectToSheet(double packerX, double packerY, double width, double height)
    {
        var sheetMinX = MinX + packerX;
        var sheetMaxY = MaxY - packerY;
        return new ReservedRect(sheetMinX, sheetMaxY - height, sheetMinX + width, sheetMaxY);
    }
}
