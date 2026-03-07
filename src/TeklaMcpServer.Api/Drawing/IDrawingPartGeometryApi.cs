namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartGeometryApi
{
    PartGeometryInViewResult GetPartGeometryInView(int viewId, int modelId);
}
