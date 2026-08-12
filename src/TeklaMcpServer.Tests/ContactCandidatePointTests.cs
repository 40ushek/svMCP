using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Candidates taken from where parts touch.
///
/// The layer is small; what it must not do is the interesting part. It must name both
/// parts and not one, it must not emit a point it cannot attribute, and it must not let an
/// incomplete search look like a complete set of places.
/// </summary>
public sealed class ContactCandidatePointTests
{
    private static PartSolidGeometryInViewResult Slab(int modelId, double z0, double z1, bool onEdge = false)
    {
        var result = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = modelId };

        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 100), (0, 100) };
        var index = 0;

        foreach (var depth in new[] { z0, z1 })
            foreach (var (x, y) in corners)
            {
                result.Solid.Vertices.Add(new PartVertexGeometry
                {
                    Index = index++,
                    Point = onEdge ? [depth, y, x] : [x, y, depth]
                });
            }

        double[] normal = onEdge ? [1, 0, 0] : [0, 0, 1];

        var near = new PartFaceGeometry { Index = 0, Normal = [-normal[0], -normal[1], -normal[2]] };
        near.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });

        var far = new PartFaceGeometry { Index = 1, Normal = normal };
        far.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [4, 5, 6, 7] });

        result.Solid.Faces.Add(near);
        result.Solid.Faces.Add(far);

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

    private static ViewContactCandidatePointsResult Candidates(Reader reader, IEnumerable<int> ids) =>
        DrawingContactCandidatePointBuilder.Build(
            ContactGeometryInViewBuilder.Build(
                TeklaDrawingViewContactApi.Build(1, ids, reader, new ContactOptions())));

    private static ViewContactCandidatePointsResult TwoSlabs(bool onEdge = false) =>
        Candidates(new Reader()
            .Returns(10, Slab(10, 0, 50, onEdge))
            .Returns(11, Slab(11, 50, 100, onEdge)), [10, 11]);

    [Fact]
    public void APolygonGivesEveryCornerAndALineGivesTwoEnds()
    {
        Assert.Equal(4, TwoSlabs().Points.Count);
        Assert.Equal(2, TwoSlabs(onEdge: true).Points.Count);
    }

    [Fact]
    public void EveryPointNamesBothPartsAndNeitherAlone()
    {
        var result = TwoSlabs();

        Assert.All(result.Points, point => Assert.Equal(new[] { 10, 11 }, point.ModelObjectIds.OrderBy(id => id)));
    }

    [Fact]
    public void EveryPointSaysWhereItCameFromAndHowSurelyItIsThere()
    {
        var point = TwoSlabs().Points[0];

        Assert.Equal(DrawingPartCandidatePointSource.Contact, point.Source);
        Assert.Equal(DrawingPartCandidateConfidence.DerivedGeometry, point.Confidence);
        Assert.Equal("contact_shape_point", point.Reason.Code);
        Assert.Equal(new[] { 10, 11 }, point.Reason.ModelObjectIds.OrderBy(id => id));
    }

    [Fact]
    public void TheAnchorNamesTheContactTheShapeAndThePlaceAndNoPart()
    {
        var result = TwoSlabs();
        var point = result.Points[0];

        Assert.Equal(DrawingPartCandidateAnchorKind.ContactPoint, point.Anchor.Kind);

        // No owner: a contact belongs to two parts, so an anchor claiming one would make
        // the other a coincidence. The key stands without it because the id already says
        // which contact and which shape.
        Assert.Null(point.Anchor.ModelObjectId);
        Assert.Equal("ContactPoint:" + point.Anchor.Id, point.Anchor.Key);

        // The contact id carries separators of its own, so the three fields are recovered
        // from the right: a run of digits, then a fixed-width hash, then whatever is left.
        var fields = point.Anchor.Id.Split('|');
        Assert.Equal("0", fields[^1]);
        Assert.Equal(16, fields[^2].Length);
        Assert.StartsWith(string.Join("|", fields[..^2]), point.Anchor.Id);

        Assert.Equal(4, result.Points.Select(candidate => candidate.Anchor.Key).Distinct().Count());
    }

    [Fact]
    public void TheSamePlaceIsNamedTheSameWayWhicheverWayTheGeometryArrived()
    {
        var one = TwoSlabs();

        var other = Candidates(new Reader()
            .Returns(11, Slab(11, 50, 100))
            .Returns(10, Slab(10, 0, 50)), [11, 10]);

        Assert.Equal(
            one.Points.Select(point => point.Anchor.Key).OrderBy(key => key),
            other.Points.Select(point => point.Anchor.Key).OrderBy(key => key));
    }

    [Fact]
    public void APartThatWasNeverReadLeavesTheSetIncompleteWithoutSpoilingIt()
    {
        var result = Candidates(new Reader()
            .Returns(10, Slab(10, 0, 50))
            .Returns(11, Slab(11, 50, 100))
            .Fails(12, "solid unavailable"), [10, 11, 12]);

        // The four points that were found are still four correct points.
        Assert.Equal(4, result.Points.Count);
        Assert.Contains(result.Unread, part => part.ModelId == 12);
        Assert.False(result.SearchComplete);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void AShapeWhoseOwnersAreUnknownGivesNoPointAndIsHandedBack()
    {
        // The geometry is real and is kept, but "these two parts meet here" is a sentence
        // that cannot be finished, so no candidate pretends otherwise.
        var shape = new ContactShapeInView(
            "contact-1",
            new ContactParticipants("10", "assembly-outline"),
            ContactKind.FaceToFace,
            RegionFlattener.Flatten(new List<Vec3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0) }));

        var geometry = new ViewContactGeometryResult(
            1, new[] { shape }, new List<UnflattenedRegion>(), new List<UnreadPart>(), searchComplete: true);

        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        Assert.Empty(result.Points);
        Assert.Same(shape, Assert.Single(result.Unresolved));
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void TwoFormsOfOneContactThatLandOnTheSameLineGiveOneSetOfPoints()
    {
        // Two patches of one contact, apart in depth and nowhere else. The view looks along
        // their plane, so the sheet shows one line - and one line has two ends, not four.
        // The shapes stay two in the geometry; it is the places that must not double.
        var participants = new ContactParticipants("10", "11");

        var deep = new ContactShapeInView("contact-1", participants, ContactKind.FaceToFace,
            RegionFlattener.Flatten(new List<Vec3>
            {
                new(50, 0, 0), new(50, 100, 0), new(50, 100, 10), new(50, 0, 10)
            }));

        var shallow = new ContactShapeInView("contact-1", participants, ContactKind.FaceToFace,
            RegionFlattener.Flatten(new List<Vec3>
            {
                new(50, 0, 80), new(50, 100, 80), new(50, 100, 90), new(50, 0, 90)
            }));

        Assert.Equal(deep.ShapeId, shallow.ShapeId);

        var geometry = new ViewContactGeometryResult(
            1, new[] { deep, shallow }, new List<UnflattenedRegion>(),
            new List<UnreadPart>(), searchComplete: true);

        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(2, result.Points.Select(point => point.Anchor.Key).Distinct().Count());
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void TwoContactsThatLookAlikeAreStillTwoPlaces()
    {
        // The same shape under a different contact is a different pair of parts meeting.
        // Nothing may merge those - the dedup is by anchor, and the anchor names the contact.
        var shape = RegionFlattener.Flatten(new List<Vec3>
        {
            new(50, 0, 0), new(50, 100, 0), new(50, 100, 10), new(50, 0, 10)
        });

        var geometry = new ViewContactGeometryResult(
            1,
            new[]
            {
                new ContactShapeInView("contact-1", new ContactParticipants("10", "11"), ContactKind.FaceToFace, shape),
                new ContactShapeInView("contact-2", new ContactParticipants("10", "12"), ContactKind.FaceToFace, shape)
            },
            new List<UnflattenedRegion>(), new List<UnreadPart>(), searchComplete: true);

        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        Assert.Equal(4, result.Points.Count);
        Assert.Equal(4, result.Points.Select(point => point.Anchor.Key).Distinct().Count());
    }

    [Fact]
    public void ALostRegionIsCarriedThroughRatherThanForgotten()
    {
        var participants = new ContactParticipants("10", "11");
        var lost = new UnflattenedRegion("contact-1", 0, participants, "region left nothing once flattened");

        var geometry = new ViewContactGeometryResult(
            1, new List<ContactShapeInView>(), new[] { lost },
            new List<UnreadPart>(), searchComplete: true);

        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        Assert.Same(lost, Assert.Single(result.Unflattened));
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void AViewWhereNothingTouchesIsCompleteRatherThanSuspect()
    {
        var result = Candidates(new Reader()
            .Returns(10, Slab(10, 0, 50))
            .Returns(11, Slab(11, 500, 600)), [10, 11]);

        Assert.Empty(result.Points);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void AViewThatWasNeverSearchedCarriesItsReasonThrough()
    {
        var geometry = ContactGeometryInViewBuilder.Build(
            new ViewContactsResult(7, ContactGraph.Build([]), new List<UnreadPart>(), "no drawing open"));

        var result = DrawingContactCandidatePointBuilder.Build(geometry);

        Assert.Equal("no drawing open", result.Error);
        Assert.Empty(result.Points);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void ContactCandidatesAreNotContourCandidates()
    {
        // The two layers stay apart. A contour corner says where a part ends; a contact
        // point says where two parts meet. Both may land on the same millimetre, and the
        // difference between the claims is what a later rule has to weigh.
        Assert.All(TwoSlabs().Points, point =>
        {
            Assert.Equal(DrawingPartCandidatePointSource.Contact, point.Source);
            Assert.NotEqual(DrawingPartCandidatePointSource.PartContour, point.Source);
            Assert.NotEqual(DrawingPartCandidateAnchorKind.ContourVertex, point.Anchor.Kind);
        });
    }
}
