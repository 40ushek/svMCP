using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TeklaDrawingAssemblyOutlineApiTests
{
    [Fact]
    public void BuildUnionsReadablePartOutlines()
    {
        var result = TeklaDrawingAssemblyOutlineApi.Build(
            7,
            [10, 20],
            new StubSolidGeometryApi(
                Square(7, 10, 0, 0, 10, 10),
                Square(7, 20, 30, 0, 40, 10)));

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.PartOutlines.Count);
        Assert.Equal(2, result.AssemblyOutline.Count);
    }

    [Fact]
    public void FailedPartMakesOutlineIncompleteRatherThanSilentlySmaller()
    {
        var failed = new PartSolidGeometryInViewResult
        {
            Success = false,
            ViewId = 7,
            ModelId = 20,
            Error = "solid unavailable"
        };

        var result = TeklaDrawingAssemblyOutlineApi.Build(
            7,
            [10, 20],
            new StubSolidGeometryApi(Square(7, 10, 0, 0, 10, 10), failed));

        Assert.False(result.IsComplete);
        var unread = Assert.Single(result.Unread);
        Assert.Equal(20, unread.ModelId);
        Assert.Equal("solid unavailable", unread.Reason);
        Assert.Single(result.PartOutlines);
    }

    [Fact]
    public void AnOutlineTakenOverASubsetSaysThatItWas()
    {
        // A restricted outline looks exactly like a full one: smaller, with no sign of why.
        // An extent over the frame alone is a different number from the extent of the
        // sheet, and a reader has to be able to tell which they were handed.
        var geometry = new StubSolidGeometryApi(
            Square(1, 1, 0, 0, 100, 100),
            Square(1, 2, 200, 0, 300, 100));

        var whole = TeklaDrawingAssemblyOutlineApi.Build(1, [1, 2], geometry);
        var frameOnly = TeklaDrawingAssemblyOutlineApi.Build(1, [1], geometry, restricted: true, visibleCount: 2);

        Assert.False(whole.Restricted);
        Assert.True(frameOnly.Restricted);
        Assert.Equal(2, frameOnly.VisibleCount);
        Assert.Equal(2, whole.PartOutlines.Count);
        Assert.Single(frameOnly.PartOutlines);
    }

    [Fact]
    public void ARestrictedOutlineIsStillCompleteWhenEveryPartAskedForWasRead()
    {
        // Restricted is not incomplete. The caller chose the subset; nothing went missing.
        var geometry = new StubSolidGeometryApi(Square(1, 1, 0, 0, 100, 100));

        var result = TeklaDrawingAssemblyOutlineApi.Build(1, [1], geometry, restricted: true, visibleCount: 3);

        Assert.True(result.IsComplete);
        Assert.True(result.Restricted);
    }

    private static PartSolidGeometryInViewResult Square(int viewId, int modelId, double minX, double minY, double maxX, double maxY)
    {
        var result = new PartSolidGeometryInViewResult
        {
            Success = true,
            ViewId = viewId,
            ModelId = modelId
        };

        result.Solid.Vertices.AddRange(
        [
            new PartVertexGeometry { Index = 0, Point = [minX, minY, 0] },
            new PartVertexGeometry { Index = 1, Point = [maxX, minY, 0] },
            new PartVertexGeometry { Index = 2, Point = [maxX, maxY, 0] },
            new PartVertexGeometry { Index = 3, Point = [minX, maxY, 0] }
        ]);

        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });
        result.Solid.Faces.Add(face);
        return result;
    }

    private sealed class StubSolidGeometryApi : IDrawingPartSolidGeometryApi
    {
        private readonly Dictionary<int, PartSolidGeometryInViewResult> _byModelId;

        public StubSolidGeometryApi(params PartSolidGeometryInViewResult[] geometry) =>
            _byModelId = geometry.ToDictionary(item => item.ModelId);

        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) =>
            _byModelId[modelId];
    }
}
