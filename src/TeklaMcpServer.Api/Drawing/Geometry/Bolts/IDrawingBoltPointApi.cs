namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingBoltPointApi
{
    GetBoltGroupPointsResult GetBoltGroupPointsInView(int viewId, int modelId);
    List<GetBoltGroupPointsResult> GetPartBoltPointsInView(int viewId, int partId);
}
