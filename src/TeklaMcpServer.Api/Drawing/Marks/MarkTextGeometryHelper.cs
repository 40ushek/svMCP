using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public sealed class MarkTextBoxInfo
{
    public string Source { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Width { get; set; }
    public double Height { get; set; }
    public double AngleToAxis { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public List<double[]> Corners { get; set; } = new();
}

public static class MarkTextGeometryHelper
{
    public static List<MarkTextBoxInfo> CollectTextBoxes(Mark mark)
    {
        var results = new List<MarkTextBoxInfo>();
        var visited = new HashSet<int>();
        CollectFromChildren(mark.GetObjects(), "mark.objects", results, visited, depth: 0);
        return results;
    }

    private static void CollectFromChildren(
        DrawingObjectEnumerator? enumerator,
        string source,
        List<MarkTextBoxInfo> results,
        HashSet<int> visited,
        int depth)
    {
        if (enumerator == null || depth > 4)
            return;

        while (enumerator.MoveNext())
            CollectFromObject(enumerator.Current, source, results, visited, depth);
    }

    private static void CollectFromObject(
        object? candidate,
        string source,
        List<MarkTextBoxInfo> results,
        HashSet<int> visited,
        int depth)
    {
        if (candidate == null)
            return;

        var visitId = RuntimeHelpers.GetHashCode(candidate);
        if (!visited.Add(visitId))
            return;

        if (TryCreateTextBox(candidate, source, out var textBox))
            results.Add(textBox);

        if (depth >= 4)
            return;

        try
        {
            var getObjectsMethod = candidate.GetType().GetMethod(
                "GetObjects",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (getObjectsMethod?.Invoke(candidate, null) is DrawingObjectEnumerator childEnumerator)
                CollectFromChildren(childEnumerator, $"{source}>{candidate.GetType().Name}", results, visited, depth + 1);
        }
        catch
        {
            // Ignore objects that do not expose recursive child enumeration.
        }
    }

    private static bool TryCreateTextBox(object candidate, string source, out MarkTextBoxInfo textBox)
    {
        textBox = new MarkTextBoxInfo();

        try
        {
            if (candidate is Text text)
            {
                textBox = CreateTextBox(text.GetObjectAlignedBoundingBox(), source, text.GetType().Name, text.TextString);
                return true;
            }

            var type = candidate.GetType();
            var textProperty = type.GetProperty(
                "TextString",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var looksLikeText = textProperty != null
                || type.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeText)
                return false;

            var objectAlignedMethod = type.GetMethod(
                "GetObjectAlignedBoundingBox",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (objectAlignedMethod?.Invoke(candidate, null) is not RectangleBoundingBox objectAlignedBoundingBox)
                return false;

            textBox = CreateTextBox(
                objectAlignedBoundingBox,
                source,
                type.Name,
                textProperty?.GetValue(candidate, null)?.ToString() ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MarkTextBoxInfo CreateTextBox(
        RectangleBoundingBox box,
        string source,
        string objectType,
        string text)
    {
        return new MarkTextBoxInfo
        {
            Source = source,
            ObjectType = objectType,
            Text = text,
            Width = Math.Round(box.Width, 2),
            Height = Math.Round(box.Height, 2),
            AngleToAxis = Math.Round(box.AngleToAxis, 2),
            CenterX = Math.Round((box.MinPoint.X + box.MaxPoint.X) / 2.0, 2),
            CenterY = Math.Round((box.MinPoint.Y + box.MaxPoint.Y) / 2.0, 2),
            MinX = Math.Round(box.MinPoint.X, 2),
            MinY = Math.Round(box.MinPoint.Y, 2),
            MaxX = Math.Round(box.MaxPoint.X, 2),
            MaxY = Math.Round(box.MaxPoint.Y, 2),
            Corners = new List<double[]>
            {
                new[] { Math.Round(box.LowerLeft.X, 2), Math.Round(box.LowerLeft.Y, 2) },
                new[] { Math.Round(box.UpperLeft.X, 2), Math.Round(box.UpperLeft.Y, 2) },
                new[] { Math.Round(box.UpperRight.X, 2), Math.Round(box.UpperRight.Y, 2) },
                new[] { Math.Round(box.LowerRight.X, 2), Math.Round(box.LowerRight.Y, 2) }
            }
        };
    }
}
