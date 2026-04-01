namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingAssemblyGeometryApi
{
    AssemblyGeometryInViewResult GetAssemblyGeometryInView(int viewId, int modelId);
}
