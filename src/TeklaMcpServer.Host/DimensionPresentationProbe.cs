using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;
using DrawingView = Tekla.Structures.Drawing.View;
using PresentationView = Tekla.Structures.DrawingPresentationModel.View;

namespace TeklaMcpServer.Host;

/// <summary>
/// Read-only probe for the geometry Tekla exposes for straight dimensions.
/// It deliberately does not create drawing objects or debug overlays.
/// </summary>
internal sealed class DimensionPresentationProbe
{
    private readonly List<string> _lines = [];

    public void Run()
    {
        // The log is written whatever fails, including the steps before the read loop
        // (no active drawing, selection lookup), so a failed run still leaves a log.
        try
        {
            RunCore();
        }
        finally
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"TeklaMcpServer.DimensionPresentationProbe.{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(path, _lines);
            Console.WriteLine($"Dimension presentation probe written to {path}");
        }
    }

    private void RunCore()
    {
        var drawingHandler = new DrawingHandler();
        var drawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing.");
        var selectedView = TryResolveSelectedView(drawingHandler);
        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        using var traceListener = new TextWriterTraceListener(Console.Out);
        Trace.Listeners.Add(traceListener);
        Trace.AutoFlush = true;

        try
        {
            DrawingEnumeratorBase.AutoFetch = false;
            Log($"Drawing: {drawing.Name}");
            Log(selectedView == null
                ? "Selected view: none"
                : $"Selected view: id={selectedView.GetIdentifier().ID}, name={selectedView.Name}, type={selectedView.ViewType}");

            using var connection = new PresentationConnection();
            ReadCurrentPresentationModel(connection, selectedView);

            var dimensions = drawing.GetSheet().GetAllObjects(typeof(StraightDimensionSet));
            var allDimensions = new List<(StraightDimensionSet Set, int? OwnerViewId, string OwnerViewType)>();
            var sheetDimensionCount = 0;
            var ownerlessDimensionCount = 0;

            while (dimensions.MoveNext())
            {
                if (dimensions.Current is not StraightDimensionSet dimensionSet)
                    continue;

                sheetDimensionCount++;
                var ownerView = dimensionSet.GetView();
                var ownerViewId = ownerView?.GetIdentifier().ID;
                if (!ownerViewId.HasValue)
                    ownerlessDimensionCount++;
                var ownerType = ownerViewId.HasValue
                    ? $"{ownerViewId.Value}:{ownerView!.GetType().Name}"
                    : "<unknown>";
                allDimensions.Add((dimensionSet, ownerViewId, ownerType));
            }

            var selectedViewDimensions = selectedView == null
                ? []
                : allDimensions.Where(item => item.OwnerViewId == selectedView.GetIdentifier().ID).ToList();
            // DIMENSION_PROBE_ALL=1 reads every dimension on the sheet (for comparing Distance
            // with the drawn line across views and scales); by default only the selected view.
            var readAll = Environment.GetEnvironmentVariable("DIMENSION_PROBE_ALL") == "1";
            var sampleDimensions = readAll
                ? allDimensions
                : selectedViewDimensions.Count > 0
                    ? selectedViewDimensions
                    : allDimensions
                        .OrderByDescending(item => item.Set.GetIdentifier().ID)
                        .Take(3)
                        .ToList();

            Log($"StraightDimensionSets on sheet: {sheetDimensionCount}");
            Log($"StraightDimensionSets owned by selected view: {selectedViewDimensions.Count}");
            Log($"StraightDimensionSets with unresolved owner view: {ownerlessDimensionCount}");
            if (selectedViewDimensions.Count == 0)
                Log("No dimensions matched the selected view; sampling the three highest dimension-set IDs.");

            foreach (var item in sampleDimensions)
            {
                var dimensionSet = item.Set;
                var setId = dimensionSet.GetIdentifier().ID;
                Log($"DimensionSet id={setId}, ownerView={item.OwnerViewType}");
                ReadPresentation(connection, setId, "set");

                var segments = dimensionSet.GetObjects();
                while (segments.MoveNext())
                {
                    if (segments.Current is not StraightDimension segment)
                        continue;

                    var segmentId = segment.GetIdentifier().ID;
                    Log($"  DimensionSegment id={segmentId}");
                    ReadPresentation(connection, segmentId, "segment");
                }
            }
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
            Trace.Listeners.Remove(traceListener);
        }
    }

    private static DrawingView? TryResolveSelectedView(DrawingHandler drawingHandler)
    {
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is DrawingView view)
                return view;

            if (selected.Current is StraightDimensionSet dimensionSet
                && dimensionSet.GetView() is DrawingView dimensionView)
                return dimensionView;

            if (selected.Current is StraightDimension dimension
                && dimension.GetView() is DrawingView segmentView)
                return segmentView;
        }

        return null;
    }

    private void ReadPresentation(PresentationConnection connection, int objectId, string label)
    {
        try
        {
            var segment = connection.Service.GetObjectPresentation(objectId);
            if (segment == null)
            {
                Log($"    {label}: presentation segment=null");
                return;
            }

            if (segment.Primitives == null)
            {
                Log($"    {label}: presentation segment exists, primitives=null");
                return;
            }

            if (segment.Primitives.Count == 0)
            {
                Log($"    {label}: presentation segment exists, primitives=0");
                return;
            }

            Log($"    {label}: primitiveCount={segment.Primitives.Count}");
            ReadPrimitives(segment.Primitives, 3);
        }
        catch (Exception ex)
        {
            Log($"    {label}: GetObjectPresentation failed: {ex.Message}");
        }
    }

    private void ReadCurrentPresentationModel(PresentationConnection connection, DrawingView? selectedView)
    {
        try
        {
            var views = connection.Service.GetCurrentPresentationModel()?.ToList();
            if (views == null)
            {
                Log("Current presentation model: null");
                return;
            }

            Log($"Current presentation model views: {views.Count}");
            foreach (PresentationView view in views)
            {
                Log($"PresentationView id={view.Id}, primitives={view.Primitives?.Count ?? 0}");
                if (selectedView != null && view.Id == selectedView.GetIdentifier().ID && view.Primitives != null)
                    ReadPrimitives(view.Primitives, 1);
            }
        }
        catch (Exception ex)
        {
            Log($"Current presentation model read failed: {ex.Message}");
        }
    }

    private void ReadPrimitives(IList<PrimitiveBase> primitives, int depth)
    {
        if (depth > 8)
            return;

        var indent = new string(' ', depth * 2);
        foreach (var primitive in primitives)
        {
            switch (primitive)
            {
                case LinePrimitive line:
                    Log(FormattableString.Invariant(
                        $"{indent}LinePrimitive ({line.StartPoint.X:0.###},{line.StartPoint.Y:0.###}) -> ({line.EndPoint.X:0.###},{line.EndPoint.Y:0.###})"));
                    break;
                case TextPrimitive text:
                    Log(FormattableString.Invariant(
                        $"{indent}TextPrimitive pos=({text.Position.X:0.###},{text.Position.Y:0.###}) angle={text.Angle:0.###} text=\"{text.Text}\""));
                    break;
                case Segment nested:
                    Log($"{indent}Segment id={nested.Id}, objectType={nested.ObjectType}, layer={nested.Layer}, count={nested.Primitives?.Count ?? 0}");
                    if (nested.Primitives != null)
                        ReadPrimitives(nested.Primitives, depth + 1);
                    break;
                case PrimitiveGroup group:
                    Log($"{indent}PrimitiveGroup count={group.Primitives?.Count ?? 0}");
                    if (group.Primitives != null)
                        ReadPrimitives(group.Primitives, depth + 1);
                    break;
                case PolygonPrimitive polygon:
                    Log($"{indent}PolygonPrimitive");
                    if (polygon.OuterLoop != null)
                        ReadPrimitives([polygon.OuterLoop], depth + 1);
                    if (polygon.InnerLoops != null)
                        foreach (var loop in polygon.InnerLoops)
                            ReadPrimitives([loop], depth + 1);
                    break;
                default:
                    Log($"{indent}{primitive?.GetType().Name ?? "null"}");
                    break;
            }
        }
    }

    private void Log(string line) => _lines.Add(line);
}
