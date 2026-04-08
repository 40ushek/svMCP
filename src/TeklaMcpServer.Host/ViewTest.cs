using Tekla.Structures.Model.UI;

namespace TeklaMcpServer.Host;

internal class ViewTest
{
    public static void CheckView()
    {
        var curView = ViewHandler.GetActiveView();
        var filter = curView.ViewFilter;
    }
}
