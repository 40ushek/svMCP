using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public interface IDrawingViewApi
{
    DrawingViewsResult GetViews();
    MoveViewResult     MoveView(int viewId, double dx, double dy, bool absolute);
    SetViewScaleResult SetViewScale(IEnumerable<int> viewIds, double scale);
    FitViewsResult     FitViewsToSheet(
        double? margin,
        double gap,
        double titleBlockHeight,
        DrawingScalePolicy scalePolicy = DrawingScalePolicy.UniformAllNonDetail,
        DrawingLayoutApplyMode applyMode = DrawingLayoutApplyMode.DebugPreview);
}

