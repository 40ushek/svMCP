using System;
using System.Collections.Generic;
using Tekla.Structures.Geometry3d;
using SolidTypes = Tekla.Structures.Solid;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Canonical view-local solid vertex snapshot shared by raw and topology DTOs.
/// </summary>
internal static class SolidViewVertexCollector
{
    public static SolidViewGeometrySnapshot Collect(Tekla.Structures.Model.Solid solid) =>
        CollectGeometry(solid, tolerateTraversalErrors: true);

    public static SolidViewGeometrySnapshot CollectGeometry(
        Tekla.Structures.Model.Solid solid,
        bool tolerateTraversalErrors)
    {
        var result = new SolidViewGeometrySnapshot();

        try
        {
            var faceEnumerator = solid.GetFaceEnumerator();
            while (faceEnumerator.MoveNext())
            {
                if (faceEnumerator.Current is not SolidTypes.Face face)
                    continue;

                var faceSnapshot = new SolidViewFaceSnapshot
                {
                    Normal = ToNullableArray(face.Normal)
                };

                var loopEnumerator = face.GetLoopEnumerator();
                while (loopEnumerator.MoveNext())
                {
                    if (loopEnumerator.Current is not SolidTypes.Loop loop)
                        continue;

                    var loopIndexes = new List<int>();
                    if (loop.GetVertexEnumerator() is SolidTypes.VertexEnumerator vertexEnumerator)
                    {
                        while (vertexEnumerator.MoveNext())
                        {
                            if (vertexEnumerator.Current is not Point vertex)
                                continue;

                            loopIndexes.Add(GetOrAddPoint(result.Vertices, vertex));
                        }
                    }

                    faceSnapshot.Loops.Add(loopIndexes);
                }

                result.Faces.Add(faceSnapshot);
            }

            result.IsComplete = true;
        }
        catch when (tolerateTraversalErrors)
        {
            // Some runtime solids may not expose stable face/loop traversal.
        }

        return result;
    }

    private static int GetOrAddPoint(List<double[]> target, Point point)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (SamePoint(target[i], point))
                return i;
        }

        target.Add([Round(point.X), Round(point.Y), Round(point.Z)]);
        return target.Count - 1;
    }

    private static bool SamePoint(double[] point, Point candidate)
    {
        const double epsilon = 0.0001;
        if (point.Length < 3)
            return false;

        return Math.Abs(point[0] - candidate.X) <= epsilon
            && Math.Abs(point[1] - candidate.Y) <= epsilon
            && Math.Abs(point[2] - candidate.Z) <= epsilon;
    }

    private static double Round(double value) => Math.Round(value, 5);

    private static double[]? ToNullableArray(Vector? vector) =>
        vector == null ? null : [vector.X, vector.Y, vector.Z];
}

internal sealed class SolidViewGeometrySnapshot
{
    public List<double[]> Vertices { get; } = new();
    public List<SolidViewFaceSnapshot> Faces { get; } = new();
    public bool IsComplete { get; set; }
}

internal sealed class SolidViewFaceSnapshot
{
    public double[]? Normal { get; set; }
    public List<List<int>> Loops { get; } = new();
}
