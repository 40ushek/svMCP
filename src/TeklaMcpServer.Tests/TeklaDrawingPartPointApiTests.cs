using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingPartPointApiTests
{
    [Fact]
    public void BuildResult_DoesNotUseSolidPointsWhenGeometryIsIncomplete()
    {
        var result = TeklaDrawingPartPointApi.BuildResult(new PartInView
        {
            Success = true,
            ViewId = 10,
            ModelId = 101,
            SolidGeometryComplete = false,
            SolidVertices =
            [
                [100d, 100d, 0d],
                [110d, 100d, 0d],
                [100d, 110d, 0d]
            ]
        });

        Assert.DoesNotContain(result.Points, point =>
            point.Kind is DrawingPartPointKind.SolidVertex
                or DrawingPartPointKind.HullVertex
                or DrawingPartPointKind.ExtremeStart
                or DrawingPartPointKind.ExtremeEnd);
    }
}
