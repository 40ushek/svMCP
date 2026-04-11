namespace TeklaMcpServer.Api.Drawing;

public sealed class MoveMarkResult
{
    public bool Moved { get; set; }
    public int MarkId { get; set; }
    public double InsertionX { get; set; }
    public double InsertionY { get; set; }
}
