using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Reads the exact projected outline of the visible parts of one drawing view.</summary>
public interface IDrawingViewOutlineApi
{
    ViewAssemblyOutlineResult GetAssemblyOutline(int viewId, OutlineOptions? options = null);
}
