using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The crossing from what the bridge read in a drawing view to what the contact search
/// takes. Nothing here touches Tekla, which is the point of the adapter being a pure
/// translation: the part of the path most likely to be wrong is also the part that can be
/// tested without a running model.
/// </summary>
public sealed class ViewSolidAdapterTests
{
    /// <summary>A unit square face at z = 0, given as four vertices and one loop.</summary>
    private static PartSolidGeometryInViewResult Square(
        double[]? axisX = null, double[]? axisY = null, double[]? normal = null, int[]? loopIndexes = null)
    {
        var result = new PartSolidGeometryInViewResult
        {
            Success = true,
            ViewId = 7,
            ModelId = 42,
            AxisX = axisX ?? [],
            AxisY = axisY ?? []
        };

        result.Solid.Vertices.AddRange(
        [
            new PartVertexGeometry { Index = 10, Point = [0, 0, 0] },
            new PartVertexGeometry { Index = 11, Point = [100, 0, 0] },
            new PartVertexGeometry { Index = 12, Point = [100, 50, 0] },
            new PartVertexGeometry { Index = 13, Point = [0, 50, 0] }
        ]);

        var face = new PartFaceGeometry { Index = 0, Normal = normal };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [.. loopIndexes ?? [10, 11, 12, 13]] });
        result.Solid.Faces.Add(face);

        return result;
    }

    [Fact]
    public void VerticesAreMatchedByTheirIndexAndNotTheirPosition()
    {
        // The loop names vertices 10 to 13, and nothing promises indexes start at zero.
        var solid = ViewSolidAdapter.FromGeometry(Square());

        var face = Assert.Single(solid!.Faces);
        var loop = Assert.Single(face.Loops);

        Assert.Equal(4, loop.Points.Count);
        Assert.Equal(new Vec3(100, 50, 0), loop.Points[2]);
    }

    [Fact]
    public void AFaceIsDroppedWholeWhenOneOfItsVerticesIsMissing()
    {
        // Keeping the three that resolved would close the contour through a different
        // route: a triangle the body never had, which then gets contacts found on it.
        var solid = ViewSolidAdapter.FromGeometry(Square(loopIndexes: [10, 11, 99, 13]));

        Assert.Null(solid);
    }

    [Fact]
    public void DroppedFacesAreCountedRatherThanSwallowed()
    {
        var geometry = Square();

        var second = new PartFaceGeometry { Index = 1, Normal = [0, 0, -1] };
        second.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [10, 11, 99] });
        geometry.Solid.Faces.Add(second);

        var solid = ViewSolidAdapter.FromGeometry(geometry);

        Assert.Single(solid!.Faces);
        Assert.Equal(1, solid.DroppedFaces);
    }

    [Fact]
    public void AFaceWithADegenerateHoleIsDroppedWhole()
    {
        var geometry = Square();
        geometry.Solid.Faces[0].Loops.Add(new PartLoopGeometry { Index = 1, VertexIndexes = [10, 11] });

        Assert.Null(ViewSolidAdapter.FromGeometry(geometry));
    }

    [Fact]
    public void AFaceWithoutANormalIsKeptButCannotFormAPlane()
    {
        var solid = ViewSolidAdapter.FromGeometry(Square(normal: null));

        // Kept, so its edges still count; its normal is nothing, so the face search skips
        // it rather than working on a plane nobody supplied.
        var face = Assert.Single(solid!.Faces);
        Assert.False(face.Normal.IsUsableDirection);
    }

    [Fact]
    public void TheBoxFollowsThePartsOwnAxesWhenTheyAreGiven()
    {
        var c = System.Math.Sqrt(0.5);
        var solid = ViewSolidAdapter.FromGeometry(Square(axisX: [c, c, 0], axisY: [-c, c, 0]));

        Assert.Equal(c, solid!.BoundingBox.AxisX.X, 6);
        Assert.Equal(c, solid.BoundingBox.AxisX.Y, 6);
    }

    [Fact]
    public void AxesAreNormalisedAndSquaredUpRatherThanTrusted()
    {
        // Tekla promises neither unit length nor a right angle between them.
        var solid = ViewSolidAdapter.FromGeometry(Square(axisX: [5, 0, 0], axisY: [1, 3, 0]));

        var box = solid!.BoundingBox;

        Assert.Equal(1, box.AxisX.Length, 6);
        Assert.Equal(1, box.AxisY.Length, 6);
        Assert.Equal(0, box.AxisX.Dot(box.AxisY), 6);
        Assert.Equal(0, box.AxisY.Dot(box.AxisZ), 6);
    }

    [Fact]
    public void WithoutAxesTheBoxFallsBackToTheViewsOwn()
    {
        var solid = ViewSolidAdapter.FromGeometry(Square());

        // Looser on a raked part, but valid: over-including only costs the narrow phase
        // some time, where a wrong frame would lose contacts outright.
        Assert.Equal(new Vec3(1, 0, 0), solid!.BoundingBox.AxisX);
        Assert.Equal(50, solid.BoundingBox.HalfExtents.X, 6);
        Assert.Equal(25, solid.BoundingBox.HalfExtents.Y, 6);
    }

    [Fact]
    public void CoordinatesArePassedThroughUntouched()
    {
        // They arrive in the view's system and must stay there: the drawing's dimensions
        // live in the same system, and a conversion here would break the only comparison
        // this path exists for.
        var solid = ViewSolidAdapter.FromGeometry(Square());
        var loop = Assert.Single(Assert.Single(solid!.Faces).Loops);

        Assert.Equal(new Vec3(0, 0, 0), loop.Points[0]);
        Assert.Equal(new Vec3(100, 0, 0), loop.Points[1]);
    }

    [Fact]
    public void GeometryThatDidNotSucceedYieldsNothing()
    {
        var failed = Square();
        failed.Success = false;

        Assert.Null(ViewSolidAdapter.FromGeometry(failed));
    }
}
