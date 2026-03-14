namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutItem
{
    public int Id { get; set; }

    public double AnchorX { get; set; }

    public double AnchorY { get; set; }

    public double CurrentX { get; set; }

    public double CurrentY { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool HasLeaderLine { get; set; }

    public bool HasAxis { get; set; }

    public double AxisDx { get; set; }

    public double AxisDy { get; set; }

    public bool CanMove { get; set; } = true;

    // Optional view bounds in sheet coordinates — candidates outside will be rejected.
    // If all are 0 (default) bounds are not enforced.
    public double BoundsMinX { get; set; }
    public double BoundsMinY { get; set; }
    public double BoundsMaxX { get; set; }
    public double BoundsMaxY { get; set; }

    public bool HasBounds => BoundsMaxX > BoundsMinX && BoundsMaxY > BoundsMinY;
}
