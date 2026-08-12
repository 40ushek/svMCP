using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Candidates from contours: the real corners of each part and of the assembly, including
/// the ones a bounding box or a convex hull would flatten away.
/// </summary>
public sealed class ContourCandidatePointTests
{
    private static OutlineTreeNodeResult Ring(bool hole, params (double X, double Y)[] points) => new()
    {
        IsHole = hole,
        Polygon = points.Select(point => new[] { point.X, point.Y }).ToList()
    };

    [Fact]
    public void APartContourCornerNamesItsPart()
    {
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] = [Ring(false, (0, 0), (100, 0), (100, 50), (0, 50))]
            });

        Assert.Equal(4, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal([11], candidate.ModelObjectIds);
            Assert.Equal(DrawingPartCandidatePointSource.PartContour, candidate.Source);
            Assert.Equal(DrawingPartCandidateConfidence.DerivedGeometry, candidate.Confidence);
            Assert.Equal(DrawingPartCandidateAnchorKind.ContourVertex, candidate.Anchor.Kind);
        });
    }

    [Fact]
    public void AnAssemblyContourCornerNamesNoPart()
    {
        // Merging boundaries destroys the ownership rather than obscuring it, so there is
        // nothing to name and nothing to look up.
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [Ring(false, (0, 0), (200, 0), (200, 50), (0, 50))],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>());

        Assert.Equal(4, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Empty(candidate.ModelObjectIds);
            Assert.Equal(DrawingPartCandidatePointSource.AssemblyContour, candidate.Source);
            Assert.Equal(DrawingPartCandidateAnchorKind.None, candidate.Anchor.Kind);
        });
    }

    [Fact]
    public void ContourCornersAreNotPassedOffAsSolidVertices()
    {
        // After the union a corner need not be a vertex of the solid at all - cut ends and
        // merged coplanar faces produce corners the model never had.
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [Ring(false, (0, 0), (10, 0), (10, 10), (0, 10))],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] = [Ring(false, (0, 0), (10, 0), (10, 10), (0, 10))]
            });

        Assert.DoesNotContain(candidates, candidate =>
            candidate.Source == DrawingPartCandidatePointSource.SolidVertex ||
            candidate.Anchor.Kind == DrawingPartCandidateAnchorKind.Vertex);
    }

    [Fact]
    public void AnOpeningsEdgeIsAPlaceToo()
    {
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] =
                [
                    new OutlineTreeNodeResult
                    {
                        IsHole = false,
                        Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]],
                        Children = [Ring(true, (40, 40), (60, 40), (60, 60), (40, 60))]
                    }
                ]
            });

        Assert.Equal(8, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.Point[0] == 40 && candidate.Point[1] == 40);
    }

    [Fact]
    public void ACornerRemembersWhetherItsRingWasAnOpening()
    {
        // The outer boundary bounds the assembly and an opening does not, so a policy
        // asking for an overall has to tell them apart. Flattened to bare coordinates it
        // cannot be recovered afterwards.
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] =
                [
                    new OutlineTreeNodeResult
                    {
                        IsHole = false,
                        Polygon = [[0, 0], [100, 0], [100, 100], [0, 100]],
                        Children = [Ring(true, (40, 40), (60, 40), (60, 60), (40, 60))]
                    }
                ]
            });

        var outer = candidates.Where(candidate => candidate.Reason.Values["isHole"] == "false").ToList();
        var hole = candidates.Where(candidate => candidate.Reason.Values["isHole"] == "true").ToList();

        Assert.Equal(4, outer.Count);
        Assert.Equal(4, hole.Count);
        Assert.All(hole, candidate => Assert.Equal("1", candidate.Reason.Values["depth"]));
        Assert.All(outer, candidate => Assert.StartsWith("outer:", candidate.Reason.Values["ring"]));
        Assert.All(hole, candidate => Assert.StartsWith("hole:", candidate.Reason.Values["ring"]));
    }

    [Fact]
    public void TheAnchorDoesNotMoveWhenClipperStartsTheRingElsewhere()
    {
        // Clipper promises nothing about which vertex a ring begins at, so an index into
        // the raw traversal would change while the model did not - and Anchor.Key is
        // documented as stable.
        var square = new (double X, double Y)[] { (0, 0), (100, 0), (100, 50), (0, 50) };
        var rotated = new (double X, double Y)[] { (100, 50), (0, 50), (0, 0), (100, 0) };

        var first = DrawingPartCandidatePointBuilder.BuildFromContours(
            [], new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [11] = [Ring(false, square)] });
        var second = DrawingPartCandidatePointBuilder.BuildFromContours(
            [], new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [11] = [Ring(false, rotated)] });

        static Dictionary<string, string> KeyByPoint(List<DrawingPartCandidatePoint> candidates) =>
            candidates.ToDictionary(
                candidate => $"{candidate.Point[0]},{candidate.Point[1]}",
                candidate => candidate.Anchor.Key);

        Assert.Equal(KeyByPoint(first), KeyByPoint(second));
    }

    [Fact]
    public void TheAnchorDoesNotMoveWhenClipperWalksTheRingBackwards()
    {
        // Rotating alone would leave the smallest corner in place and move every other
        // index, so the direction has to be pinned too.
        var forward = new (double X, double Y)[] { (0, 0), (100, 0), (100, 50), (0, 50) };
        var backward = forward.Reverse().ToArray();

        var first = DrawingPartCandidatePointBuilder.BuildFromContours(
            [], new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [11] = [Ring(false, forward)] });
        var second = DrawingPartCandidatePointBuilder.BuildFromContours(
            [], new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> { [11] = [Ring(false, backward)] });

        static Dictionary<string, string> KeyByPoint(List<DrawingPartCandidatePoint> candidates) =>
            candidates.ToDictionary(
                candidate => $"{candidate.Point[0]},{candidate.Point[1]}",
                candidate => candidate.Anchor.Key);

        Assert.Equal(KeyByPoint(first), KeyByPoint(second));
    }

    [Fact]
    public void TwoRingsAlmostSharingACornerAreStillToldApart()
    {
        // The first attempt named a ring by its smallest corner rounded to three decimals,
        // so rings whose minima differ by less than that collided and their corners shared
        // keys.
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] =
                [
                    Ring(false, (0, 0), (100, 0), (100, 50), (0, 50)),
                    Ring(false, (0.0001, 0), (200, 0), (200, 50), (0.0001, 50))
                ]
            });

        var rings = candidates.Select(candidate => candidate.Reason.Values["ring"]).Distinct().ToList();

        Assert.Equal(2, rings.Count);
        Assert.Equal(8, candidates.Select(candidate => candidate.Anchor.Key).Distinct().Count());
    }

    [Fact]
    public void APointWithNoAnchorHasNoKey()
    {
        // "0:None:" would read as an identity, and every assembly-contour point would share
        // it.
        var candidates = DrawingPartCandidatePointBuilder.BuildFromContours(
            [Ring(false, (0, 0), (10, 0), (10, 10), (0, 10))],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>());

        Assert.All(candidates, candidate => Assert.Equal(string.Empty, candidate.Anchor.Key));
    }

    [Fact]
    public void RingsThatDifferOnlyInWhereTheDigitsSplitAreStillToldApart()
    {
        // Without a separator between a corner's two numbers, (1, 23) and (12, 3) hand the
        // hash the same characters - the same input, not a rare collision.
        var first = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] = [Ring(false, (1, 23), (100, 0), (100, 50))]
            });

        var second = DrawingPartCandidatePointBuilder.BuildFromContours(
            [],
            new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>
            {
                [11] = [Ring(false, (12, 3), (100, 0), (100, 50))]
            });

        Assert.NotEqual(
            first[0].Reason.Values["ring"],
            second[0].Reason.Values["ring"]);
    }
}
