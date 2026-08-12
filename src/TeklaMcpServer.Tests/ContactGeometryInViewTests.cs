using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// What contact geometry looks like once it is on the sheet.
///
/// The case worth guarding is the edge-on one. A face contact is a real surface in three
/// dimensions, and on an elevation the view usually looks along it rather than at it - so
/// what the drawing shows is a line, and the patch's four corners are two places seen
/// twice. Taken as they come they would offer a dimension two anchors that are the same
/// anchor.
/// </summary>
public sealed class ContactGeometryInViewTests
{
    /// <summary>A slab lying across the view, meeting its neighbour face-on to the viewer.</summary>
    private static PartSolidGeometryInViewResult Flatwise(int modelId, double z0, double z1) =>
        TwoFacedSlab(modelId, (x, y, z) => (x, y, z), z0, z1);

    /// <summary>The same slab stood on edge, so the view looks along the meeting face.</summary>
    private static PartSolidGeometryInViewResult Edgewise(int modelId, double x0, double x1) =>
        TwoFacedSlab(modelId, (x, y, z) => (z, y, x), x0, x1);

    /// <summary>
    /// Two parallel faces a distance apart - enough of a body for the contact search, and
    /// no more. <paramref name="place"/> decides which way the pair of faces points.
    /// </summary>
    private static PartSolidGeometryInViewResult TwoFacedSlab(
        int modelId, System.Func<double, double, double, (double X, double Y, double Z)> place,
        double near, double far)
    {
        var result = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = modelId };

        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 100), (0, 100) };
        var index = 0;

        foreach (var depth in new[] { near, far })
            foreach (var (x, y) in corners)
            {
                var (px, py, pz) = place(x, y, depth);
                result.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [px, py, pz] });
            }

        var (nx, ny, nz) = place(0, 0, 1);

        var nearFace = new PartFaceGeometry { Index = 0, Normal = [-nx, -ny, -nz] };
        nearFace.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });

        var farFace = new PartFaceGeometry { Index = 1, Normal = [nx, ny, nz] };
        farFace.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [4, 5, 6, 7] });

        result.Solid.Faces.Add(nearFace);
        result.Solid.Faces.Add(farFace);

        return result;
    }

    private sealed class Reader : IDrawingPartSolidGeometryApi
    {
        private readonly Dictionary<int, PartSolidGeometryInViewResult> _byId = new();

        public Reader Returns(int modelId, PartSolidGeometryInViewResult result)
        {
            _byId[modelId] = result;
            return this;
        }

        public Reader Fails(int modelId, string error)
        {
            _byId[modelId] = new PartSolidGeometryInViewResult { Success = false, ModelId = modelId, Error = error };
            return this;
        }

        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => _byId[modelId];
    }

    private static ViewContactGeometryResult Flatten(Reader reader, IEnumerable<int> ids) =>
        ContactGeometryInViewBuilder.Build(
            TeklaDrawingViewContactApi.Build(1, ids, reader, new ContactOptions()));

    [Fact]
    public void AContactTheViewLooksAtKeepsItsCorners()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 50, 100));

        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.Equal(PlanarShapeKind.Polygon, shape.Shape.Kind);
        Assert.Equal(4, shape.Shape.Points.Count);
    }

    [Fact]
    public void AContactTheViewLooksAlongReadsAsTwoPlacesNotFour()
    {
        var reader = new Reader().Returns(10, Edgewise(10, 0, 50)).Returns(11, Edgewise(11, 50, 100));

        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.Equal(PlanarShapeKind.Segment, shape.Shape.Kind);
        Assert.Equal(2, shape.Shape.Points.Count);
        Assert.Equal(new[] { 0.0, 100.0 }, shape.Shape.Points.Select(point => point.Y).OrderBy(y => y));
    }

    [Fact]
    public void TheDepthIsGoneFromWhatIsReported()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 50, 100));

        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.All(shape.Shape.Points, point => Assert.Equal(0, point.Z));
    }

    [Fact]
    public void BothPartsAreNamedBecauseTheSurfaceBelongsToBoth()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 50, 100));

        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.Equal(new[] { 10, 11 }, shape.Participants.ModelObjectIds.OrderBy(id => id));
        Assert.True(shape.Participants.Resolved);
        Assert.NotEqual(string.Empty, shape.ContactId);
        Assert.NotEqual(string.Empty, shape.ShapeId);
    }

    [Fact]
    public void APartThatWasNeverReadLeavesTheGeometryIncomplete()
    {
        // Every region that arrived flattened perfectly. That says nothing about the part
        // whose geometry never arrived, and the result must not read as if it did.
        var reader = new Reader()
            .Returns(10, Flatwise(10, 0, 50))
            .Returns(11, Flatwise(11, 50, 100))
            .Fails(12, "solid unavailable");

        var result = Flatten(reader, [10, 11, 12]);

        Assert.Single(result.Shapes);
        Assert.Empty(result.Unflattened);
        Assert.Contains(result.Unread, part => part.ModelId == 12);
        Assert.False(result.SearchComplete);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void AViewWithNothingTouchingIsCompleteRatherThanSuspect()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 500, 600));

        var result = Flatten(reader, [10, 11]);

        Assert.Empty(result.Shapes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void AnAnchorNamesAPlaceAndSurvivesTheGeometryArrivingDifferently()
    {
        // The same two slabs, described from the other end. Nothing about the junction has
        // changed, so nothing about the name of a place on it may change either.
        var one = Flatten(new Reader()
            .Returns(10, Flatwise(10, 0, 50))
            .Returns(11, Flatwise(11, 50, 100)), [10, 11]);

        var other = Flatten(new Reader()
            .Returns(11, Flatwise(11, 50, 100))
            .Returns(10, Flatwise(10, 0, 50)), [11, 10]);

        Assert.Equal(one.Shapes[0].ShapeId, other.Shapes[0].ShapeId);
        Assert.Equal(one.Shapes[0].AnchorKey(2), other.Shapes[0].AnchorKey(2));
        Assert.Equal(one.Shapes[0].Shape.Points[2], other.Shapes[0].Shape.Points[2]);
    }

    [Fact]
    public void AnAnchorPointsAtOneCornerAndNotAnother()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 50, 100));
        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.Equal(4, shape.Shape.Points.Distinct().Count());
        Assert.Equal(4, Enumerable.Range(0, 4).Select(shape.AnchorKey).Distinct().Count());
    }

    [Fact]
    public void HalfAnOwnerIsNoOwner()
    {
        // One body the search knew only by a name that is not a model id. The surface is
        // still real and still reported - but claiming it for the part that did resolve
        // would make the other one disappear, and something downstream would then read the
        // contact as a feature of a single part.
        var participants = new ContactParticipants("10", "assembly-outline");

        Assert.False(participants.Resolved);
        Assert.Empty(participants.ModelObjectIds);
        Assert.Equal(10, participants.ModelObjectIdA);
        Assert.Null(participants.ModelObjectIdB);
        Assert.Equal("assembly-outline", participants.SolidBId);
    }

    [Fact]
    public void BothOwnersOrNeitherIsWhatTheListSays()
    {
        Assert.Equal(new[] { 10, 11 }, new ContactParticipants("10", "11").ModelObjectIds);
        Assert.Empty(new ContactParticipants("a", "b").ModelObjectIds);
    }

    [Fact]
    public void AnAnchorToAPointThatIsNotThereIsRefused()
    {
        var reader = new Reader().Returns(10, Flatwise(10, 0, 50)).Returns(11, Flatwise(11, 50, 100));
        var shape = Assert.Single(Flatten(reader, [10, 11]).Shapes);

        Assert.Equal(4, shape.Shape.Points.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => shape.AnchorKey(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => shape.AnchorKey(4));
        Assert.NotEqual(string.Empty, shape.AnchorKey(3));
    }

    [Fact]
    public void TwoRegionsThatReadAsOneLineShareTheirName()
    {
        // Two patches in one contact plane, apart in depth and nowhere else. The view looks
        // along that plane, so on the sheet they are one line - and the name says so. This
        // is the reason the id is called a shape id and not a region id.
        var deep = RegionFlattener.Flatten(new List<Vec3>
        {
            new(50, 0, 0), new(50, 100, 0), new(50, 100, 10), new(50, 0, 10)
        });

        var shallow = RegionFlattener.Flatten(new List<Vec3>
        {
            new(50, 0, 80), new(50, 100, 80), new(50, 100, 90), new(50, 0, 90)
        });

        Assert.Equal(PlanarShapeKind.Segment, deep.Kind);
        Assert.Equal(deep.Id, shallow.Id);
    }

    [Fact]
    public void AViewThatWasNeverSearchedCarriesItsReasonThrough()
    {
        var searched = new ViewContactsResult(
            7, ContactGraph.Build([]), new List<UnreadPart>(), "no drawing open");

        var result = ContactGeometryInViewBuilder.Build(searched);

        Assert.Equal("no drawing open", result.Error);
        Assert.False(result.IsComplete);
    }
}
