using System.Collections.Generic;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A body for the contact search, built from geometry the bridge already read in a
/// drawing view's coordinate system.
///
/// Nothing here touches Tekla. The bridge sets the work plane to the view and reads the
/// solid once; this turns the result into the shapes the search wants. That keeps the two
/// jobs apart - reading a running model, and understanding what was read - and this half
/// can be tested without Tekla running.
///
/// Coordinates stay exactly as they arrived, in the view's system. The search answers in
/// whatever system it is given and never converts, so contacts come back in the same
/// coordinates as the drawing's dimensions and the two can be compared directly.
/// </summary>
public sealed class ViewSolidAdapter : ISolid
{
    private ViewSolidAdapter(string id, IReadOnlyList<IFace> faces, Obb boundingBox, int droppedFaces)
    {
        Id = id;
        Faces = faces;
        BoundingBox = boundingBox;
        DroppedFaces = droppedFaces;
    }

    public string Id { get; }
    public IEnumerable<IFace> Faces { get; }
    public Obb BoundingBox { get; }

    /// <summary>
    /// Faces left out: either a loop named a vertex that was not in the set, or no loop
    /// on the face had three points to make a contour from.
    ///
    /// Reported rather than swallowed: a body missing a face still yields contacts, but
    /// the ones it does not yield are no longer evidence of anything. The bridge's own
    /// completeness flag is not used for this - it is set unconditionally on the success
    /// path, so it never varies and would give false comfort.
    /// </summary>
    public int DroppedFaces { get; }

    public static ViewSolidAdapter? FromGeometry(PartSolidGeometryInViewResult geometry)
    {
        if (geometry is null || !geometry.Success)
            return null;

        var solid = geometry.Solid;

        // Indexed rather than positional: the loops refer to vertices by Index, and
        // nothing promises those run 0, 1, 2 with no gaps.
        var vertices = new Dictionary<int, Vec3>(solid.Vertices.Count);
        foreach (var vertex in solid.Vertices)
        {
            if (vertex.Point.Length >= 3)
                vertices[vertex.Index] = new Vec3(vertex.Point[0], vertex.Point[1], vertex.Point[2]);
        }

        var faces = new List<IFace>(solid.Faces.Count);
        var dropped = 0;

        foreach (var face in solid.Faces)
        {
            var loops = new List<ILoop>(face.Loops.Count);
            var broken = false;

            foreach (var loop in face.Loops)
            {
                var points = new List<Vec3>(loop.VertexIndexes.Count);
                foreach (var index in loop.VertexIndexes)
                {
                    if (!vertices.TryGetValue(index, out var point))
                    {
                        // Every index has to resolve. Skipping the missing one and keeping
                        // the rest would close the contour through a different route: four
                        // corners minus one is not a smaller quadrilateral, it is a
                        // triangle that was never part of the body. Inventing a face is
                        // worse than losing one, because the invention gets contacts found
                        // on it and nothing downstream can tell.
                        broken = true;
                        break;
                    }

                    points.Add(point);
                }

                if (broken)
                    break;

                if (points.Count < 3)
                {
                    // A degenerate inner loop is just as damaging as a missing outer
                    // loop: accepting the rest would fill a hole that the source solid
                    // did not fill. Drop the whole face rather than inventing area.
                    broken = true;
                    break;
                }

                loops.Add(new ViewLoop(points));
            }

            if (broken || loops.Count == 0)
            {
                dropped++;
                continue;
            }

            // A face Tekla gave no normal for is kept for its edges and skipped by the
            // face search, which needs a plane. Inventing one from the loop would be a
            // guess dressed up as data.
            var normal = face.Normal is { Length: >= 3 }
                ? new Vec3(face.Normal[0], face.Normal[1], face.Normal[2])
                : Vec3.Zero;

            faces.Add(new ViewFace(normal, loops));
        }

        if (faces.Count == 0)
            return null;

        var box = BuildBox(geometry, vertices.Values);

        return new ViewSolidAdapter(
            geometry.ModelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            faces,
            box,
            dropped);
    }

    /// <summary>
    /// A box on the part's own axes, measured over the vertices that came with them.
    ///
    /// The extent is computed here rather than sent, so the box cannot disagree with the
    /// geometry it guards - a box that fails to cover its own body would have the broad
    /// phase discard pairs whose faces touch, and nothing would report it. The axes are
    /// the one thing that cannot be worked out from the vertices, which is why they are
    /// sent and this is not.
    /// </summary>
    private static Obb BuildBox(PartSolidGeometryInViewResult geometry, IEnumerable<Vec3> points)
    {
        Vec3 axisX = new(1, 0, 0), axisY = new(0, 1, 0), axisZ = new(0, 0, 1);

        if (geometry.AxisX.Length >= 3 && geometry.AxisY.Length >= 3)
        {
            var x = new Vec3(geometry.AxisX[0], geometry.AxisX[1], geometry.AxisX[2]);
            var y = new Vec3(geometry.AxisY[0], geometry.AxisY[1], geometry.AxisY[2]);

            // Tekla's axes are neither promised to be unit length nor exactly square to
            // each other, so the frame is rebuilt rather than trusted.
            if (x.IsUsableDirection && y.IsUsableDirection)
            {
                var z = x.Cross(y);
                if (z.IsUsableDirection)
                {
                    axisX = x.Normalized();
                    axisZ = z.Normalized();
                    axisY = axisZ.Cross(axisX);
                }
            }
        }

        return Obb.FromPoints(points, axisX, axisY, axisZ);
    }

    private sealed class ViewFace : IFace
    {
        public ViewFace(Vec3 normal, IReadOnlyList<ILoop> loops)
        {
            Normal = normal;
            Loops = loops;
        }

        public Vec3 Normal { get; }
        public IReadOnlyList<ILoop> Loops { get; }
    }

    private sealed class ViewLoop : ILoop
    {
        public ViewLoop(IReadOnlyList<Vec3> points) => Points = points;

        public IReadOnlyList<Vec3> Points { get; }
    }
}
