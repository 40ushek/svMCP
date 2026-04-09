using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingContextApi
{
    public GetDrawingLayoutContextResult GetLayoutContext()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
        {
            return new GetDrawingLayoutContextResult
            {
                Success = false,
                Error = new DrawingNotOpenException().Message
            };
        }

        var viewApi = new TeklaDrawingViewApi();
        var views = viewApi.GetViews();
        var reservedAreas = viewApi.GetReservedAreas();
        var builder = new DrawingContextBuilder();

        return new GetDrawingLayoutContextResult
        {
            Success = true,
            Context = builder.Build(
                new DrawingInfo
                {
                    Name = activeDrawing.Name,
                    Mark = activeDrawing.Mark,
                    Title1 = activeDrawing.Title1,
                    Title2 = activeDrawing.Title2,
                    Title3 = activeDrawing.Title3,
                    Type = activeDrawing.GetType().Name,
                    Status = activeDrawing.UpToDateStatus.ToString()
                },
                views,
                reservedAreas)
        };
    }
}
