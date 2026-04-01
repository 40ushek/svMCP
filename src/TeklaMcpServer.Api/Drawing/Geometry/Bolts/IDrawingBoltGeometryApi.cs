namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingBoltGeometryApi
{
    BoltGroupGeometryInViewResult GetBoltGroupGeometryInView(int viewId, int modelId);
    PartBoltGeometryInViewResult GetPartBoltGeometryInView(int viewId, int partId);
}
