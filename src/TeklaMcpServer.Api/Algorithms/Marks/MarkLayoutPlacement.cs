namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutPlacement
{
    public int Id { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool HasLeaderLine { get; set; }

    public bool CanMove { get; set; }
}
