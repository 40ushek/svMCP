using System.Linq;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingViewContextApi
{
    private readonly Model _model;

    public TeklaDrawingViewContextApi(Model model)
    {
        _model = model;
    }

    public GetDrawingViewContextResult GetViewContext(int viewId)
    {
        DrawingViewsResult viewsResult;
        try
        {
            viewsResult = new TeklaDrawingViewApi().GetViews();
        }
        catch (DrawingNotOpenException exception)
        {
            return new GetDrawingViewContextResult
            {
                Success = false,
                ViewId = viewId,
                Error = exception.Message
            };
        }

        var view = viewsResult.Views.FirstOrDefault(candidate => candidate.Id == viewId);

        if (view == null)
            return new GetDrawingViewContextResult
            {
                Success = false,
                ViewId = viewId,
                Error = new ViewNotFoundException(viewId).Message
            };

        var viewScale = view.Scale > 0 ? view.Scale : 1.0;
        var builder = new DrawingViewContextBuilder(
            new TeklaDrawingPartGeometryApi(_model),
            new TeklaDrawingBoltGeometryApi(_model),
            new TeklaDrawingGridApi());
        var context = builder.Build(viewId, viewScale);
        return DrawingViewContextMapper.ToResult(context);
    }
}
