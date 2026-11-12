using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Cuts a solid by the plane of a section view: the contour of the cut is what the section
/// draws of a part running through it. Found on M.46 section F: a gusset running through the
/// cut had one small notch face wholly inside the view depth and was taken for the whole part.
/// </summary>
internal static class SolidDepthClip
{
    private sealed class Builder
    {
        public List<PartVertexGeometry> Vertices { get; } = new();
        public List<PartFaceGeometry> Faces { get; } = new();
        public int FirstIndex { get; set; }

        public void AddFace(double[]? normal, IEnumerable<IReadOnlyList<double[]>> loops)
        {
            var face = new PartFaceGeometry { Index = Faces.Count, Normal = normal };
            foreach (var loop in loops)
            {
                var indexes = new List<int>();
                foreach (var point in loop)
                {
                    indexes.Add(FirstIndex + Vertices.Count);
                    Vertices.Add(new PartVertexGeometry { Index = FirstIndex + Vertices.Count, Point = point });
                }
                face.Loops.Add(new PartLoopGeometry { Index = face.Loops.Count, VertexIndexes = indexes });
            }
            Faces.Add(face);
        }
    }

    /// <summary>
    /// The section of the body by the plane, as faces lying in it. Null when the plane misses
    /// the body or the cut edges do not close into contours. Its vertices are numbered from
    /// firstIndex, so they can stand beside the body's own.
    /// </summary>
    public static PartSolidGeometry? Section(PartSolidGeometry solid, double plane, int firstIndex = 0)
    {
        var vertices = solid.Vertices.Where(vertex => vertex.Point.Length >= 3)
            .GroupBy(vertex => vertex.Index).ToDictionary(group => group.Key, group => group.First().Point);
        if (solid.Faces.Any(face => face.Loops.Any(loop => loop.VertexIndexes.Any(index => !vertices.ContainsKey(index)))))
            return null;
        if (CrossSection(solid, vertices, plane) is not { Count: > 0 } section) return null;

        var builder = new Builder { FirstIndex = firstIndex };
        foreach (var region in section)
            builder.AddFace([0, 0, 1], region);
        return new PartSolidGeometry {
            BboxMin = solid.BboxMin, BboxMax = solid.BboxMax, ViewHull = solid.ViewHull,
            SolidGeometryComplete = solid.SolidGeometryComplete,
            Vertices = builder.Vertices, Faces = builder.Faces
        };
    }

    /// <summary>The area a face covers on the view plane: its outer loop less its holes.</summary>
    public static double ProjectedArea(PartSolidGeometry solid, PartFaceGeometry face)
    {
        var vertices = solid.Vertices.Where(vertex => vertex.Point.Length >= 3)
            .GroupBy(vertex => vertex.Index).ToDictionary(group => group.Key, group => group.First().Point);
        var areas = face.Loops.Select(loop => Area(loop.VertexIndexes
            .Where(vertices.ContainsKey).Select(index => vertices[index]).ToArray())).ToArray();
        return areas.Length == 0 ? 0 : Math.Max(areas[0] - areas.Skip(1).Sum(), 0);
    }

    private static bool Above(double[] point, double plane) => point[2] > plane;

    private static double[] Cross(double[] first, double[] second, double plane)
    {
        var t = (plane - first[2]) / (second[2] - first[2]);
        return [first[0] + (second[0] - first[0]) * t, first[1] + (second[1] - first[1]) * t, plane];
    }

    // The body's section at a plane: outer contours, each with its holes. Empty when the plane
    // misses the body; null when the cut edges do not close into contours.
    private static List<List<IReadOnlyList<double[]>>>? CrossSection(PartSolidGeometry solid,
        IReadOnlyDictionary<int, double[]> vertices, double plane)
    {
        var points = new Dictionary<(int, int), double[]>();
        var links = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var face in solid.Faces)
        {
            var cuts = new List<(int, int)>();
            foreach (var loop in face.Loops)
                for (var i = 0; i < loop.VertexIndexes.Count; i++)
                {
                    var a = loop.VertexIndexes[i];
                    var b = loop.VertexIndexes[(i + 1) % loop.VertexIndexes.Count];
                    if (Above(vertices[a], plane) == Above(vertices[b], plane)) continue;
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    points[key] = Cross(vertices[key.Item1], vertices[key.Item2], plane);
                    cuts.Add(key);
                }
            if (cuts.Count == 0) continue;
            if (cuts.Count % 2 != 0) return null;
            // The cuts of one flat face lie on one line; along it the face is entered and left in turn.
            var byX = cuts.Max(key => points[key][0]) - cuts.Min(key => points[key][0])
                >= cuts.Max(key => points[key][1]) - cuts.Min(key => points[key][1]);
            var ordered = cuts.OrderBy(key => points[key][byX ? 0 : 1]).ToArray();
            for (var i = 0; i < ordered.Length; i += 2)
            {
                Link(links, ordered[i], ordered[i + 1]);
                Link(links, ordered[i + 1], ordered[i]);
            }
        }
        if (links.Values.Any(list => list.Count != 2)) return null;

        var contours = new List<IReadOnlyList<double[]>>();
        var visited = new HashSet<(int, int)>();
        foreach (var start in links.Keys)
        {
            if (!visited.Add(start)) continue;
            var contour = new List<double[]> { points[start] };
            var previous = start;
            var current = links[start][0];
            while (current != start)
            {
                if (!visited.Add(current)) return null;
                contour.Add(points[current]);
                var next = links[current][0] == previous ? links[current][1] : links[current][0];
                previous = current;
                current = next;
            }
            if (contour.Count >= 3) contours.Add(contour);
        }
        return Regions(contours);
    }

    private static void Link(Dictionary<(int, int), List<(int, int)>> links, (int, int) from, (int, int) to)
    {
        if (!links.TryGetValue(from, out var list)) links[from] = list = new List<(int, int)>();
        list.Add(to);
    }

    // A contour lying inside an odd number of others is a hole of the smallest one around it.
    private static List<List<IReadOnlyList<double[]>>> Regions(List<IReadOnlyList<double[]>> contours)
    {
        var around = contours.Select(contour => contours.Where(other => !ReferenceEquals(other, contour)
            && Contains(other, contour[0])).ToArray()).ToArray();
        var regions = new Dictionary<IReadOnlyList<double[]>, List<IReadOnlyList<double[]>>>();
        for (var i = 0; i < contours.Count; i++)
            if (around[i].Length % 2 == 0) regions[contours[i]] = [contours[i]];
        for (var i = 0; i < contours.Count; i++)
            if (around[i].Length % 2 == 1)
                regions[around[i].OrderBy(Area).First()].Add(contours[i]);
        return regions.Values.ToList();
    }

    private static double Area(IReadOnlyList<double[]> contour)
    {
        var twice = 0d;
        for (var i = 0; i < contour.Count; i++)
        {
            var next = contour[(i + 1) % contour.Count];
            twice += contour[i][0] * next[1] - next[0] * contour[i][1];
        }
        return Math.Abs(twice) / 2;
    }

    private static bool Contains(IReadOnlyList<double[]> contour, double[] point)
    {
        var inside = false;
        for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            if (contour[i][1] > point[1] != contour[j][1] > point[1]
                && point[0] < (contour[j][0] - contour[i][0]) * (point[1] - contour[i][1])
                    / (contour[j][1] - contour[i][1]) + contour[i][0])
                inside = !inside;
        return inside;
    }
}
