using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingPartCandidatePointBuilderTests
{
    [Fact]
    public void Build_EmitsFaceEdgeMidpointsWithEdgeAnchorAndNormal()
    {
        var candidates = DrawingPartCandidatePointBuilder.Build(CreateCompleteGeometry());

        var candidate = Assert.Single(
            candidates,
            candidate => candidate.Source == DrawingPartCandidatePointSource.FaceBoundaryMidpoint
                && candidate.Anchor.Id == "3/0/4-9");

        Assert.Equal([5d, 0d, 0d], candidate.Point);
        Assert.Equal([0d, -1d, 0d], Assert.IsType<double[]>(candidate.Normal));
        Assert.Equal([0d, -1d], Assert.IsType<double[]>(candidate.InPlaneNormal));
        Assert.Equal(DrawingPartCandidateConfidence.ExactGeometry, candidate.Confidence);
        Assert.Equal(DrawingPartCandidateAnchorKind.FaceEdge, candidate.Anchor.Kind);
        Assert.Equal("42:FaceEdge:3/0/4-9", candidate.Anchor.Key);
        Assert.Equal("face_boundary_midpoint", candidate.Reason.Code);
        Assert.Equal("4", candidate.Reason.Values["startVertexIndex"]);
        Assert.Equal("9", candidate.Reason.Values["endVertexIndex"]);
    }

    [Fact]
    public void Build_EmitsVerticesWithStableAnchorsAndUnknownNormals()
    {
        var candidates = DrawingPartCandidatePointBuilder.Build(CreateCompleteGeometry());

        var vertex = Assert.Single(
            candidates,
            candidate => candidate.Source == DrawingPartCandidatePointSource.SolidVertex
                && candidate.Anchor.Id == "9");

        Assert.Equal([10d, 0d, 0d], vertex.Point);
        Assert.Null(vertex.Normal);
        Assert.Null(vertex.InPlaneNormal);
        Assert.Equal("42:Vertex:9", vertex.Anchor.Key);
    }

    [Fact]
    public void Build_DoesNotUsePartialSolidForFaceVertexOrHullCandidates()
    {
        var geometry = CreateCompleteGeometry();
        geometry.Solid.SolidGeometryComplete = false;

        var candidates = DrawingPartCandidatePointBuilder.Build(geometry);

        Assert.DoesNotContain(candidates, candidate => candidate.Source is
            DrawingPartCandidatePointSource.FaceBoundaryMidpoint or
            DrawingPartCandidatePointSource.SolidVertex or
            DrawingPartCandidatePointSource.HullVertex);

        Assert.Contains(candidates, candidate => candidate.Source == DrawingPartCandidatePointSource.AxisStart);
        var corners = candidates.Where(candidate => candidate.Source == DrawingPartCandidatePointSource.BoundingBoxCorner).ToList();
        Assert.Equal(4, corners.Count);
        Assert.All(corners, corner =>
        {
            Assert.Equal(DrawingPartCandidateConfidence.BoundingBoxFallback, corner.Confidence);
            Assert.Null(corner.Normal);
            Assert.Null(corner.InPlaneNormal);
        });
    }

    [Fact]
    public void Build_MarksHullCandidatesAsDerivedGeometry()
    {
        var candidates = DrawingPartCandidatePointBuilder.Build(CreateCompleteGeometry());

        var hull = Assert.Single(
            candidates,
            candidate => candidate.Source == DrawingPartCandidatePointSource.HullVertex
                && candidate.Anchor.Id == "10,0");

        Assert.Equal(DrawingPartCandidateConfidence.DerivedGeometry, hull.Confidence);
        Assert.Equal("42:HullVertex:10,0", hull.Anchor.Key);
    }

    [Fact]
    public void Build_PreservesAnchorKeysWhenTraversalCollectionsAreReordered()
    {
        var first = DrawingPartCandidatePointBuilder.Build(CreateCompleteGeometry());
        var reordered = CreateCompleteGeometry();
        reordered.Solid.Vertices.Reverse();
        reordered.Solid.Faces.Reverse();
        var firstFace = Assert.Single(reordered.Solid.Faces, face => face.Index == 3);
        firstFace.Loops.Reverse();
        firstFace.Loops.Single(loop => loop.Index == 0).VertexIndexes = [12, 9, 4];
        reordered.Solid.ViewHull.Reverse();
        var second = DrawingPartCandidatePointBuilder.Build(reordered);

        Assert.Equal(
            first.Select(candidate => candidate.Anchor.Key),
            second.Select(candidate => candidate.Anchor.Key));
    }

    [Fact]
    public void Build_LeavesInPlaneNormalUnknownForFrontOrBackFace()
    {
        var geometry = CreateCompleteGeometry();
        Assert.Single(geometry.Solid.Faces, face => face.Index == 3).Normal = [0d, 0d, 1d];

        var candidate = Assert.Single(
            DrawingPartCandidatePointBuilder.Build(geometry),
            candidate => candidate.Anchor.Id == "3/0/4-9");

        Assert.Equal([0d, 0d, 1d], Assert.IsType<double[]>(candidate.Normal));
        Assert.Null(candidate.InPlaneNormal);
    }

    [Fact]
    public void Build_SkipsAmbiguousDuplicateVertexIndexes()
    {
        var geometry = CreateCompleteGeometry();
        geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = 4, Point = [100d, 100d, 0d] });

        var candidates = DrawingPartCandidatePointBuilder.Build(geometry);

        Assert.DoesNotContain(candidates, candidate => candidate.Anchor.Key == "42:Vertex:4");
        Assert.DoesNotContain(candidates, candidate => candidate.Anchor.Id == "3/0/4-9");
    }

    private static PartSolidGeometryInViewResult CreateCompleteGeometry() =>
        new()
        {
            Success = true,
            ViewId = 7,
            ModelId = 42,
            StartPoint = [0d, 0d, 0d],
            EndPoint = [10d, 0d, 0d],
            Solid = new PartSolidGeometry
            {
                BboxMin = [0d, 0d, 0d],
                BboxMax = [10d, 5d, 2d],
                SolidGeometryComplete = true,
                Vertices =
                [
                    new PartVertexGeometry { Index = 4, Point = [0d, 0d, 0d] },
                    new PartVertexGeometry { Index = 9, Point = [10d, 0d, 0d] },
                    new PartVertexGeometry { Index = 12, Point = [10d, 5d, 0d] }
                ],
                Faces =
                [
                    new PartFaceGeometry
                    {
                        Index = 3,
                        Normal = [0d, -1d, 0d],
                        Loops =
                        [
                            new PartLoopGeometry
                            {
                                Index = 0,
                                VertexIndexes = [4, 9, 12]
                            },
                            new PartLoopGeometry
                            {
                                Index = 2,
                                VertexIndexes = [4, 12, 9]
                            }
                        ]
                    },
                    new PartFaceGeometry
                    {
                        Index = 8,
                        Normal = [1d, 0d, 0d],
                        Loops =
                        [
                            new PartLoopGeometry
                            {
                                Index = 0,
                                VertexIndexes = [9, 12, 4]
                            }
                        ]
                    }
                ],
                ViewHull = [[0d, 0d], [10d, 0d], [10d, 5d]]
            }
        };
}
