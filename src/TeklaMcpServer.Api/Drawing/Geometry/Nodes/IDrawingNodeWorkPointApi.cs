namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingNodeWorkPointApi
{
    GetAssemblyWorkPointsResult GetAssemblyWorkPointsInView(int viewId, int modelId);
}
