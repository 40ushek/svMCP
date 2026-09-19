using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Host;

/// <summary>
/// Read-only diagnostic for developing two non-parallel view planes about the
/// line where those planes intersect. It never modifies the drawing.
/// </summary>
internal sealed class ViewFoldTransformProbe
{
    private const double BasisLengthModelUnits = 1000.0;
    private const double CoordinateSystemTolerance = 1e-6;
    private const double ZeroPaperVectorTolerance = 1e-6;
    private const double RelativeLengthTolerance = 1e-4;
    private const double DirectionAngleToleranceDegrees = 0.01;
    private const double ParallelPlaneAngleToleranceDegrees = 0.1;

    private readonly List<string> _lines = [];

    public void Run(bool applyRequested)
    {
        var drawingHandler = new DrawingHandler();
        var drawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing. Open a drawing in Tekla and try again.");
        var (baseView, targetView) = SelectedViews(drawingHandler);
        var globalModelCs = new CoordinateSystem(
            new Point(0.0, 0.0, 0.0),
            new Vector(1.0, 0.0, 0.0),
            new Vector(0.0, 1.0, 0.0));
        var baseMap = BuildMap(baseView, globalModelCs);
        var targetMap = BuildMap(targetView, globalModelCs);

        Log($"Drawing: {drawing.Name}");
        WriteMap("base", baseMap);
        WriteMap("target", targetMap);

        var baseNormal = Normalize(baseMap.ViewCoordinateSystem.AxisX.Cross(baseMap.ViewCoordinateSystem.AxisY));
        var targetNormal = Normalize(targetMap.ViewCoordinateSystem.AxisX.Cross(targetMap.ViewCoordinateSystem.AxisY));
        var hingeRaw = baseNormal.Cross(targetNormal);
        var hingeLength = Length(hingeRaw);
        if (hingeLength <= Math.Sin(ParallelPlaneAngleToleranceDegrees * (Math.PI / 180.0)))
        {
            Log("fold candidate: False; the view planes are parallel or anti-parallel and have no unique hinge line.");
            WriteLog();
            return;
        }

        var hinge = Normalize(hingeRaw);
        var normalDot = Clamp(baseNormal.Dot(targetNormal), -1.0, 1.0);
        var foldAngleDegrees = Math.Acos(normalDot) * (180.0 / Math.PI);
        var hingePoint = IntersectionPoint(baseNormal, baseMap.ViewCoordinateSystem.Origin, targetNormal, targetMap.ViewCoordinateSystem.Origin, hinge, hingeLength);
        var baseHingePaper = baseMap.ToPaper(hingePoint);
        var targetHingePaper = targetMap.ToPaper(hingePoint);
        var baseHingeEndPaper = baseMap.ToPaper(Add(hingePoint, hinge, BasisLengthModelUnits));
        var targetHingeEndPaper = targetMap.ToPaper(Add(hingePoint, hinge, BasisLengthModelUnits));
        var edge = CompareVector(baseHingeEndPaper - baseHingePaper, targetHingeEndPaper - targetHingePaper);
        var scaleRelativeDifference = RelativeDifference(baseMap.Scale, targetMap.Scale);
        var scaleMatches = scaleRelativeDifference <= RelativeLengthTolerance;
        var delta = baseHingePaper - targetHingePaper;
        var hingeEdgePaper = baseHingeEndPaper - baseHingePaper;
        var baseCenter = FrameCenterPaper(baseView);
        var targetCenter = FrameCenterPaper(targetView);
        var targetCenterAfter = new PaperPoint(targetCenter.X + delta.X, targetCenter.Y + delta.Y);
        var baseSideSign = SideOfEdge(hingeEdgePaper, baseCenter - baseHingePaper);
        var targetSideSign = SideOfEdge(hingeEdgePaper, targetCenterAfter - baseHingePaper);
        var sidesOpposite = baseSideSign != 0.0 && targetSideSign != 0.0 && baseSideSign != targetSideSign;
        var canFoldByTranslation = baseMap.ViewCsMatchesDisplayCs
            && targetMap.ViewCsMatchesDisplayCs
            && scaleMatches
            && edge.Matches;

        Log($"fold angle degrees: {F(foldAngleDegrees)}");
        Log($"hinge point model: {F(hingePoint)} direction={F(hinge)}");
        Log($"hinge base paper={F(baseHingePaper)} target paper={F(targetHingePaper)}");
        Log($"hinge edge: residual={F(edge.Residual)} baseLength={F(edge.BaseLength)} targetLength={F(edge.TargetLength)} relativeLengthDifference={F(edge.RelativeLengthDifference)} angleDegrees={F(edge.AngleDegrees)} matches={edge.Matches}");
        Log($"scale: base={F(baseMap.Scale)} target={F(targetMap.Scale)} relativeDifference={F(scaleRelativeDifference)} matches={scaleMatches}");
        Log($"frame sides of hinge after move: base={baseSideSign} target={targetSideSign} relation={(sidesOpposite ? "opposite-side" : "same-side-or-undetermined")}");
        Log($"hinge translation candidate: {canFoldByTranslation}");
        Log($"recommended target origin delta for hinge: {F(delta)}");
        Log(!canFoldByTranslation
            ? "decision: do not move; the hinge cannot be made coincident by a safe translation."
            : sidesOpposite
                ? "decision: hinge mapping matches and the frames develop on opposite sides of the hinge; explicit fold apply is allowed."
                : "decision: hinge mapping matches, but the frames would lie on the same side of the hinge (or a side is undetermined); do not move.");
        if (applyRequested)
            ApplyAndReadBack(drawing, baseMap.ViewId, targetMap.ViewId, hingePoint, hinge, canFoldByTranslation && sidesOpposite, delta, globalModelCs);
        WriteLog();
    }

    private static (View Base, View Target) SelectedViews(DrawingHandler drawingHandler)
    {
        var selectedViews = new List<View>();
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is View view)
                selectedViews.Add(view);
        }

        if (selectedViews.Count != 2)
            throw new InvalidOperationException($"Select exactly two drawing views, then run --view-fold-probe-selected. Selected views: {selectedViews.Count}.");

        var ordered = selectedViews.OrderByDescending(ViewArea).ToList();
        if (Math.Abs(ViewArea(ordered[0]) - ViewArea(ordered[1])) <= 1e-9 * Math.Max(1.0, ViewArea(ordered[0])))
            throw new InvalidOperationException("The two selected views have the same frame area. Fold base selection is ambiguous.");

        return (ordered[0], ordered[1]);
    }

    private static FoldViewMap BuildMap(View view, CoordinateSystem globalModelCs)
    {
        var origin = view.Origin ?? throw new InvalidOperationException($"View {view.GetIdentifier().ID} has no sheet origin.");
        var scale = view.Attributes.Scale;
        if (scale <= 0.0)
            throw new InvalidOperationException($"View {view.GetIdentifier().ID} has invalid scale {scale}.");

        var viewCs = view.ViewCoordinateSystem;
        CoordinateSystem? displayCs = null;
        try { displayCs = view.DisplayCoordinateSystem; } catch { }
        return new FoldViewMap(
            view.GetIdentifier().ID,
            view.Name,
            scale,
            origin,
            viewCs,
            displayCs,
            MatrixFactory.ByCoordinateSystems(globalModelCs, viewCs));
    }

    private void ApplyAndReadBack(
        Drawing drawing,
        int baseViewId,
        int targetViewId,
        Point hingePoint,
        Vector hinge,
        bool canFoldByTranslation,
        PaperVector delta,
        CoordinateSystem globalModelCs)
    {
        if (!canFoldByTranslation)
        {
            Log("fold apply: skipped because the hinge mapping is not a translation candidate.");
            return;
        }

        Log("fold apply: experimental; overlap is allowed for this explicitly invoked operation.");
        var move = new TeklaMcpServer.Api.Drawing.ViewLayout.TeklaDrawingViewApi()
            .MoveView(targetViewId, delta.X, delta.Y, absolute: false);
        Log($"fold apply: moved targetViewId={targetViewId} oldOrigin=({F(move.OldOriginX)}, {F(move.OldOriginY)}) newOrigin=({F(move.NewOriginX)}, {F(move.NewOriginY)})");

        var viewsAfterApply = EnumerateViews(drawing);
        var baseAfterApply = viewsAfterApply.FirstOrDefault(view => view.GetIdentifier().ID == baseViewId)
            ?? throw new InvalidOperationException($"Base view {baseViewId} was not found after the fold move.");
        var targetAfterApply = viewsAfterApply.FirstOrDefault(view => view.GetIdentifier().ID == targetViewId)
            ?? throw new InvalidOperationException($"Target view {targetViewId} was not found after the fold move.");
        var baseAfterMap = BuildMap(baseAfterApply, globalModelCs);
        var targetAfterMap = BuildMap(targetAfterApply, globalModelCs);
        var baseHingePaper = baseAfterMap.ToPaper(hingePoint);
        var targetHingePaper = targetAfterMap.ToPaper(hingePoint);
        var edge = CompareVector(
            baseAfterMap.ToPaper(Add(hingePoint, hinge, BasisLengthModelUnits)) - baseHingePaper,
            targetAfterMap.ToPaper(Add(hingePoint, hinge, BasisLengthModelUnits)) - targetHingePaper);
        Log($"fold readback: target hinge delta={F(baseHingePaper - targetHingePaper)} edgeMatches={edge.Matches}");
    }

    private static List<View> EnumerateViews(Drawing drawing)
    {
        var result = new List<View>();
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is View view)
                result.Add(view);
        }

        return result;
    }

    private static PaperPoint FrameCenterPaper(View view)
    {
        var box = view.GetAxisAlignedBoundingBox();
        return new PaperPoint(
            (box.LowerLeft.X + box.UpperRight.X) * 0.5,
            (box.LowerLeft.Y + box.UpperRight.Y) * 0.5);
    }

    // hinge is normalized, so the closed form needs the extra 1/|n1 x n2| = 1/sin(foldAngle).
    private static Point IntersectionPoint(Vector firstNormal, Point firstOrigin, Vector secondNormal, Point secondOrigin, Vector hinge, double hingeLength)
    {
        var firstDistance = firstNormal.Dot(new Vector(firstOrigin.X, firstOrigin.Y, firstOrigin.Z));
        var secondDistance = secondNormal.Dot(new Vector(secondOrigin.X, secondOrigin.Y, secondOrigin.Z));
        var firstTerm = secondNormal.Cross(hinge);
        var secondTerm = hinge.Cross(firstNormal);
        return new Point(
            ((firstDistance * firstTerm.X) + (secondDistance * secondTerm.X)) / hingeLength,
            ((firstDistance * firstTerm.Y) + (secondDistance * secondTerm.Y)) / hingeLength,
            ((firstDistance * firstTerm.Z) + (secondDistance * secondTerm.Z)) / hingeLength);
    }

    private static PaperVectorComparison CompareVector(PaperVector baseVector, PaperVector targetVector)
    {
        var baseLength = baseVector.Length;
        var targetLength = targetVector.Length;
        var baseIsZero = baseLength <= ZeroPaperVectorTolerance;
        var targetIsZero = targetLength <= ZeroPaperVectorTolerance;
        if (baseIsZero || targetIsZero)
        {
            var bothAreZero = baseIsZero && targetIsZero;
            return new PaperVectorComparison(baseVector - targetVector, baseLength, targetLength, bothAreZero ? 0.0 : 1.0, bothAreZero ? 0.0 : 180.0, bothAreZero);
        }

        var relativeLengthDifference = RelativeDifference(baseLength, targetLength);
        var angleDegrees = Math.Acos(Clamp(baseVector.Dot(targetVector) / (baseLength * targetLength), -1.0, 1.0)) * (180.0 / Math.PI);
        return new PaperVectorComparison(
            baseVector - targetVector,
            baseLength,
            targetLength,
            relativeLengthDifference,
            angleDegrees,
            relativeLengthDifference <= RelativeLengthTolerance && angleDegrees <= DirectionAngleToleranceDegrees);
    }

    private void WriteMap(string label, FoldViewMap map)
    {
        Log($"{label}: viewId={map.ViewId} name={map.Name} scale={F(map.Scale)} sheetOrigin=({F(map.SheetOrigin.X)}, {F(map.SheetOrigin.Y)})");
        Log($"{label}: viewCs origin={F(map.ViewCoordinateSystem.Origin)} axisX={F(map.ViewCoordinateSystem.AxisX)} axisY={F(map.ViewCoordinateSystem.AxisY)}");
        Log($"{label}: viewCsEqualsDisplayCs={map.ViewCsMatchesDisplayCs}");
    }

    private void WriteLog()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeklaMcpServer.ViewFoldTransformProbe.log");
        File.WriteAllLines(path, _lines);
        Console.WriteLine($"View fold transform probe written to {path}");
    }

    private void Log(string line) => _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");

    private static double ViewArea(View view) => Math.Max(view.Width, 0.0) * Math.Max(view.Height, 0.0);
    private static double RelativeDifference(double left, double right) => Math.Abs(left - right) / Math.Max(left, right);
    private static Vector Normalize(Vector value)
    {
        var length = Length(value);
        if (length <= CoordinateSystemTolerance)
            throw new InvalidOperationException("Cannot normalize a zero model-space vector.");
        return new Vector(value.X / length, value.Y / length, value.Z / length);
    }
    private static double Length(Vector value) => Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
    private static Point Add(Point point, Vector direction, double length) => new(point.X + (direction.X * length), point.Y + (direction.Y * length), point.Z + (direction.Z * length));
    private static double SideOfEdge(PaperVector edge, PaperVector side)
    {
        var cross = (edge.X * side.Y) - (edge.Y * side.X);
        return Math.Abs(cross) <= ZeroPaperVectorTolerance ? 0.0 : Math.Sign(cross);
    }
    private static bool CoordinateSystemsMatch(CoordinateSystem left, CoordinateSystem right)
        => Distance(left.Origin, right.Origin) <= CoordinateSystemTolerance
            && Distance(left.AxisX, right.AxisX) <= CoordinateSystemTolerance
            && Distance(left.AxisY, right.AxisY) <= CoordinateSystemTolerance;
    private static double Distance(Point left, Point right) => Math.Sqrt(((left.X - right.X) * (left.X - right.X)) + ((left.Y - right.Y) * (left.Y - right.Y)) + ((left.Z - right.Z) * (left.Z - right.Z)));
    private static double Distance(Vector left, Vector right) => Math.Sqrt(((left.X - right.X) * (left.X - right.X)) + ((left.Y - right.Y) * (left.Y - right.Y)) + ((left.Z - right.Z) * (left.Z - right.Z)));
    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string F(Point value) => $"({F(value.X)}, {F(value.Y)}, {F(value.Z)})";
    private static string F(Vector value) => $"({F(value.X)}, {F(value.Y)}, {F(value.Z)})";
    private static string F(PaperPoint value) => $"({F(value.X)}, {F(value.Y)})";
    private static string F(PaperVector value) => $"({F(value.X)}, {F(value.Y)})";

    private sealed class FoldViewMap
    {
        public FoldViewMap(int viewId, string name, double scale, Point sheetOrigin, CoordinateSystem viewCoordinateSystem, CoordinateSystem? displayCoordinateSystem, Matrix modelToView)
        {
            ViewId = viewId; Name = name; Scale = scale; SheetOrigin = sheetOrigin; ViewCoordinateSystem = viewCoordinateSystem; DisplayCoordinateSystem = displayCoordinateSystem; ModelToView = modelToView;
            ViewCsMatchesDisplayCs = displayCoordinateSystem != null && CoordinateSystemsMatch(viewCoordinateSystem, displayCoordinateSystem);
        }
        public int ViewId { get; private set; }
        public string Name { get; private set; }
        public double Scale { get; private set; }
        public Point SheetOrigin { get; private set; }
        public CoordinateSystem ViewCoordinateSystem { get; private set; }
        public CoordinateSystem? DisplayCoordinateSystem { get; private set; }
        public Matrix ModelToView { get; private set; }
        public bool ViewCsMatchesDisplayCs { get; private set; }
        public PaperPoint ToPaper(Point modelPoint)
        {
            var local = ModelToView.Transform(modelPoint);
            return new PaperPoint(SheetOrigin.X + (local.X / Scale), SheetOrigin.Y + (local.Y / Scale));
        }
    }

    private sealed class PaperVectorComparison
    {
        public PaperVectorComparison(PaperVector residual, double baseLength, double targetLength, double relativeLengthDifference, double angleDegrees, bool matches)
        { Residual = residual; BaseLength = baseLength; TargetLength = targetLength; RelativeLengthDifference = relativeLengthDifference; AngleDegrees = angleDegrees; Matches = matches; }
        public PaperVector Residual { get; private set; }
        public double BaseLength { get; private set; }
        public double TargetLength { get; private set; }
        public double RelativeLengthDifference { get; private set; }
        public double AngleDegrees { get; private set; }
        public bool Matches { get; private set; }
    }

    private struct PaperPoint
    {
        public PaperPoint(double x, double y) { X = x; Y = y; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public static PaperVector operator -(PaperPoint left, PaperPoint right) => new(left.X - right.X, left.Y - right.Y);
    }

    private struct PaperVector
    {
        public PaperVector(double x, double y) { X = x; Y = y; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Length => Math.Sqrt((X * X) + (Y * Y));
        public double Dot(PaperVector other) => (X * other.X) + (Y * other.Y);
        public static PaperVector operator -(PaperVector left, PaperVector right) => new(left.X - right.X, left.Y - right.Y);
    }
}
