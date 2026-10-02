using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Raw bolt evidence captured with the view context, not dimension candidates.</summary>
internal sealed class ViewBoltGeometrySnapshot
{
    public JsonElement Answer { get; }

    private ViewBoltGeometrySnapshot(object answer) => Answer = JsonSerializer.SerializeToElement(answer, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    internal static ViewBoltGeometrySnapshot Capture(int viewId, IEnumerable<int> partIds,
        Func<int, PartBoltGeometryInViewResult> read, DepthBox? restrictionBox,
        Func<double[], double[]> toRestrictionCoordinates, bool partSelectionComplete,
        string? restrictionError = null)
    {
        var requested = partIds.Distinct().OrderBy(id => id).ToArray();
        var groups = new SortedDictionary<int, BoltGroupGeometry>();
        var unread = new List<object>();
        foreach (var partId in requested)
        {
            try
            {
                var result = read(partId);
                if (!result.Success || result.ViewId != viewId || result.PartId != partId)
                {
                    unread.Add(new { partId, reason = result.Error ?? "Bolt geometry read failed or returned a different source" });
                    continue;
                }
                foreach (var group in result.BoltGroups)
                {
                    if (group.ModelId <= 0 || group.Positions.Count == 0
                        || group.Positions.Any(position => !ValidPoint(position.Point))
                        || new[] { group.FirstPosition, group.SecondPosition, group.BboxMin, group.BboxMax }
                            .SelectMany(point => point).Any(value => double.IsNaN(value) || double.IsInfinity(value)))
                        throw new InvalidOperationException($"Bolt group {group.ModelId} has invalid or missing geometry");
                    if (!groups.ContainsKey(group.ModelId)) groups.Add(group.ModelId, group);
                }
            }
            catch (Exception ex) { unread.Add(new { partId, reason = ex.Message }); }
        }

        var rows = groups.Values.Select(group => new {
            geometry = group,
            restriction = group.Positions.Select(position => new {
                index = position.Index,
                centerState = ClassifyCenter(position.Point, restrictionBox, toRestrictionCoordinates)
            }).ToArray()
        }).ToArray();
        return new ViewBoltGeometrySnapshot(new {
            scope = "related-to-included-parts", coordinateSystem = "display",
            isComplete = partSelectionComplete && unread.Count == 0,
            partSelectionComplete, requestedPartIds = requested, unread,
            boltGroups = rows, restrictionError,
            boundaryTolerance = DepthBox.BoundaryTolerance,
            selectionComplete = false, visibilityVerified = false,
            selectionStatus = "raw geometry with center-only restriction evidence; axial extent and occlusion are not verified"
        });
    }

    private static string ClassifyCenter(double[] point, DepthBox? box,
        Func<double[], double[]> transform)
    {
        if (!box.HasValue || !box.Value.IsValid || !ValidPoint(point)) return "Unresolved";
        double[] local;
        try { local = transform(point); }
        catch { return "Unresolved"; }
        if (!ValidPoint(local)) return "Unresolved";
        var b = box.Value;
        var t = DepthBox.BoundaryTolerance;
        return local[0] >= b.MinX - t && local[0] <= b.MaxX + t
            && local[1] >= b.MinY - t && local[1] <= b.MaxY + t
            && local[2] >= b.MinZ - t && local[2] <= b.MaxZ + t
                ? "Inside" : "Outside";
    }

    private static bool ValidPoint(double[] point) => point.Length == 3
        && point.All(value => !double.IsNaN(value) && !double.IsInfinity(value));
}
