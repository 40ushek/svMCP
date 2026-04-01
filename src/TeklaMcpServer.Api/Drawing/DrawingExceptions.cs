namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingNotOpenException : System.Exception
{
    public DrawingNotOpenException() : base("No drawing is currently open.") { }
}

public sealed class ViewNotFoundException : System.Exception
{
    public ViewNotFoundException(int id) : base($"View with ID {id} not found in active drawing.") { }
}
