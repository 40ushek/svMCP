using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The analyzer answers one question per part per axis - does a load-bearing contact
/// remove it - from the contact's own plane normal, not from the shape it happened to
/// flatten to. Most tests build a <see cref="ContactShapeInView"/> directly with a chosen
/// kind, state and normal: since those three now travel on the shape itself (see
/// <c>ContactShapeInView</c>'s remarks on <c>ContactId</c> not being a safe lookup key),
/// nothing here needs a real <see cref="ContactGraph"/>. One integration test at the bottom
/// runs the real pipeline to check the public overload is wired to it correctly.
/// </summary>
public sealed class PartDegreesOfFreedomAnalyzerTests
{
    private static ContactShapeInView Shape(
        int a, int b, Vec3 normal, params (double X, double Y)[] points) =>
        Shape(a, b, normal, ContactKind.FaceToFace, ContactState.Touching, points);

    private static ContactShapeInView Shape(
        int a, int b, Vec3 normal, ContactKind kind, ContactState state, params (double X, double Y)[] points)
    {
        var flattened = RegionFlattener.Flatten(points.Select(p => new Vec3(p.X, p.Y, 0)).ToList());
        return new ContactShapeInView(
            contactId: $"{a}-{b}-{flattened.Id}", new ContactParticipants(a.ToString(), b.ToString()),
            kind, state, normal, flattened);
    }

    private static ViewContactGeometryResult Geometry(
        IReadOnlyList<ContactShapeInView> shapes, IReadOnlyList<UnreadPart>? unread = null,
        bool searchComplete = true, string? error = null) =>
        new(1, shapes, [], unread ?? [], searchComplete, error);

    private static ViewPartFreedomResult Build(
        IReadOnlyList<int> requestedIds, ViewContactGeometryResult geometry,
        IReadOnlyList<SearchFailure>? failures = null) =>
        PartDegreesOfFreedomAnalyzer.Build(1, failures ?? [], requestedIds, geometry);

    private static PartDegreesOfFreedom Of(ViewPartFreedomResult result, int modelId) =>
        result.Parts.Single(part => part.ModelId == modelId);

    // A face whose normal points along X resists motion in X and leaves Y - the slide along
    // the face. The flattened shape's own outline no longer matters to the reading, so an
    // arbitrary vertical segment is used throughout to stand for "some real patch".
    private static readonly Vec3 NormalAlongX = new(1, 0, 0);
    private static readonly Vec3 NormalAlongY = new(0, 1, 0);
    private static readonly Vec3 NormalOutOfPlane = new(0, 0, 1);
    private static readonly Vec3 NormalDiagonal = new(1, 1, 0);

    [Fact]
    public void ANormalAlongXRemovesXAndLeavesYFree()
    {
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.False(Of(result, 1).FreeX);
        Assert.True(Of(result, 1).FreeY);
        Assert.False(Of(result, 2).FreeX);
        Assert.True(Of(result, 2).FreeY);
    }

    [Fact]
    public void ANormalAlongYRemovesYAndLeavesXFree()
    {
        var shape = Shape(1, 2, NormalAlongY, (0, 0), (100, 0));
        var result = Build([1, 2], Geometry([shape]));

        Assert.True(Of(result, 1).FreeX);
        Assert.False(Of(result, 1).FreeY);
    }

    [Fact]
    public void APartTouchedFromTwoDirectionsIsFixedOnBothAxes()
    {
        // A stud on a bottom plate (normal Y) and butted by a side noggin (normal X): the
        // two contacts remove different axes, and together leave nothing free.
        var onPlate = Shape(1, 2, NormalAlongY, (0, 0), (60, 0));
        var bySide = Shape(1, 3, NormalAlongX, (0, 0), (0, 200));
        var result = Build([1, 2, 3], Geometry([onPlate, bySide]));

        var stud = Of(result, 1);
        Assert.False(stud.FreeX);
        Assert.False(stud.FreeY);
        Assert.Equal(3, Assert.Single(stud.ConstrainedByX).PartnerModelId);
        Assert.Equal(2, Assert.Single(stud.ConstrainedByY).PartnerModelId);
    }

    [Fact]
    public void ANormalOutOfTheViewPlaneConstrainsNeitherInPlaneAxis()
    {
        // A face-on contact - two flat plates seen from directly above - resists depth, not
        // sliding across the sheet. This is the case a shape-shaped reading got wrong: a
        // narrow polygon here (an accident of how the patch happened to flatten) used to be
        // read as a clean vertical support and remove X for nothing physical.
        var narrowPolygon = Shape(1, 2, NormalOutOfPlane, (0, 0), (0.2, 0), (0.2, 500), (0, 500));
        var result = Build([1, 2], Geometry([narrowPolygon]));

        Assert.True(Of(result, 1).FreeX);
        Assert.True(Of(result, 1).FreeY);
        Assert.Single(result.AmbiguousDirection);
        Assert.Empty(result.NotLoadBearing);
    }

    [Fact]
    public void ADiagonalInPlaneNormalIsNotForcedOntoAnAxis()
    {
        var shape = Shape(1, 2, NormalDiagonal, (0, 0), (100, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.True(Of(result, 1).FreeX);
        Assert.True(Of(result, 1).FreeY);
        Assert.Single(result.AmbiguousDirection);
    }

    [Theory]
    [InlineData(ContactKind.FaceToEdge)]
    [InlineData(ContactKind.EdgeToEdge)]
    public void OnlyFaceToFaceCarriesSupport(ContactKind kind)
    {
        // SolidContacts' own words: FaceToFace is "the only kind that carries real support".
        // A normal that would otherwise remove X must not, when the touch is an edge one.
        var shape = Shape(1, 2, NormalAlongX, kind, ContactState.Touching, (0, 0), (0, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.True(Of(result, 1).FreeX);
        Assert.True(Of(result, 1).FreeY);
        Assert.Single(result.NotLoadBearing);
        Assert.Empty(result.AmbiguousDirection);
    }

    [Theory]
    [InlineData(ContactState.Gap)]
    [InlineData(ContactState.Overlap)]
    public void OnlyTouchingCountsAsSettled(ContactState state)
    {
        // A gap inside the search's broad tolerance is not zero, and an overlap is a
        // modelling error - neither is a position two parts have actually settled into.
        var shape = Shape(1, 2, NormalAlongX, ContactKind.FaceToFace, state, (0, 0), (0, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.True(Of(result, 1).FreeX);
        Assert.Single(result.NotLoadBearing);
    }

    [Fact]
    public void APartTouchingNothingIsFreeOnBothAxes()
    {
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var result = Build([1, 2, 9], Geometry([shape]));

        var isolated = Of(result, 9);
        Assert.True(isolated.FreeX);
        Assert.True(isolated.FreeY);
        Assert.Empty(isolated.ConstrainedByX);
    }

    [Fact]
    public void AnUnresolvedParticipantAttributesToNobody()
    {
        var flattened = RegionFlattener.Flatten([new Vec3(0, 0, 0), new Vec3(0, 100, 0)]);
        var unresolved = new ContactShapeInView(
            "1-x", new ContactParticipants("1", "not-a-model-id"),
            ContactKind.FaceToFace, ContactState.Touching, NormalAlongX, flattened);

        var result = Build([1], Geometry([unresolved]));

        Assert.True(Of(result, 1).FreeX);
        Assert.Empty(result.AmbiguousDirection);
        Assert.Empty(result.NotLoadBearing);
    }

    [Fact]
    public void RotationIsAlwaysUnanswered()
    {
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.Null(Of(result, 1).FreeRotation);
    }

    [Fact]
    public void AnErrorIsReportedAndNothingIsGuessed()
    {
        var contacts = new ViewContactsResult(
            1, ContactGraph.Build([]), [], "no drawing is open", requestedIds: [1]);
        var geometry = Geometry([]);

        var result = PartDegreesOfFreedomAnalyzer.Build(contacts, geometry);

        Assert.Equal("no drawing is open", result.Error);
        Assert.Empty(result.Parts);
        Assert.False(result.IsComplete);
        Assert.Null(result.AllPartsHaveObservedAxisContacts);
    }

    [Fact]
    public void AnUnreadPartMakesTheReadIncompleteWithoutHidingWhatWasFound()
    {
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var unread = new UnreadPart(3, "geometry read failed");
        var geometry = Geometry([shape], unread: [unread], searchComplete: false);

        var result = Build([1, 2, 3], geometry);

        Assert.False(result.IsComplete);
        Assert.Single(result.Unread);
        Assert.Null(result.AllPartsHaveObservedAxisContacts);
        // What was found is still reported - an incomplete read is not an empty one.
        Assert.False(Of(result, 1).FreeX);
    }

    [Fact]
    public void ASearchFailureMakesTheReadIncompleteEvenWhenGeometryClaimsItIsNot()
    {
        // failures is its own parameter precisely so a geometry that reads complete cannot
        // paper over a pair the search itself threw on - geometry.IsComplete alone must not
        // be trusted blindly here, or a caller assembling the two independently could get
        // isComplete=true and a non-null verdict out of a read that quietly missed a pair.
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var geometry = Geometry([shape], searchComplete: true);
        var failure = new SearchFailure("1", "9", new InvalidOperationException("solid threw"));

        var result = Build([1, 2], geometry, failures: [failure]);

        Assert.False(result.IsComplete);
        Assert.Single(result.Failures);
        Assert.Null(result.AllPartsHaveObservedAxisContacts);
    }

    [Fact]
    public void MismatchedViewIdsAreRejectedRatherThanSilentlyMixed()
    {
        var contacts = new ViewContactsResult(1, ContactGraph.Build([]), [], requestedIds: [1]);
        var geometryForAnotherView = new ViewContactGeometryResult(2, [], [], [], searchComplete: true);

        Assert.Throws<ArgumentException>(
            () => PartDegreesOfFreedomAnalyzer.Build(contacts, geometryForAnotherView));
    }

    [Fact]
    public void ThisFlagIsFalseWhileAnyPartHasAFreeAxis()
    {
        var shape = Shape(1, 2, NormalAlongX, (0, 0), (0, 100));
        var result = Build([1, 2], Geometry([shape]));

        Assert.False(result.AllPartsHaveObservedAxisContacts);
    }

    [Fact]
    public void ThisFlagIsTrueOnlyWhenEveryPartIsFixedOnBothAxesLocally()
    {
        var onPlate = Shape(1, 2, NormalAlongY, (0, 0), (60, 0));
        var bySide = Shape(1, 3, NormalAlongX, (0, 0), (0, 200));
        var result = Build([1], Geometry([onPlate, bySide]));

        Assert.True(result.AllPartsHaveObservedAxisContacts);
    }

    [Fact]
    public void ThisFlagIsFalseWithNoParts()
    {
        var result = Build([], Geometry([]));

        Assert.Empty(result.Parts);
        Assert.False(result.AllPartsHaveObservedAxisContacts);
    }

    // --- Integration: the public overload against a real contact search ---

    private static PartSolidGeometryInViewResult EdgewiseSlab(int modelId, double x0, double x1)
    {
        var result = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = modelId };
        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 100), (0, 100) };
        var index = 0;

        // Swept along view X so the two slabs meet on a face the view looks along - a
        // segment in the view plane, whose normal ends up close to X.
        foreach (var depth in new[] { x0, x1 })
            foreach (var (y, z) in corners)
                result.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [depth, y, z] });

        var near = new PartFaceGeometry { Index = 0, Normal = [-1, 0, 0] };
        near.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });
        var far = new PartFaceGeometry { Index = 1, Normal = [1, 0, 0] };
        far.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [4, 5, 6, 7] });
        result.Solid.Faces.Add(near);
        result.Solid.Faces.Add(far);

        return result;
    }

    private sealed class Reader : IDrawingPartSolidGeometryApi
    {
        private readonly Dictionary<int, PartSolidGeometryInViewResult> _byId = new();
        public Reader Returns(int modelId, PartSolidGeometryInViewResult result) { _byId[modelId] = result; return this; }
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => _byId[modelId];
    }

    [Fact]
    public void ThePublicOverloadRunsTheRealPipelineAndClassifiesAFaceToFaceTouch()
    {
        var reader = new Reader()
            .Returns(10, EdgewiseSlab(10, 0, 50))
            .Returns(11, EdgewiseSlab(11, 50, 100));

        var contacts = TeklaDrawingViewContactApi.Build(1, [10, 11], reader, new ContactOptions());
        var geometry = ContactGeometryInViewBuilder.Build(contacts);

        var result = PartDegreesOfFreedomAnalyzer.Build(contacts, geometry);

        Assert.True(result.IsComplete);
        Assert.Empty(result.NotLoadBearing);
        Assert.Empty(result.AmbiguousDirection);
        // Two flat slabs pushed together along X remove exactly that axis.
        var a = Of(result, 10);
        Assert.False(a.FreeX);
        Assert.True(a.FreeY);
    }
}
