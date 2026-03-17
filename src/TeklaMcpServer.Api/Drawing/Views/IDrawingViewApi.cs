using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingViewApi
{
    DrawingViewsResult GetViews();
    MoveViewResult     MoveView(int viewId, double dx, double dy, bool absolute);
    SetViewScaleResult SetViewScale(IEnumerable<int> viewIds, double scale);
    FitViewsResult     FitViewsToSheet(double? margin, double gap, double titleBlockHeight);
}
