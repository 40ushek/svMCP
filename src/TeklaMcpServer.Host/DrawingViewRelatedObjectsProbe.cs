using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Host;

/// <summary>
/// Read-only diagnostic for the Open API forum workaround: a drawing part that Tekla
/// renders may have related drawing objects. This does not make a visibility decision.
/// </summary>
internal sealed class DrawingViewRelatedObjectsProbe
{
    private readonly List<string> _lines = [];

    public void Run()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing()
            ?? throw new InvalidOperationException("No active drawing. Open a drawing in Tekla and try again.");
        var view = ResolveTargetView(drawingHandler, activeDrawing);
        var model = new Model();

        Log($"Drawing: {activeDrawing.Name}");
        Log($"View: id={view.GetIdentifier().ID}, name={view.Name}, type={view.ViewType}");
        Log("Columns: drawingPartId | modelId | partPos | hidden | relatedCount | relatedTypes");

        var parts = new List<DrawingPart>();
        var objects = view.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is DrawingPart part)
                parts.Add(part);
        }

        Log($"Drawing parts returned by View.GetObjects(): {parts.Count}");
        foreach (var part in parts.OrderBy(part => part.ModelIdentifier.ID))
            Log(DescribePart(model, part));

        var path = Path.Combine(Path.GetTempPath(), "TeklaMcpServer.ViewRelatedObjectsProbe.log");
        File.WriteAllLines(path, _lines);
        Console.WriteLine($"View related-objects probe written to {path}");
    }

    private static View ResolveTargetView(DrawingHandler drawingHandler, Drawing activeDrawing)
    {
        var selected = drawingHandler.GetDrawingObjectSelector().GetSelected();
        while (selected.MoveNext())
        {
            if (selected.Current is View selectedView)
                return selectedView;
        }

        var views = new List<View>();
        var viewEnumerator = activeDrawing.GetSheet().GetViews();
        while (viewEnumerator.MoveNext())
        {
            if (viewEnumerator.Current is View view)
                views.Add(view);
        }

        if (views.Count == 1)
            return views[0];
        if (views.Count == 0)
            throw new InvalidOperationException("No views found in the active drawing.");

        throw new InvalidOperationException(
            $"Active drawing contains {views.Count} views. Select one view in the drawing editor and run the host again.");
    }

    private static string DescribePart(Model model, DrawingPart drawingPart)
    {
        var modelId = drawingPart.ModelIdentifier.ID;
        var drawingId = TryGetDrawingId(drawingPart);
        try
        {
            if (!drawingPart.Select())
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0} | {1} | <unreadable> | <unreadable> | <unreadable> | drawing part Select() returned false",
                    drawingId,
                    modelId);
            }
        }
        catch (Exception exception)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | {1} | <unreadable> | <unreadable> | <unreadable> | drawing part Select() failed: {2}",
                drawingId,
                modelId,
                exception.Message);
        }

        var hidden = TryReadHidden(drawingPart, out var isHidden, out var hiddenError)
            ? isHidden.ToString()
            : $"<unreadable: {hiddenError}>";
        var partPos = ReadPartPos(model, drawingPart.ModelIdentifier);

        try
        {
            var related = drawingPart.GetRelatedObjects();
            var descriptions = new List<string>();
            while (related.MoveNext())
            {
                if (related.Current == null)
                {
                    descriptions.Add("<null>");
                    continue;
                }

                descriptions.Add(DescribeRelatedObject(related.Current));
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3} | {4} | {5}",
                drawingId,
                modelId,
                partPos,
                hidden,
                descriptions.Count,
                descriptions.Count == 0 ? "<empty>" : string.Join(", ", descriptions));
        }
        catch (Exception exception)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3} | <error> | {4}",
                drawingId,
                modelId,
                partPos,
                hidden,
                exception.Message);
        }
    }

    private static string TryGetDrawingId(DrawingPart part)
    {
        try
        {
            return part.GetIdentifier().ID.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static bool TryReadHidden(DrawingPart part, out bool isHidden, out string? error)
    {
        try
        {
            isHidden = part.Hideable.IsHidden;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            isHidden = false;
            error = exception.Message;
            return false;
        }
    }

    private static string ReadPartPos(Model model, Identifier identifier)
    {
        try
        {
            if (model.SelectModelObject(identifier) is not ModelPart part)
                return "<missing-model-part>";

            var partPos = string.Empty;
            return part.GetReportProperty("PART_POS", ref partPos)
                ? string.IsNullOrWhiteSpace(partPos) ? "<empty>" : partPos
                : "<unreadable>";
        }
        catch (Exception exception)
        {
            return $"<error: {exception.Message}>";
        }
    }

    private static string DescribeRelatedObject(object related)
    {
        var typeName = related.GetType().Name;
        if (related is not DatabaseObject databaseObject)
            return typeName;

        try
        {
            return $"{typeName}#{databaseObject.GetIdentifier().ID}";
        }
        catch
        {
            return typeName;
        }
    }

    private void Log(string line) => _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");
}
