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

    /// <summary>
    /// The solid cut by the plane of the view, as Tekla returns it: regions, each a list of
    /// loops (the outer contour first, then its holes), each loop a list of [x, y, z] points in
    /// the same coordinates as Vertices. Empty when the plane misses the solid. Null when the
    /// cut was not read: the reader then computes it from Faces.
    /// </summary>
    public List<List<List<double[]>>>? ViewPlaneCut { get; set; }

    /// <summary>True when Vertices, Faces and ViewHull came from a complete solid traversal.</summary>
    public bool SolidGeometryComplete { get; set; }
}
