namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartCandidatePointApi
{
    GetPartCandidatePointsResult GetPartCandidatePointsInView(int viewId, int modelId);
}
