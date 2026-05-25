using System;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

// Standalone example: place AngleDimension at every vertex of a ContourPlate
// in the active drawing view.
//
// Usage:
//   var placer = new ContourPlateAngleDimensionPlacer();
//   placer.Run(viewId: null, distance: 200.0, attributesFile: "standard", skipRightAngles: false);
//
// The ContourPlate is found automatically — the first one in the target view.
// viewId = null  → first non-sheet view in the active drawing.

public sealed class ContourPlateAngleDimensionPlacer
{
    private readonly Model _model = new Model();

    public void Run(int? viewId, double distance, string attributesFile, bool skipRightAngles)
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

        // Read the plate normal in world space BEFORE switching the work plane,
        // otherwise GetCoordinateSystem() returns coordinates in the view plane.
        var plateCs = plate.GetCoordinateSystem();
        var plateNormal = plateCs.AxisX.Cross(plateCs.AxisY);

        var viewCs = targetView.ViewCoordinateSystem;
        var viewNormal = viewCs.AxisX.Cross(viewCs.AxisY);

        // When the plate normal is opposite to the view normal the contour
        // winding appears mirrored in the view. Swap prev/next neighbors so
        // AngleDimension always measures the interior angle.
        plateNormal.Normalize();
        viewNormal.Normalize();
        var flipped = plateNormal.Dot(viewNormal) < 0.0;

        var attributes = new AngleDimensionAttributes();
        try { attributes.LoadAttributes(string.IsNullOrWhiteSpace(attributesFile) ? "standard" : attributesFile); }
        catch { }
        attributes.Type = AngleTypes.AngleAtVertex;

        // AngleDimension expects view-plane coordinates.
        // Switch the model work plane to the view coordinate system so that
        // ContourPoint coordinates are expressed in the view plane.
        var wph = _model.GetWorkPlaneHandler();
        var originalPlane = wph.GetCurrentTransformationPlane();
        wph.SetCurrentTransformationPlane(new TransformationPlane(viewCs));

        var created = 0;
        try
        {
            // Re-select the plate after work plane switch so coordinates are in view space.
            var viewPlate = (Tekla.Structures.Model.ContourPlate)_model.SelectModelObject(plate.Identifier);
            var pts = viewPlate.Contour.ContourPoints;
            var n = pts.Count;

            if (n < 3)
                throw new InvalidOperationException($"Contour has only {n} points; at least 3 are required.");

            for (var i = 0; i < n; i++)
            {
                var before = ((i - 1) % n + n) % n;
                var after = (i + 1) % n;

                // Swap neighbors when the contour appears mirrored in the view.
                var firstIdx = flipped ? after : before;
                var secondIdx = flipped ? before : after;

                var vertex = Flatten((Point)pts[i]);
                var first = Flatten((Point)pts[firstIdx]);
                var second = Flatten((Point)pts[secondIdx]);

                if (skipRightAngles && IsRightAngle(vertex, first, second))
                    continue;

                var dim = new AngleDimension(targetView, vertex, first, second, distance, attributes);
                if (dim.Insert())
                    created++;
            }

            if (created == 0)
                throw new InvalidOperationException("AngleDimension.Insert() returned false for all vertices.");

            activeDrawing.CommitChanges();
            Console.WriteLine($"Created {created} angle dimension(s).");
        }
        finally
        {
            wph.SetCurrentTransformationPlane(originalPlane);
        }
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

    private static bool IsRightAngle(Point vertex, Point first, Point second, double toleranceDeg = 0.5)
    {
        var ux = first.X - vertex.X;
        var uy = first.Y - vertex.Y;
        var wx = second.X - vertex.X;
        var wy = second.Y - vertex.Y;
        var cross = ux * wy - uy * wx;
        var dot = ux * wx + uy * wy;
        var angleDeg = Math.Atan2(Math.Abs(cross), dot) * (180.0 / Math.PI);
        return Math.Abs(angleDeg - 90.0) <= toleranceDeg;
    }
}
