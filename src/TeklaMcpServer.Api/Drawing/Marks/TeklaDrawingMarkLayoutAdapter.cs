using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Marks;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class TeklaDrawingMarkLayoutEntry
{
    public Mark Mark { get; set; } = null!;

    public int ViewId { get; set; }

    public MarkLayoutItem Item { get; set; } = null!;

    public double CenterX { get; set; }

    public double CenterY { get; set; }
}

internal static class TeklaDrawingMarkLayoutAdapter
{
    private const double MovementVerificationEpsilon = 0.05;
    private const double LayoutBoundsMargin = 10.0;
    private const double LeaderAnchorDepthMm = 10.0;
    private const double LeaderAnchorNoOpEpsilon = 0.5;

    public static List<TeklaDrawingMarkLayoutEntry> CollectEntries(
        View view,
        MarksViewContext marksViewContext,
        DrawingViewContext? viewContext = null)
    {
        var entries = new List<TeklaDrawingMarkLayoutEntry>();
        var contextsById = marksViewContext.Marks.ToDictionary(item => item.MarkId);
        var markEnum = view.GetAllObjects(typeof(Mark));
        while (markEnum.MoveNext())
        {
            if (markEnum.Current is not Mark mark)
                continue;

            var markId = mark.GetIdentifier().ID;
            if (!contextsById.TryGetValue(markId, out var markContext))
                continue;

            if (!TryCreateLayoutItem(markContext, marksViewContext, viewContext, out var item))
                continue;

            entries.Add(new TeklaDrawingMarkLayoutEntry
            {
                Mark = mark,
                ViewId = marksViewContext.ViewId ?? view.GetIdentifier().ID,
                CenterX = item.CurrentX,
                CenterY = item.CurrentY,
                Item = item
            });
        }

        return entries;
    }

    internal static bool TryCreateLayoutItem(
        MarkContext markContext,
        MarksViewContext marksViewContext,
        DrawingViewContext? viewContext,
        out MarkLayoutItem item)
    {
        item = null!;

        var center = markContext.CurrentCenter ?? markContext.Geometry.Center;
        var bounds = markContext.Geometry.Bounds;
        if (center == null || bounds == null)
            return false;

        if (markContext.Geometry.Width < 0.1 && markContext.Geometry.Height < 0.1)
            return false;

        var localCorners = markContext.Geometry.Corners
            .Select(corner => new[] { corner.X - center.X, corner.Y - center.Y })
            .ToList();

        var source = CreateSourceReference(markContext);
        var hasSourceCenter = MarkSourceResolver.TryResolveCenter(source, viewContext, out var sourceCenterX, out var sourceCenterY);
        var viewBounds = marksViewContext.ViewBounds;
        item = new MarkLayoutItem
        {
            Id = markContext.MarkId,
            AnchorX = markContext.Anchor?.X ?? center.X,
            AnchorY = markContext.Anchor?.Y ?? center.Y,
            CurrentX = center.X,
            CurrentY = center.Y,
            Width = markContext.Geometry.Width,
            Height = markContext.Geometry.Height,
            HasLeaderLine = markContext.HasLeaderLine,
            HasAxis = TryGetAxisDirection(markContext, out var axisDx, out var axisDy),
            AxisDx = axisDx,
            AxisDy = axisDy,
            CanMove = markContext.CanMove,
            LocalCorners = localCorners,
            BoundsMinX = (viewBounds?.MinX ?? 0.0) - LayoutBoundsMargin,
            BoundsMaxX = (viewBounds?.MaxX ?? 0.0) + LayoutBoundsMargin,
            BoundsMinY = (viewBounds?.MinY ?? 0.0) - LayoutBoundsMargin,
            BoundsMaxY = (viewBounds?.MaxY ?? 0.0) + LayoutBoundsMargin,
            SourceKind = source.Kind,
            SourceModelId = source.ModelId,
            SourceCenterX = hasSourceCenter ? sourceCenterX : null,
            SourceCenterY = hasSourceCenter ? sourceCenterY : null,
        };
        return true;
    }

    public static List<int> ApplyPlacements(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, MarkLayoutPlacement> placementsById,
        Model model)
    {
        var movedIds = new List<int>();

        foreach (var entry in entries)
        {
            var id = entry.Mark.GetIdentifier().ID;
            if (!placementsById.TryGetValue(id, out var placement))
                continue;

            var dx = placement.X - entry.CenterX;
            var dy = placement.Y - entry.CenterY;
            if (entry.Item.HasAxis && !entry.Item.HasLeaderLine)
            {
                var distanceAlongAxis = (dx * entry.Item.AxisDx) + (dy * entry.Item.AxisDy);
                dx = entry.Item.AxisDx * distanceAlongAxis;
                dy = entry.Item.AxisDy * distanceAlongAxis;
            }

            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
                continue;

            var beforeInsertion = entry.Mark.InsertionPoint;
            var beforeCenterX = entry.CenterX;
            var beforeCenterY = entry.CenterY;
            var insertionPoint = entry.Mark.InsertionPoint;
            insertionPoint.X += dx;
            insertionPoint.Y += dy;
            entry.Mark.InsertionPoint = insertionPoint;
            if (!entry.Mark.Modify())
                continue;

            if (!TryReloadMarkState(entry.Mark, entry.ViewId, model, out var actualInsertion, out var actualCenterX, out var actualCenterY))
                continue;

            var insertionChanged =
                Math.Abs(actualInsertion.X - beforeInsertion.X) > MovementVerificationEpsilon ||
                Math.Abs(actualInsertion.Y - beforeInsertion.Y) > MovementVerificationEpsilon;
            var centerChanged =
                Math.Abs(actualCenterX - beforeCenterX) > MovementVerificationEpsilon ||
                Math.Abs(actualCenterY - beforeCenterY) > MovementVerificationEpsilon;

            if (!insertionChanged && !centerChanged)
                continue;

            entry.CenterX = actualCenterX;
            entry.CenterY = actualCenterY;
            movedIds.Add(id);
        }

        return movedIds;
    }

    internal static List<int> OptimizeLeaderAnchors(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, List<double[]>> partPolygonsByModelId)
    {
        var updatedIds = new List<int>();

        foreach (var entry in entries)
        {
            if (!entry.Item.HasLeaderLine ||
                !entry.Item.SourceModelId.HasValue ||
                !partPolygonsByModelId.TryGetValue(entry.Item.SourceModelId.Value, out var polygon) ||
                entry.Mark.Placing is not LeaderLinePlacing leaderLinePlacing)
            {
                continue;
            }

            if (!LeaderAnchorResolver.TryResolveAnchorTarget(
                    polygon,
                    entry.CenterX,
                    entry.CenterY,
                    LeaderAnchorDepthMm,
                    out var targetX,
                    out var targetY))
                continue;

            if (Math.Abs(leaderLinePlacing.StartPoint.X - targetX) < LeaderAnchorNoOpEpsilon &&
                Math.Abs(leaderLinePlacing.StartPoint.Y - targetY) < LeaderAnchorNoOpEpsilon)
            {
                continue;
            }

            entry.Mark.Placing = new LeaderLinePlacing(new Point(targetX, targetY, 0));
            if (!entry.Mark.Modify())
                continue;

            entry.Item.AnchorX = targetX;
            entry.Item.AnchorY = targetY;
            updatedIds.Add(entry.Mark.GetIdentifier().ID);
        }

        return updatedIds;
    }

    private static bool TryReloadMarkState(
        Mark mark,
        int viewId,
        Model model,
        out Point insertionPoint,
        out double centerX,
        out double centerY)
    {
        insertionPoint = mark.InsertionPoint;
        centerX = 0.0;
        centerY = 0.0;
        try
        {
            var geometry = MarkGeometryResolver.Build(mark, model, viewId);
            insertionPoint = mark.InsertionPoint;
            centerX = geometry.CenterX;
            centerY = geometry.CenterY;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static MarkSourceReference CreateSourceReference(MarkContext markContext)
    {
        var hasSourceKind = Enum.TryParse<MarkLayoutSourceKind>(markContext.SourceKind, ignoreCase: true, out var sourceKind);
        return new MarkSourceReference(hasSourceKind ? sourceKind : MarkLayoutSourceKind.Unknown, markContext.ModelId);
    }

    private static bool TryGetAxisDirection(MarkContext markContext, out double axisDx, out double axisDy)
    {
        axisDx = 0.0;
        axisDy = 0.0;

        if (!string.Equals(markContext.PlacingType, nameof(BaseLinePlacing), StringComparison.Ordinal) ||
            markContext.Axis?.Direction == null)
        {
            return false;
        }

        axisDx = markContext.Axis.Direction.X;
        axisDy = markContext.Axis.Direction.Y;
        return Math.Abs(axisDx) >= 0.001 || Math.Abs(axisDy) >= 0.001;
    }
}
