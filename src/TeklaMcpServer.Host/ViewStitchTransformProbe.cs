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
/// Diagnostic for manual view stitching. By default it is read-only; the explicit
/// apply switch moves the smaller selected view only after a successful comparison.
/// </summary>
internal sealed class ViewStitchTransformProbe
{
    private const double BasisLengthModelUnits = 1000.0;
    private const double RelativeLengthTolerance = 1e-4;
    private const double DirectionAngleToleranceDegrees = 0.01;
    // These are paper-space values. Projected depth vectors can carry small
    // floating-point noise even when a model direction has no paper component.
    private const double ZeroPaperVectorTolerance = 1e-6;
    private const double CoordinateSystemEqualityTolerance = 1e-6;

    private readonly List<string> _lines = [];

    public void Run(string[] args)
    {
        var drawingHandler = new DrawingHandler();
        var drawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing. Open a drawing in Tekla and try again.");

        var (baseViewId, targetViewId) = ParseViewIds(drawingHandler, args);
        if (baseViewId == targetViewId)
            throw new InvalidOperationException("Base and target view IDs must be different.");

        var views = EnumerateViews(drawing);
        var baseView = views.FirstOrDefault(view => view.GetIdentifier().ID == baseViewId)
            ?? throw new InvalidOperationException($"Base view {baseViewId} was not found in the active drawing.");
        var targetView = views.FirstOrDefault(view => view.GetIdentifier().ID == targetViewId)
            ?? throw new InvalidOperationException($"Target view {targetViewId} was not found in the active drawing.");

        var globalModelCs = new CoordinateSystem(
            new Point(0.0, 0.0, 0.0),
            new Vector(1.0, 0.0, 0.0),
            new Vector(0.0, 1.0, 0.0));

        var baseMap = BuildMap(baseView, globalModelCs);
        var targetMap = BuildMap(targetView, globalModelCs);
        var comparison = Compare(baseMap, targetMap);
        var applyRequested = args.Any(arg => string.Equals(
            arg,
            "--view-stitch-apply-selected",
            StringComparison.OrdinalIgnoreCase));

        Log($"Drawing: {drawing.Name}");
        Log($"Probe basis length in model units: {BasisLengthModelUnits.ToString("0.###", CultureInfo.InvariantCulture)}");
        WriteMap("base", baseMap);
        WriteMap("target", targetMap);
        WriteComparison(comparison);
        if (applyRequested)
            ApplyAndReadBack(drawing, baseViewId, targetViewId, baseMap, targetMap, comparison, globalModelCs);

        var path = Path.Combine(Path.GetTempPath(), "TeklaMcpServer.ViewStitchTransformProbe.log");
        File.WriteAllLines(path, _lines);
        Console.WriteLine($"View stitch transform probe written to {path}");
    }

    private static (int BaseViewId, int TargetViewId) ParseViewIds(
        DrawingHandler drawingHandler,
        IReadOnlyList<string> args)
    {
        var selectedMode = args.FirstOrDefault(arg =>
            string.Equals(arg, "--view-stitch-probe-selected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--view-stitch-apply-selected", StringComparison.OrdinalIgnoreCase));
        if (selectedMode != null)
        {
            var selectedViews = new List<View>();
            var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
            while (selected.MoveNext())
            {
                if (selected.Current is View view)
                    selectedViews.Add(view);
            }

            if (selectedViews.Count != 2)
            {
                throw new InvalidOperationException(
                    $"Select exactly two drawing views, then run {selectedMode}. Selected views: {selectedViews.Count}.");
            }

            var orderedByArea = selectedViews
                .OrderByDescending(ViewArea)
                .ToList();
            var baseArea = ViewArea(orderedByArea[0]);
            var targetArea = ViewArea(orderedByArea[1]);
            if (Math.Abs(baseArea - targetArea) <= 1e-9 * Math.Max(1.0, baseArea))
            {
                throw new InvalidOperationException(
                    "The two selected views have the same frame area. Use --view-stitch-probe <baseViewId> <targetViewId> to choose the base view explicitly.");
            }

            return (orderedByArea[0].GetIdentifier().ID, orderedByArea[1].GetIdentifier().ID);
        }

        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], "--view-stitch-probe", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 2 >= args.Count
                || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var baseViewId)
                || !int.TryParse(args[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetViewId))
            {
                throw new InvalidOperationException(
                    "Usage: TeklaMcpServer.Host.exe --view-stitch-probe <baseViewId> <targetViewId>, --view-stitch-probe-selected, or --view-stitch-apply-selected");
            }

            return (baseViewId, targetViewId);
        }

        throw new InvalidOperationException(
            "Usage: TeklaMcpServer.Host.exe --view-stitch-probe <baseViewId> <targetViewId>, --view-stitch-probe-selected, or --view-stitch-apply-selected");
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

    private static double ViewArea(View view)
        => Math.Max(view.Width, 0.0) * Math.Max(view.Height, 0.0);

    private void ApplyAndReadBack(
        Drawing drawing,
        int baseViewId,
        int targetViewId,
        ViewMap baseMap,
        ViewMap targetMap,
        StitchComparison comparison,
        CoordinateSystem globalModelCs)
    {
        if (!comparison.CanStitchByTranslation)
        {
            Log("apply: skipped because the two views do not have matching linear transforms.");
            return;
        }

        if (!baseMap.ViewCsMatchesDisplayCs || !targetMap.ViewCsMatchesDisplayCs)
        {
            Log("apply: skipped because ViewCoordinateSystem and DisplayCoordinateSystem differ or DisplayCoordinateSystem is unavailable.");
            return;
        }

        var delta = comparison.TargetOriginDelta;
        var move = new TeklaMcpServer.Api.Drawing.ViewLayout.TeklaDrawingViewApi()
            .MoveView(targetViewId, delta.X, delta.Y, absolute: false);
        Log($"apply: moved targetViewId={targetViewId} oldOrigin=({F(move.OldOriginX)}, {F(move.OldOriginY)}) newOrigin=({F(move.NewOriginX)}, {F(move.NewOriginY)})");

        var viewsAfterApply = EnumerateViews(drawing);
        var baseAfterApply = viewsAfterApply.FirstOrDefault(view => view.GetIdentifier().ID == baseViewId)
            ?? throw new InvalidOperationException($"Base view {baseViewId} was not found after the move.");
        var targetAfterApply = viewsAfterApply.FirstOrDefault(view => view.GetIdentifier().ID == targetViewId)
            ?? throw new InvalidOperationException($"Target view {targetViewId} was not found after the move.");
        var afterComparison = Compare(
            BuildMap(baseAfterApply, globalModelCs),
            BuildMap(targetAfterApply, globalModelCs));
        Log($"readback: target origin delta={F(afterComparison.TargetOriginDelta)} translationOnlyCandidate={afterComparison.CanStitchByTranslation}");
    }

    private static ViewMap BuildMap(View view, CoordinateSystem globalModelCs)
    {
        var origin = view.Origin
            ?? throw new InvalidOperationException($"View {view.GetIdentifier().ID} has no sheet origin.");
        var scale = view.Attributes.Scale;
        if (scale <= 0.0)
            throw new InvalidOperationException($"View {view.GetIdentifier().ID} has invalid scale {scale}.");

        var viewCs = view.ViewCoordinateSystem;
        CoordinateSystem? displayCs = null;
        try
        {
            displayCs = view.DisplayCoordinateSystem;
        }
        catch
        {
        }

        var modelToView = MatrixFactory.ByCoordinateSystems(globalModelCs, viewCs);

        var modelOrigin = new Point(0.0, 0.0, 0.0);
        var modelX = new Point(BasisLengthModelUnits, 0.0, 0.0);
        var modelY = new Point(0.0, BasisLengthModelUnits, 0.0);
        var modelZ = new Point(0.0, 0.0, BasisLengthModelUnits);

        var sheetOrigin = ToSheet(modelToView.Transform(modelOrigin), origin, scale);
        var sheetX = ToSheet(modelToView.Transform(modelX), origin, scale) - sheetOrigin;
        var sheetY = ToSheet(modelToView.Transform(modelY), origin, scale) - sheetOrigin;
        var sheetZ = ToSheet(modelToView.Transform(modelZ), origin, scale) - sheetOrigin;

        return new ViewMap(
            view.GetIdentifier().ID,
            view.Name,
            scale,
            new SheetPoint(origin.X, origin.Y),
            viewCs.Origin,
            viewCs.AxisX,
            viewCs.AxisY,
            displayCs,
            sheetOrigin,
            sheetX,
            sheetY,
            sheetZ);
    }

    private static SheetPoint ToSheet(Point localPoint, Point viewOrigin, double scale)
        => new(viewOrigin.X + (localPoint.X / scale), viewOrigin.Y + (localPoint.Y / scale));

    private static StitchComparison Compare(ViewMap baseMap, ViewMap targetMap)
    {
        var x = CompareLinearVector("X", baseMap.ModelXOnSheet, targetMap.ModelXOnSheet);
        var y = CompareLinearVector("Y", baseMap.ModelYOnSheet, targetMap.ModelYOnSheet);
        var z = CompareLinearVector("Z", baseMap.ModelZOnSheet, targetMap.ModelZOnSheet);
        var canStitchByTranslation = x.Matches && y.Matches && z.Matches;
        var shift = baseMap.ModelOriginOnSheet - targetMap.ModelOriginOnSheet;

        return new StitchComparison(
            canStitchByTranslation,
            x,
            y,
            z,
            shift);
    }

    private static LinearVectorComparison CompareLinearVector(
        string name,
        SheetVector baseVector,
        SheetVector targetVector)
    {
        var residual = baseVector - targetVector;
        var baseLength = baseVector.Length;
        var targetLength = targetVector.Length;
        var maxLength = Math.Max(baseLength, targetLength);

        var baseIsZero = baseLength <= ZeroPaperVectorTolerance;
        var targetIsZero = targetLength <= ZeroPaperVectorTolerance;
        if (baseIsZero || targetIsZero)
        {
            var bothAreZero = baseIsZero && targetIsZero;
            return new LinearVectorComparison(
                name,
                residual,
                baseLength,
                targetLength,
                bothAreZero ? 0.0 : 1.0,
                bothAreZero ? 0.0 : 180.0,
                bothAreZero);
        }

        var relativeLengthDifference = Math.Abs(baseLength - targetLength) / maxLength;
        var cosine = (baseVector.Dot(targetVector)) / (baseLength * targetLength);
        cosine = Math.Max(-1.0, Math.Min(1.0, cosine));
        var angleDegrees = Math.Acos(cosine) * (180.0 / Math.PI);
        var matches = relativeLengthDifference <= RelativeLengthTolerance
            && angleDegrees <= DirectionAngleToleranceDegrees;

        return new LinearVectorComparison(
            name,
            residual,
            baseLength,
            targetLength,
            relativeLengthDifference,
            angleDegrees,
            matches);
    }

    private void WriteMap(string label, ViewMap map)
    {
        Log($"{label}: viewId={map.ViewId} name={map.Name} scale={F(map.Scale)} sheetOrigin={F(map.SheetOrigin)}");
        Log($"{label}: viewCs origin={F(map.ViewCsOrigin)} axisX={F(map.ViewCsAxisX)} axisY={F(map.ViewCsAxisY)}");
        if (map.DisplayCoordinateSystem == null)
        {
            Log($"{label}: displayCs=<unavailable>");
        }
        else
        {
            var display = map.DisplayCoordinateSystem;
            Log($"{label}: displayCs origin={F(display.Origin)} axisX={F(display.AxisX)} axisY={F(display.AxisY)}");
            Log($"{label}: viewCsEqualsDisplayCs={map.ViewCsMatchesDisplayCs}");
        }

        Log($"{label}: F(O)={F(map.ModelOriginOnSheet)}");
        Log($"{label}: F(X)-F(O)={F(map.ModelXOnSheet)}");
        Log($"{label}: F(Y)-F(O)={F(map.ModelYOnSheet)}");
        Log($"{label}: F(Z)-F(O)={F(map.ModelZOnSheet)}");
    }

    private void WriteComparison(StitchComparison comparison)
    {
        WriteLinearComparison(comparison.X);
        WriteLinearComparison(comparison.Y);
        WriteLinearComparison(comparison.Z);
        Log($"translation-only candidate: {comparison.CanStitchByTranslation}");
        Log($"recommended target origin delta: {F(comparison.TargetOriginDelta)}");
        Log(comparison.CanStitchByTranslation
            ? "decision: calculated linear parts match; apply delta only for a visual live-drawing check."
            : "decision: translation would align only one point; do not move the target view.");
    }

    private void WriteLinearComparison(LinearVectorComparison comparison)
    {
        Log(
            $"linear {comparison.Name}: residual={F(comparison.Residual)} " +
            $"baseLength={F(comparison.BaseLength)} targetLength={F(comparison.TargetLength)} " +
            $"relativeLengthDifference={F(comparison.RelativeLengthDifference)} " +
            $"angleDegrees={F(comparison.AngleDegrees)} matches={comparison.Matches}");
    }

    private void Log(string line) => _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string F(Point value) => $"({F(value.X)}, {F(value.Y)}, {F(value.Z)})";

    private static string F(Vector value) => $"({F(value.X)}, {F(value.Y)}, {F(value.Z)})";

    private static string F(SheetPoint value) => $"({F(value.X)}, {F(value.Y)})";

    private static string F(SheetVector value) => $"({F(value.X)}, {F(value.Y)})";

    private static bool CoordinateSystemsMatch(CoordinateSystem left, CoordinateSystem right)
        => Distance(left.Origin, right.Origin) <= CoordinateSystemEqualityTolerance
            && Distance(left.AxisX, right.AxisX) <= CoordinateSystemEqualityTolerance
            && Distance(left.AxisY, right.AxisY) <= CoordinateSystemEqualityTolerance;

    private static double Distance(Point left, Point right)
        => Math.Sqrt(
            ((left.X - right.X) * (left.X - right.X)) +
            ((left.Y - right.Y) * (left.Y - right.Y)) +
            ((left.Z - right.Z) * (left.Z - right.Z)));

    private static double Distance(Vector left, Vector right)
        => Math.Sqrt(
            ((left.X - right.X) * (left.X - right.X)) +
            ((left.Y - right.Y) * (left.Y - right.Y)) +
            ((left.Z - right.Z) * (left.Z - right.Z)));

    private sealed class ViewMap
    {
        public ViewMap(
            int viewId,
            string name,
            double scale,
            SheetPoint sheetOrigin,
            Point viewCsOrigin,
            Vector viewCsAxisX,
            Vector viewCsAxisY,
            CoordinateSystem? displayCoordinateSystem,
            SheetPoint modelOriginOnSheet,
            SheetVector modelXOnSheet,
            SheetVector modelYOnSheet,
            SheetVector modelZOnSheet)
        {
            ViewId = viewId;
            Name = name;
            Scale = scale;
            SheetOrigin = sheetOrigin;
            ViewCsOrigin = viewCsOrigin;
            ViewCsAxisX = viewCsAxisX;
            ViewCsAxisY = viewCsAxisY;
            ViewCoordinateSystem = new CoordinateSystem(viewCsOrigin, viewCsAxisX, viewCsAxisY);
            DisplayCoordinateSystem = displayCoordinateSystem;
            ViewCsMatchesDisplayCs = displayCoordinateSystem != null
                && CoordinateSystemsMatch(ViewCoordinateSystem, displayCoordinateSystem);
            ModelOriginOnSheet = modelOriginOnSheet;
            ModelXOnSheet = modelXOnSheet;
            ModelYOnSheet = modelYOnSheet;
            ModelZOnSheet = modelZOnSheet;
        }

        public int ViewId { get; private set; }
        public string Name { get; private set; }
        public double Scale { get; private set; }
        public SheetPoint SheetOrigin { get; private set; }
        public Point ViewCsOrigin { get; private set; }
        public Vector ViewCsAxisX { get; private set; }
        public Vector ViewCsAxisY { get; private set; }
        public CoordinateSystem ViewCoordinateSystem { get; private set; }
        public CoordinateSystem? DisplayCoordinateSystem { get; private set; }
        public bool ViewCsMatchesDisplayCs { get; private set; }
        public SheetPoint ModelOriginOnSheet { get; private set; }
        public SheetVector ModelXOnSheet { get; private set; }
        public SheetVector ModelYOnSheet { get; private set; }
        public SheetVector ModelZOnSheet { get; private set; }
    }

    private sealed class StitchComparison
    {
        public StitchComparison(
            bool canStitchByTranslation,
            LinearVectorComparison x,
            LinearVectorComparison y,
            LinearVectorComparison z,
            SheetVector targetOriginDelta)
        {
            CanStitchByTranslation = canStitchByTranslation;
            X = x;
            Y = y;
            Z = z;
            TargetOriginDelta = targetOriginDelta;
        }

        public bool CanStitchByTranslation { get; private set; }
        public LinearVectorComparison X { get; private set; }
        public LinearVectorComparison Y { get; private set; }
        public LinearVectorComparison Z { get; private set; }
        public SheetVector TargetOriginDelta { get; private set; }
    }

    private sealed class LinearVectorComparison
    {
        public LinearVectorComparison(
            string name,
            SheetVector residual,
            double baseLength,
            double targetLength,
            double relativeLengthDifference,
            double angleDegrees,
            bool matches)
        {
            Name = name;
            Residual = residual;
            BaseLength = baseLength;
            TargetLength = targetLength;
            RelativeLengthDifference = relativeLengthDifference;
            AngleDegrees = angleDegrees;
            Matches = matches;
        }

        public string Name { get; private set; }
        public SheetVector Residual { get; private set; }
        public double BaseLength { get; private set; }
        public double TargetLength { get; private set; }
        public double RelativeLengthDifference { get; private set; }
        public double AngleDegrees { get; private set; }
        public bool Matches { get; private set; }
    }

    private struct SheetPoint
    {
        public SheetPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; private set; }
        public double Y { get; private set; }

        public static SheetVector operator -(SheetPoint left, SheetPoint right)
            => new(left.X - right.X, left.Y - right.Y);
    }

    private struct SheetVector
    {
        public SheetVector(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; private set; }
        public double Y { get; private set; }

        public static SheetVector operator -(SheetVector left, SheetVector right)
            => new(left.X - right.X, left.Y - right.Y);

        public double Dot(SheetVector other) => (X * other.X) + (Y * other.Y);

        public double Length => Math.Sqrt((X * X) + (Y * Y));
    }
}
