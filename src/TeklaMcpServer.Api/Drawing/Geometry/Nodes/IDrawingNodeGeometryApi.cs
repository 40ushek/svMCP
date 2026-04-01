namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingNodeGeometryApi
{
    GetAssemblyNodesResult GetAssemblyNodesInView(int viewId, int modelId);
}
