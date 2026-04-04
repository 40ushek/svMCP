namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartGeometryApi
{
    List<PartGeometryInViewResult> GetAllPartsGeometryInView(int viewId);
    PartGeometryInViewResult GetPartGeometryInView(int viewId, int modelId);
}
