using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Builds candidate dimension anchors from one already-read part topology snapshot.
/// It deliberately keeps convex-hull and bounding-box candidates distinguishable from
/// exact face/vertex evidence; downstream placement must gate those fallbacks by source.
/// </summary>
public static class DrawingPartCandidatePointBuilder
{
    /// <summary>
    /// Candidates from the contours of a whole view: every corner of every part's own
    /// contour, and every corner of the assembly outline.
    ///
    /// A view-level source, because that is what a contour is. Part contours carry their
    /// part; the assembly outline carries none, because merging boundaries destroys the
    /// ownership rather than obscuring it - see ModelObjectIds.
    ///
    /// Takes the contours rather than the outline result: what it needs is corners, and
    /// depending on the result would drag in errors and completeness it has no use for -
    /// and make it untestable without building Clipper trees by hand.
    ///
    /// Confidence is DerivedGeometry throughout. These corners are computed, not read: the
    /// union runs at a tolerance that moves boundaries, so a corner is an accurate place on
    /// the drawing without being a feature the model would name.
    /// </summary>
    public static List<DrawingPartCandidatePoint> BuildFromContours(
        IReadOnlyList<OutlineTreeNodeResult>? assemblyNodes,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>>? partNodes)
    {
        var candidates = new List<DrawingPartCandidatePoint>();

        foreach (var part in partNodes ?? new Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>())
        {
            foreach (var corner in Corners(part.Value))
            {
                candidates.Add(new DrawingPartCandidatePoint
                {
                    ModelObjectIds = [part.Key],
                    Point = [corner.X, corner.Y, 0d],
                    Source = DrawingPartCandidatePointSource.PartContour,
                    Confidence = DrawingPartCandidateConfidence.DerivedGeometry,
                    Anchor = new DrawingPartCandidateAnchor
                    {
                        ModelObjectId = part.Key,
                        Kind = DrawingPartCandidateAnchorKind.ContourVertex,
                        Id = corner.RingId + "/" + corner.IndexInRing.ToString(CultureInfo.InvariantCulture)
                    },
                    Reason = new DrawingPartCandidateReason
                    {
                        Code = "part_contour_corner",
                        ModelObjectIds = [part.Key],
                        Values = corner.Describe()
                    }
                });
            }
        }

        foreach (var corner in Corners(assemblyNodes ?? []))
        {
            candidates.Add(new DrawingPartCandidatePoint
            {
                ModelObjectIds = [],
                Point = [corner.X, corner.Y, 0d],
                Source = DrawingPartCandidatePointSource.AssemblyContour,
                Confidence = DrawingPartCandidateConfidence.DerivedGeometry,
                Anchor = new DrawingPartCandidateAnchor { Kind = DrawingPartCandidateAnchorKind.None },
                Reason = new DrawingPartCandidateReason
                {
                    Code = "assembly_contour_corner",
                    Values = corner.Describe()
                }
            });
        }

        return candidates;
    }

    /// <summary>
    /// One corner of one ring, carrying what the ring was.
    ///
    /// Whether the ring is an opening is not decoration: the outer boundary bounds the
    /// assembly and an opening does not, so a policy asking for an overall must be able to
    /// tell them apart. Flattening every corner into a bare coordinate loses that, and it
    /// cannot be recovered afterwards.
    /// </summary>
    private readonly struct ContourCorner(double x, double y, string ringId, bool isHole, int depth, int indexInRing)
    {
        public double X { get; } = x;
        public double Y { get; } = y;

        /// <summary>Identifies the ring within its part, independently of traversal order.</summary>
        public string RingId { get; } = ringId;

        public bool IsHole { get; } = isHole;
        public int Depth { get; } = depth;
        public int IndexInRing { get; } = indexInRing;

        public Dictionary<string, string> Describe() => new()
        {
            ["ring"] = RingId,
            ["isHole"] = IsHole ? "true" : "false",
            ["depth"] = Depth.ToString(CultureInfo.InvariantCulture),
            ["indexInRing"] = IndexInRing.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Every corner of every ring, outer and hole alike - an opening's edge is a place too.
    ///
    /// Each ring is walked from its own lexicographically smallest corner rather than from
    /// wherever Clipper happened to start, and is named by that corner. Clipper promises
    /// nothing about which vertex a ring begins at or in which order components come back,
    /// so an index into the raw traversal would change while the model did not - and
    /// Anchor.Key is documented as stable.
    /// </summary>
    /// <summary>Every corner of every ring, outer and hole alike - an opening's edge is a place too.</summary>
    private static IEnumerable<ContourCorner> Corners(IReadOnlyList<OutlineTreeNodeResult> nodes, int depth = 0)
    {
        foreach (var node in nodes)
        {
            var ring = node.Polygon.Where(static point => point.Length >= 2).ToList();
            if (ring.Count > 0)
            {
                var canonical = Canonical(ring);
                var ringId = (node.IsHole ? "hole:" : "outer:") + Fingerprint(canonical);

                for (var index = 0; index < canonical.Count; index++)
                {
                    yield return new ContourCorner(
                        canonical[index][0], canonical[index][1], ringId, node.IsHole, depth, index);
                }
            }

            foreach (var corner in Corners(node.Children, depth + 1))
                yield return corner;
        }
    }

    /// <summary>
    /// The ring rewritten so the same shape always reads the same way.
    ///
    /// Two things have to be pinned, not one. A ring has no natural first vertex, so it is
    /// rotated to start at its lexicographically smallest corner. It also has no natural
    /// direction: Clipper can hand back the same loop walked either way, and rotating alone
    /// would leave the minimum in place while every other index moved. So both walks are
    /// built and the smaller one wins.
    /// </summary>
    private static List<double[]> Canonical(IReadOnlyList<double[]> ring)
    {
        var forward = RotateToSmallest(ring);
        var backward = RotateToSmallest(ring.Reverse().ToList());

        return Compare(forward, backward) <= 0 ? forward : backward;
    }

    private static List<double[]> RotateToSmallest(IReadOnlyList<double[]> ring)
    {
        var start = 0;
        for (var index = 1; index < ring.Count; index++)
        {
            if (Compare(ring[index], ring[start]) < 0)
                start = index;
        }

        var rotated = new List<double[]>(ring.Count);
        for (var offset = 0; offset < ring.Count; offset++)
            rotated.Add(ring[(start + offset) % ring.Count]);

        return rotated;
    }

    private static int Compare(double[] left, double[] right)
    {
        var byX = left[0].CompareTo(right[0]);
        return byX != 0 ? byX : left[1].CompareTo(right[1]);
    }

    private static int Compare(IReadOnlyList<double[]> left, IReadOnlyList<double[]> right)
    {
        for (var index = 0; index < left.Count && index < right.Count; index++)
        {
            var order = Compare(left[index], right[index]);
            if (order != 0)
                return order;
        }

        return left.Count.CompareTo(right.Count);
    }

    /// <summary>
    /// A ring's identity, from every one of its corners at full precision.
    ///
    /// Not from the smallest corner rounded to a few decimals, which was the first attempt:
    /// two rings of one part whose minima differ by less than the rounding would collide and
    /// their corners would share keys. Round-trip formatting keeps every digit the double
    /// holds, and FNV-1a is used rather than string.GetHashCode because that one is
    /// deliberately randomised per process and would not survive a restart.
    /// </summary>
    private static string Fingerprint(IReadOnlyList<double[]> canonical)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;

        foreach (var point in canonical)
        {
            // Separated on both sides. Without a comma between them (1, 23) and (12, 3)
            // feed the hash the identical characters - not an unlikely collision but the
            // same input, every time.
            foreach (var text in new[] { point[0].ToString("R", CultureInfo.InvariantCulture),
                                         ",",
                                         point[1].ToString("R", CultureInfo.InvariantCulture),
                                         ";" })
            {
                foreach (var character in text)
                {
                    hash ^= character;
                    hash *= prime;
                }
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    public static List<DrawingPartCandidatePoint> Build(PartSolidGeometryInViewResult geometry)
    {
        var candidates = new List<DrawingPartCandidatePoint>();
        if (!geometry.Success)
            return candidates;

        AddAxisCandidates(candidates, geometry);

        if (geometry.Solid.SolidGeometryComplete)
        {
            AddFaceCandidates(candidates, geometry);
            AddVertexCandidates(candidates, geometry);
            AddHullCandidates(candidates, geometry);
        }

        AddBoundingBoxCandidates(candidates, geometry);
        return candidates;
    }

    private static void AddAxisCandidates(List<DrawingPartCandidatePoint> candidates, PartSolidGeometryInViewResult geometry)
    {
        AddCandidate(
            candidates,
            geometry.ModelId,
            geometry.StartPoint,
            DrawingPartCandidatePointSource.AxisStart,
            DrawingPartCandidateConfidence.ReferenceGeometry,
            DrawingPartCandidateAnchorKind.AxisEnd,
            "start",
            normal: null,
            "axis_start");

        AddCandidate(
            candidates,
            geometry.ModelId,
            geometry.EndPoint,
            DrawingPartCandidatePointSource.AxisEnd,
            DrawingPartCandidateConfidence.ReferenceGeometry,
            DrawingPartCandidateAnchorKind.AxisEnd,
            "end",
            normal: null,
            "axis_end");
    }

    private static void AddFaceCandidates(List<DrawingPartCandidatePoint> candidates, PartSolidGeometryInViewResult geometry)
    {
        var vertices = geometry.Solid.Vertices
            .Where(static vertex => vertex.Point.Length >= 2)
            .GroupBy(static vertex => vertex.Index)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single().Point);

        foreach (var face in geometry.Solid.Faces.OrderBy(static face => face.Index))
        {
            foreach (var edge in GetBoundaryEdges(face, vertices))
            {
                var values = new Dictionary<string, string>
                {
                    ["faceIndex"] = face.Index.ToString(CultureInfo.InvariantCulture),
                    ["loopIndex"] = edge.LoopIndex.ToString(CultureInfo.InvariantCulture),
                    ["startVertexIndex"] = edge.StartVertexIndex.ToString(CultureInfo.InvariantCulture),
                    ["endVertexIndex"] = edge.EndVertexIndex.ToString(CultureInfo.InvariantCulture)
                };

                AddCandidate(
                    candidates,
                    geometry.ModelId,
                    edge.Point,
                    DrawingPartCandidatePointSource.FaceBoundaryMidpoint,
                    DrawingPartCandidateConfidence.ExactGeometry,
                    DrawingPartCandidateAnchorKind.FaceEdge,
                    $"{face.Index.ToString(CultureInfo.InvariantCulture)}/{edge.LoopIndex.ToString(CultureInfo.InvariantCulture)}/{edge.StartVertexIndex.ToString(CultureInfo.InvariantCulture)}-{edge.EndVertexIndex.ToString(CultureInfo.InvariantCulture)}",
                    face.Normal,
                    "face_boundary_midpoint",
                    values);
            }
        }
    }

    private static void AddVertexCandidates(List<DrawingPartCandidatePoint> candidates, PartSolidGeometryInViewResult geometry)
    {
        foreach (var vertex in geometry.Solid.Vertices
                     .Where(static vertex => vertex.Point.Length >= 2)
                     .GroupBy(static vertex => vertex.Index)
                     .Where(static group => group.Count() == 1)
                     .Select(static group => group.Single())
                     .OrderBy(static vertex => vertex.Index))
        {
            AddCandidate(
                candidates,
                geometry.ModelId,
                vertex.Point,
                DrawingPartCandidatePointSource.SolidVertex,
                DrawingPartCandidateConfidence.ExactGeometry,
                DrawingPartCandidateAnchorKind.Vertex,
                vertex.Index.ToString(CultureInfo.InvariantCulture),
                normal: null,
                "solid_vertex");
        }
    }

    private static void AddHullCandidates(List<DrawingPartCandidatePoint> candidates, PartSolidGeometryInViewResult geometry)
    {
        foreach (var hullPoint in geometry.Solid.ViewHull
                     .Where(static point => point.Length >= 2)
                     .OrderBy(static point => point[0])
                     .ThenBy(static point => point[1]))
        {
            AddCandidate(
                candidates,
                geometry.ModelId,
                hullPoint,
                DrawingPartCandidatePointSource.HullVertex,
                DrawingPartCandidateConfidence.DerivedGeometry,
                DrawingPartCandidateAnchorKind.HullVertex,
                $"{FormatCoordinate(hullPoint[0])},{FormatCoordinate(hullPoint[1])}",
                normal: null,
                "convex_hull_vertex");
        }
    }

    private static void AddBoundingBoxCandidates(List<DrawingPartCandidatePoint> candidates, PartSolidGeometryInViewResult geometry)
    {
        var min = geometry.Solid.BboxMin;
        var max = geometry.Solid.BboxMax;
        if (min.Length < 2 || max.Length < 2)
            return;

        var z = MidpointCoordinate(min, max, 2);
        AddBoundingBoxCorner(candidates, geometry.ModelId, "min_x_min_y", min[0], min[1], z);
        AddBoundingBoxCorner(candidates, geometry.ModelId, "max_x_min_y", max[0], min[1], z);
        AddBoundingBoxCorner(candidates, geometry.ModelId, "min_x_max_y", min[0], max[1], z);
        AddBoundingBoxCorner(candidates, geometry.ModelId, "max_x_max_y", max[0], max[1], z);
    }

    private static void AddBoundingBoxCorner(List<DrawingPartCandidatePoint> candidates, int modelId, string id, double x, double y, double z)
    {
        AddCandidate(
            candidates,
            modelId,
            [x, y, z],
            DrawingPartCandidatePointSource.BoundingBoxCorner,
            DrawingPartCandidateConfidence.BoundingBoxFallback,
            DrawingPartCandidateAnchorKind.BoundingBoxCorner,
            id,
            normal: null,
            "bounding_box_corner");
    }

    private static IEnumerable<FaceBoundaryEdge> GetBoundaryEdges(
        PartFaceGeometry face,
        IReadOnlyDictionary<int, double[]> vertices)
    {
        var edges = new List<FaceBoundaryEdge>();
        foreach (var loop in face.Loops.OrderBy(static loop => loop.Index))
        {
            var indexes = loop.VertexIndexes;
            if (indexes.Count < 2)
                continue;

            for (var index = 0; index < indexes.Count; index++)
            {
                var firstIndex = indexes[index];
                var secondIndex = indexes[(index + 1) % indexes.Count];
                if (!vertices.TryGetValue(firstIndex, out var first)
                    || !vertices.TryGetValue(secondIndex, out var second)
                    || SamePoint(first, second))
                    continue;

                var startIndex = Math.Min(firstIndex, secondIndex);
                var endIndex = Math.Max(firstIndex, secondIndex);
                if (!vertices.TryGetValue(startIndex, out var start)
                    || !vertices.TryGetValue(endIndex, out var end)
                    || SamePoint(start, end))
                    continue;

                edges.Add(new FaceBoundaryEdge(
                    loop.Index,
                    startIndex,
                    endIndex,
                    [
                        MidpointCoordinate(start, end, 0),
                        MidpointCoordinate(start, end, 1),
                        MidpointCoordinate(start, end, 2)
                    ]));
            }
        }

        return edges
            .OrderBy(static edge => edge.LoopIndex)
            .ThenBy(static edge => edge.StartVertexIndex)
            .ThenBy(static edge => edge.EndVertexIndex);
    }

    private static bool SamePoint(double[] first, double[] second) =>
        SameCoordinate(first, second, 0)
        && SameCoordinate(first, second, 1)
        && SameCoordinate(first, second, 2);

    private static bool SameCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : 0.0;
        return Math.Abs(firstValue - secondValue) < 1e-9;
    }

    private static double MidpointCoordinate(double[] first, double[] second, int index)
    {
        var firstValue = first.Length > index ? first[index] : 0.0;
        var secondValue = second.Length > index ? second[index] : firstValue;
        return (firstValue + secondValue) / 2.0;
    }

    private static string FormatCoordinate(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static void AddCandidate(
        List<DrawingPartCandidatePoint> candidates,
        int modelId,
        double[] point,
        DrawingPartCandidatePointSource source,
        DrawingPartCandidateConfidence confidence,
        DrawingPartCandidateAnchorKind anchorKind,
        string anchorId,
        double[]? normal,
        string reasonCode,
        Dictionary<string, string>? values = null)
    {
        if (point.Length < 2)
            return;

        candidates.Add(new DrawingPartCandidatePoint
        {
            ModelObjectIds = [modelId],
            Point = [.. point],
            Source = source,
            Normal = normal is { Length: > 0 } ? [.. normal] : null,
            InPlaneNormal = ProjectToViewPlane(normal),
            Confidence = confidence,
            Anchor = new DrawingPartCandidateAnchor
            {
                ModelObjectId = modelId,
                Kind = anchorKind,
                Id = anchorId
            },
            Reason = new DrawingPartCandidateReason
            {
                Code = reasonCode,
                ModelObjectIds = [modelId],
                Values = values ?? new Dictionary<string, string>()
            }
        });
    }

    private static double[]? ProjectToViewPlane(double[]? normal)
    {
        if (normal is not { Length: >= 2 })
            return null;

        var length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1]);
        return length < 1e-9 ? null : [normal[0] / length, normal[1] / length];
    }

    private sealed class FaceBoundaryEdge
    {
        public FaceBoundaryEdge(int loopIndex, int startVertexIndex, int endVertexIndex, double[] point)
        {
            LoopIndex = loopIndex;
            StartVertexIndex = startVertexIndex;
            EndVertexIndex = endVertexIndex;
            Point = point;
        }

        public int LoopIndex { get; }
        public int StartVertexIndex { get; }
        public int EndVertexIndex { get; }
        public double[] Point { get; }
    }
}
