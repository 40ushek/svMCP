using Tekla.Structures.Drawing;

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

        return new GetDrawingLayoutContextResult
        {
            Success = true,
            Context = new DrawingLayoutContextBuilder().Build(activeDrawing)
        };
    }
}
