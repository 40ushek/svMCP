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

    public bool CanMove { get; set; } = true;
}
