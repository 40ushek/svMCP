namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartGeometryApi
{
    List<PartInView> GetAllPartsGeometryInView(int viewId);
    PartInView GetPartGeometryInView(int viewId, int modelId);
}
