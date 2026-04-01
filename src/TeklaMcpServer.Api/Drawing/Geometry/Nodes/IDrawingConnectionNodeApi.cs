namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingConnectionNodeApi
{
    GetAssemblyConnectionNodesResult GetAssemblyConnectionNodesInView(int viewId, int modelId);
}
