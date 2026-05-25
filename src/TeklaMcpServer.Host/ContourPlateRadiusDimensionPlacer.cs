using System;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

// Standalone example: place RadiusDimension on every arc segment of a ContourPlate
// in the active drawing view.
//
// Supports two arc sources:
//   1. GetContourPolycurve() — returns Arc objects directly (CHAMFER_ARC_POINT plates).
//   2. Analytical fallback   — computes arc geometry from CHAMFER_ROUNDING radius and neighbors.
//
// Usage:
//   var placer = new ContourPlateRadiusDimensionPlacer();
//   placer.Run(viewId: null, distance: 150.0, attributesFile: "standard");
//
// viewId = null → first non-sheet view in the active drawing.

public sealed class ContourPlateRadiusDimensionPlacer
{
    private readonly Model _model = new Model();

    public void Run(int? viewId, double distance, string attributesFile)
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No drawing is open.");

        var targetView = ResolveView(activeDrawing, viewId);

        // Find the first ContourPlate in the view.
        Tekla.Structures.Model.ContourPlate? plate = null;
        var parts = targetView.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
        while (parts.MoveNext())
        {
            if (parts.Current is not Tekla.Structures.Drawing.Part dp)
                continue;
            if (_model.SelectModelObject(dp.ModelIdentifier) is Tekla.Structures.Model.ContourPlate cp)
            {
                plate = cp;
                break;
            }
        }

        if (plate == null)
            throw new InvalidOperationException("No ContourPlate found in the target view.");

        var viewCs = targetView.ViewCoordinateSystem;
        var wph = _model.GetWorkPlaneHandler();
        var originalPlane = wph.GetCurrentTransformationPlane();

        var attributes = new RadiusDimensionAttributes();
        try { attributes.LoadAttributes(string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile); }
        catch { }

        // GetContourPolycurve() always returns world-space coordinates regardless
        // of the current work plane. Select() is required before calling it.
        plate.Select();
        var polycurve = plate.GetContourPolycurve();

        var created = 0;
        var arcCount = 0;

        try
        {
            // Switch work plane so that RadiusDimension receives view-plane coordinates.
            wph.SetCurrentTransformationPlane(new TransformationPlane(viewCs));

            if (polycurve != null)
            {
                foreach (var curve in polycurve)
                {
                    if (curve is not Tekla.Structures.Geometry3d.Arc arc)
                        continue;

                    arcCount++;
                    if (TryInsert(targetView,
                            Flatten(arc.StartPoint),
                            Flatten(arc.ArcMiddlePoint),
                            Flatten(arc.EndPoint),
                            distance, attributes))
                        created++;
                }
            }

            // Analytical fallback for plates where GetContourPolycurve() returned
            // null or no Arc segments. Handles CHAMFER_ROUNDING by computing
            // the arc geometry from the chamfer radius and the two neighbor edges.
            if (arcCount == 0)
            {
                var viewPlate = (Tekla.Structures.Model.ContourPlate)_model.SelectModelObject(plate.Identifier);
                var pts = viewPlate.Contour.ContourPoints;
                var n = pts.Count;

                for (var i = 0; i < n; i++)
                {
                    var cp = (ContourPoint)pts[i];
                    if (cp.Chamfer.Type != Chamfer.ChamferTypeEnum.CHAMFER_ROUNDING)
                        continue;

                    var radius = cp.Chamfer.X;
                    if (radius <= 0)
                        continue;

                    arcCount++;

                    var vertex = Flatten((Point)cp);
                    var prev = Flatten((Point)pts[((i - 1) % n + n) % n]);
                    var next = Flatten((Point)pts[(i + 1) % n]);

                    // Unit vectors along the two edges meeting at this vertex.
                    var up = new Vector(prev.X - vertex.X, prev.Y - vertex.Y, 0);
                    var un = new Vector(next.X - vertex.X, next.Y - vertex.Y, 0);
                    up.Normalize();
                    un.Normalize();

                    // Half-angle trig: t = tangent length from vertex to arc endpoints,
                    // centerDist = distance from vertex to arc center.
                    var cosHalf = Math.Sqrt((1.0 + up.Dot(un)) / 2.0);
                    var sinHalf = Math.Sqrt((1.0 - up.Dot(un)) / 2.0);
                    if (sinHalf < 1e-9)
                        continue;

                    var t = radius * cosHalf / sinHalf;
                    var centerDist = radius / sinHalf;

                    // Bisector direction (toward arc center).
                    var bx = up.X + un.X;
                    var by = up.Y + un.Y;
                    var blen = Math.Sqrt(bx * bx + by * by);
                    if (blen < 1e-9)
                        continue;
                    bx /= blen;
                    by /= blen;

                    // Three points that define the arc for RadiusDimension:
                    // p1 and p3 are the arc endpoints; p2 is the arc midpoint.
                    var p1 = new Point(vertex.X + t * up.X, vertex.Y + t * up.Y, 0);
                    var p3 = new Point(vertex.X + t * un.X, vertex.Y + t * un.Y, 0);
                    var p2 = new Point(vertex.X + (centerDist - radius) * bx,
                                       vertex.Y + (centerDist - radius) * by, 0);

                    if (TryInsert(targetView, p1, p2, p3, distance, attributes))
                        created++;
                }
            }
        }
        finally
        {
            wph.SetCurrentTransformationPlane(originalPlane);
        }

        if (arcCount == 0)
            throw new InvalidOperationException(
                "No arc segments found: polycurve returned no arcs " +
                "and no CHAMFER_ROUNDING vertices in the contour.");

        if (created == 0)
            throw new InvalidOperationException(
                $"Found {arcCount} arc(s) but RadiusDimension.Insert() returned false for all.");

        activeDrawing.CommitChanges();
        Console.WriteLine($"Created {created} radius dimension(s) for {arcCount} arc(s).");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static View ResolveView(Tekla.Structures.Drawing.Drawing drawing, int? viewId)
    {
        var sheet = drawing.GetSheet();
        var enumerator = sheet.GetViews();
        View? first = null;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is not View v)
                continue;
            if (viewId.HasValue && v.GetIdentifier().ID == viewId.Value)
                return v;
            first ??= v;
        }
        return first ?? throw new InvalidOperationException("No views found in the active drawing.");
    }

    private static Point Flatten(Point p) => new Point(p.X, p.Y, 0.0);

    private static bool TryInsert(
        ViewBase view, Point p1, Point p2, Point p3,
        double distance, RadiusDimensionAttributes attributes)
    {
        var dim = new RadiusDimension(view, p1, p2, p3, distance, attributes);
        return dim.Insert();
    }
}
