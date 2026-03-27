using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal enum ViewPlacementBlockerKind
{
    ReservedArea,
    View
}

internal readonly struct ViewPlacementBlocker
{
    public ViewPlacementBlocker(ViewPlacementBlockerKind kind, ReservedRect rect, int? viewId = null)
    {
        Kind = kind;
        Rect = rect;
        ViewId = viewId;
    }

    public ViewPlacementBlockerKind Kind { get; }
    public ReservedRect Rect { get; }
    public int? ViewId { get; }
}

internal readonly struct ViewPlacementValidationResult
{
    public ViewPlacementValidationResult(bool fits, string reason, IReadOnlyList<ViewPlacementBlocker>? blockers = null)
    {
        Fits = fits;
        Reason = reason;
        Blockers = blockers ?? System.Array.Empty<ViewPlacementBlocker>();
    }

    public bool Fits { get; }
    public string Reason { get; }
    public IReadOnlyList<ViewPlacementBlocker> Blockers { get; }
}

internal static class ViewPlacementValidator
{
    public static ViewPlacementValidationResult Validate(
        ReservedRect rect,
        double minX,
        double maxX,
        double minY,
        double maxY,
        IReadOnlyList<ReservedRect>? reservedAreas = null,
        IReadOnlyList<ReservedRect>? otherViewRects = null)
    {
        if (!IsWithinArea(rect, minX, maxX, minY, maxY))
            return new ViewPlacementValidationResult(false, "out-of-bounds");

        var reservedBlockers = GetReservedBlockers(rect, reservedAreas);
        if (reservedBlockers.Count > 0)
            return new ViewPlacementValidationResult(false, "reserved-overlap", reservedBlockers);

        var viewBlockers = GetViewBlockers(rect, otherViewRects);
        if (viewBlockers.Count > 0)
            return new ViewPlacementValidationResult(false, "view-overlap", viewBlockers);

        return new ViewPlacementValidationResult(true, string.Empty);
    }

    public static ViewPlacementValidationResult Validate(
        ReservedRect rect,
        double minX,
        double maxX,
        double minY,
        double maxY,
        IReadOnlyList<ReservedRect>? reservedAreas,
        IReadOnlyDictionary<int, ReservedRect>? otherViewRectsById)
    {
        if (!IsWithinArea(rect, minX, maxX, minY, maxY))
            return new ViewPlacementValidationResult(false, "out-of-bounds");

        var reservedBlockers = GetReservedBlockers(rect, reservedAreas);
        if (reservedBlockers.Count > 0)
            return new ViewPlacementValidationResult(false, "reserved-overlap", reservedBlockers);

        var viewBlockers = GetViewBlockers(rect, otherViewRectsById);
        if (viewBlockers.Count > 0)
            return new ViewPlacementValidationResult(false, "view-overlap", viewBlockers);

        return new ViewPlacementValidationResult(true, string.Empty);
    }

    public static bool IntersectsAny(ReservedRect rect, IReadOnlyList<ReservedRect> others)
        => others.Any(other => Intersects(rect, other));

    public static bool Intersects(ReservedRect a, ReservedRect b)
        => !(a.MaxX <= b.MinX || b.MaxX <= a.MinX || a.MaxY <= b.MinY || b.MaxY <= a.MinY);

    public static bool IsWithinArea(ReservedRect rect, double minX, double maxX, double minY, double maxY)
        => rect.MinX >= minX && rect.MaxX <= maxX && rect.MinY >= minY && rect.MaxY <= maxY;

    private static List<ViewPlacementBlocker> GetReservedBlockers(
        ReservedRect rect,
        IReadOnlyList<ReservedRect>? reservedAreas)
    {
        if (reservedAreas == null || reservedAreas.Count == 0)
            return new List<ViewPlacementBlocker>();

        return reservedAreas
            .Where(area => Intersects(rect, area))
            .Select(area => new ViewPlacementBlocker(ViewPlacementBlockerKind.ReservedArea, area))
            .ToList();
    }

    private static List<ViewPlacementBlocker> GetViewBlockers(
        ReservedRect rect,
        IReadOnlyList<ReservedRect>? otherViewRects)
    {
        if (otherViewRects == null || otherViewRects.Count == 0)
            return new List<ViewPlacementBlocker>();

        return otherViewRects
            .Where(other => Intersects(rect, other))
            .Select(other => new ViewPlacementBlocker(ViewPlacementBlockerKind.View, other))
            .ToList();
    }

    private static List<ViewPlacementBlocker> GetViewBlockers(
        ReservedRect rect,
        IReadOnlyDictionary<int, ReservedRect>? otherViewRectsById)
    {
        if (otherViewRectsById == null || otherViewRectsById.Count == 0)
            return new List<ViewPlacementBlocker>();

        return otherViewRectsById
            .Where(other => Intersects(rect, other.Value))
            .Select(other => new ViewPlacementBlocker(ViewPlacementBlockerKind.View, other.Value, other.Key))
            .ToList();
    }
}
