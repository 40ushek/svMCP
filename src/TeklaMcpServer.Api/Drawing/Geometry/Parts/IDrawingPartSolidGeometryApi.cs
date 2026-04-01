namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartSolidGeometryApi
{
    PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId);
}
