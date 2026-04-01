namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingAssemblyPointApi
{
    GetAssemblyPointsResult GetAssemblyPointsInView(int viewId, int modelId);
}
