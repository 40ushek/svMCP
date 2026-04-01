using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartPointApi
{
    GetPartPointsResult GetPartPointsInView(int viewId, int modelId);
    List<GetPartPointsResult> GetAllPartPointsInView(int viewId);
}
