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
