namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutPlacement
{
    public int Id { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double AnchorX { get; set; }

    public double AnchorY { get; set; }

    public bool HasLeaderLine { get; set; }

    public bool HasAxis { get; set; }

    public double AxisDx { get; set; }

    public double AxisDy { get; set; }

    public bool CanMove { get; set; }
}
