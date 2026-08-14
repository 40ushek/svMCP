using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Adapts the already-read structural outline to the geometry-only input of
/// <see cref="CalcDimensionChains"/>. It makes no Tekla call and keeps model-specific
/// details at this boundary.
/// </summary>
public static class StructuralGeometryGroupBuilder
{
    public static GeometryGroup Build(StructuralOutline structuralOutline, string id = "structural")
    {
        if (structuralOutline == null)
            throw new ArgumentNullException(nameof(structuralOutline));

        var issues = Issues(structuralOutline).ToList();
        var boundaries = BuildRings("structural-boundary", structuralOutline.Outline.AssemblyNodes, issues);
        var shapes = structuralOutline.Outline.PartNodes
            .OrderBy(part => part.Key)
            .SelectMany(part => BuildRings(
                "defining-part:" + part.Key.ToString(CultureInfo.InvariantCulture),
                part.Value,
                issues,
                part.Key))
            .ToList();

        return new GeometryGroup(id, boundaries, shapes, new GeometryGroupCompleteness(issues));
    }

    /// <summary>
    /// Converts every ring without silently repairing malformed input. A usable remainder
    /// may still become geometry, but the caller receives an issue so it cannot mistake a
    /// partial contour for a complete one.
    /// </summary>
    internal static IReadOnlyList<GeometryGroupShape> BuildRings(
        string sourceId,
        IReadOnlyList<OutlineTreeNodeResult> nodes,
        ICollection<GeometryGroupSourceIssue> issues,
        int? modelId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("A contour source needs an id.", nameof(sourceId));
        if (nodes == null)
            throw new ArgumentNullException(nameof(nodes));
        if (issues == null)
            throw new ArgumentNullException(nameof(issues));

        var shapes = new List<GeometryGroupShape>();
        for (var index = 0; index < nodes.Count; index++)
            AddRing(sourceId, nodes[index], depth: 0, index.ToString(CultureInfo.InvariantCulture), shapes, issues, modelId);

        return shapes;
    }

    private static void AddRing(
        string sourceId,
        OutlineTreeNodeResult node,
        int depth,
        string path,
        ICollection<GeometryGroupShape> shapes,
        ICollection<GeometryGroupSourceIssue> issues,
        int? modelId)
    {
        if (node == null)
            throw new ArgumentException("A contour tree cannot contain a null ring.", nameof(node));

        var ringId = sourceId + ":ring:" + path;
        var malformedPointCount = node.Polygon.Count(point => point == null || point.Length < 2);
        var points = node.Polygon
            .Where(point => point != null && point.Length >= 2)
            .Select(point => new Vec3(point[0], point[1], 0))
            .ToList();
        var shape = RegionFlattener.Flatten(points);

        if (malformedPointCount > 0 || shape.Kind == PlanarShapeKind.Empty)
        {
            var reason = malformedPointCount == 0
                ? "ring has no usable planar point"
                : "discarded " + malformedPointCount.ToString(CultureInfo.InvariantCulture) +
                  " point(s) without an X/Y coordinate";
            if (shape.Kind == PlanarShapeKind.Empty && malformedPointCount > 0)
                reason += "; ring has no usable planar point";
            issues.Add(new GeometryGroupSourceIssue(ringId, reason));
        }

        shapes.Add(new GeometryGroupShape(
            ringId,
            shape,
            isHole: (depth & 1) == 1,
            modelId: modelId));

        for (var index = 0; index < node.Children.Count; index++)
            AddRing(sourceId, node.Children[index], depth + 1,
                path + "." + index.ToString(CultureInfo.InvariantCulture), shapes, issues, modelId);
    }

    private static IReadOnlyList<GeometryGroupSourceIssue> Issues(StructuralOutline structuralOutline)
    {
        var issues = new List<GeometryGroupSourceIssue>();

        foreach (var unread in structuralOutline.UnreadRoles)
            issues.Add(new GeometryGroupSourceIssue("role:" + unread.ModelId.ToString(CultureInfo.InvariantCulture), unread.Reason));

        foreach (var unknown in structuralOutline.Unknown)
            issues.Add(new GeometryGroupSourceIssue("role:" + unknown.ModelId.ToString(CultureInfo.InvariantCulture), "matched no role rule"));

        foreach (var unclassified in structuralOutline.Unclassified)
            issues.Add(new GeometryGroupSourceIssue("role:" + unclassified.ModelId.ToString(CultureInfo.InvariantCulture), "was never classified"));

        foreach (var missing in structuralOutline.Outline.NotVisibleRequestedIds)
        {
            issues.Add(new GeometryGroupSourceIssue(
                "outline:" + missing.ToString(CultureInfo.InvariantCulture),
                "was requested as defining but is not visible in this view"));
        }

        foreach (var unread in structuralOutline.Outline.Unread)
            issues.Add(new GeometryGroupSourceIssue("outline:" + unread.ModelId.ToString(CultureInfo.InvariantCulture), unread.Reason));

        if (structuralOutline.Defining.Count == 0)
            issues.Add(new GeometryGroupSourceIssue("structural", "no part is classified as defining"));

        if (!string.IsNullOrWhiteSpace(structuralOutline.Outline.Error))
            issues.Add(new GeometryGroupSourceIssue("structural-outline", structuralOutline.Outline.Error!));

        return issues;
    }
}
