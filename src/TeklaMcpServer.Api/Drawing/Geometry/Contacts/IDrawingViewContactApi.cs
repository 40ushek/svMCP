using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewContactApi
{
    /// <summary>
    /// Where the parts drawn in one view touch each other, in that view's coordinates,
    /// along with any part that could not be read.
    /// </summary>
    ViewContactsResult GetContactGraph(int viewId, ContactOptions? options = null);
}
