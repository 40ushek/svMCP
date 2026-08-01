using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class PartSolidGeometry
{
    public double[] BboxMin { get; set; } = [];
    public double[] BboxMax { get; set; } = [];
    public List<PartVertexGeometry> Vertices { get; set; } = new();
    public List<PartFaceGeometry> Faces { get; set; } = new();

    /// <summary>
    /// Two-dimensional convex hull of the solid projected to the XY plane of the view
    /// coordinate system. It is derived from the same canonical, rounded snapshot as
    /// Vertices, so every hull vertex corresponds to a source vertex by its [x,y]
    /// coordinates. Each point contains [x, y] in millimeters. For three or more points,
    /// the list is an open counter-clockwise traversal and does not repeat the first point.
    /// One- and two-point hulls are valid degenerate results.
    /// </summary>
    public List<double[]> ViewHull { get; set; } = new();

    /// <summary>True when Vertices, Faces and ViewHull came from a complete solid traversal.</summary>
    public bool SolidGeometryComplete { get; set; }
}
