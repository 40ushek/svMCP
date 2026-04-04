using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionTextBoxCandidate
{
    public string Owner { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<double[]> Polygon { get; set; } = [];
}

internal static class DimensionTextBoxCollector
{
    internal static List<DimensionTextBoxCandidate> Collect(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        FrameTypes frameType)
    {
        var results = new List<DimensionTextBoxCandidate>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        CollectFromOwner(results, seen, segment, "segment.objects", frameType);
        CollectFromOwner(results, seen, dimSet, "dimensionSet.objects", frameType);

        return results;
    }

    private static void CollectFromOwner(
        List<DimensionTextBoxCandidate> target,
        HashSet<string> seen,
        object owner,
        string ownerLabel,
        FrameTypes frameType)
    {
        foreach (var candidate in EnumerateNestedDrawingObjects(owner))
        {
            if (!TryCreateTextCandidate(candidate, ownerLabel, frameType, out var textCandidate))
                continue;

            if (seen.Add(BuildCandidateKey(textCandidate)))
                target.Add(textCandidate);
        }
    }

    private static string BuildCandidateKey(DimensionTextBoxCandidate candidate)
    {
        var polygonKey = string.Join(
            "|",
            candidate.Polygon.Select(static point => $"{point[0]:0.###},{point[1]:0.###}"));
        return $"{candidate.Type}::{candidate.Text}::{polygonKey}";
    }

    private static IEnumerable<object?> EnumerateNestedDrawingObjects(object owner)
    {
        if (!TryGetChildObjects(owner, out var children))
            yield break;

        while (children.MoveNext())
        {
            var child = children.Current;
            yield return child;

            if (child == null)
                continue;

            foreach (var nestedChild in EnumerateNestedDrawingObjects(child))
                yield return nestedChild;
        }
    }

    private static bool TryGetChildObjects(object owner, out DrawingObjectEnumerator children)
    {
        children = null!;

        try
        {
            var getObjectsMethod = owner.GetType().GetMethod(
                "GetObjects",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                binder: null,
                types: System.Type.EmptyTypes,
                modifiers: null);
            if (getObjectsMethod?.Invoke(owner, null) is not DrawingObjectEnumerator enumerator)
                return false;

            children = enumerator;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateTextCandidate(
        object? candidate,
        string ownerLabel,
        FrameTypes frameType,
        out DimensionTextBoxCandidate textCandidate)
    {
        textCandidate = new DimensionTextBoxCandidate();
        if (candidate == null)
            return false;

        try
        {
            if (candidate is Text text)
            {
                textCandidate = new DimensionTextBoxCandidate
                {
                    Owner = ownerLabel,
                    Type = text.GetType().FullName ?? text.GetType().Name,
                    Text = text.TextString ?? string.Empty,
                    Polygon = TeklaDrawingDimensionsApi.CreatePolygonFromObjectAlignedBox(
                        text.GetObjectAlignedBoundingBox(),
                        frameType)
                };
                return textCandidate.Polygon.Count >= 4;
            }

            var type = candidate.GetType();
            var textProperty = type.GetProperty(
                "TextString",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var looksLikeText = textProperty != null
                || type.Name.IndexOf("Text", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeText)
                return false;

            var objectAlignedMethod = type.GetMethod(
                "GetObjectAlignedBoundingBox",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (objectAlignedMethod?.Invoke(candidate, null) is not RectangleBoundingBox objectAlignedBoundingBox)
                return false;

            textCandidate = new DimensionTextBoxCandidate
            {
                Owner = ownerLabel,
                Type = type.FullName ?? type.Name,
                Text = textProperty?.GetValue(candidate, null)?.ToString() ?? string.Empty,
                Polygon = TeklaDrawingDimensionsApi.CreatePolygonFromObjectAlignedBox(objectAlignedBoundingBox, frameType)
            };
            return textCandidate.Polygon.Count >= 4;
        }
        catch
        {
            return false;
        }
    }
}
