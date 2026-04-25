using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class TeklaDrawingMarkLayoutEntry
{
    public Mark Mark { get; set; } = null!;

    public MarkContext MarkContext { get; set; } = null!;

    public int ViewId { get; set; }

    public double ViewScale { get; set; }

    public MarkLayoutItem Item { get; set; } = null!;

    public double CenterX { get; set; }

    public double CenterY { get; set; }
}

internal sealed class LeaderAnchorOptimizationResult
{
    public List<int> AcceptedIds { get; } = [];

    public List<int> RejectedIds { get; } = [];
}

internal sealed class PendingLeaderAnchorOptimization
{
    public TeklaDrawingMarkLayoutEntry Entry { get; set; } = null!;

    public int MarkId { get; set; }

    public double OldAnchorX { get; set; }

    public double OldAnchorY { get; set; }

    public double TargetX { get; set; }

    public double TargetY { get; set; }

    public double BeforeCenterX { get; set; }

    public double BeforeCenterY { get; set; }

    public Point BeforeInsertion { get; set; } = null!;

    public double ActualCenterX { get; set; }

    public double ActualCenterY { get; set; }

    public double FinalCenterX { get; set; }

    public double FinalCenterY { get; set; }

    public double BodyShiftX => ActualCenterX - BeforeCenterX;

    public double BodyShiftY => ActualCenterY - BeforeCenterY;

    public bool Reverted { get; set; }

    public bool RestoredBody { get; set; }

    public bool CompensatedBody { get; set; }
}

internal static class TeklaDrawingMarkLayoutAdapter
{
    private const double MovementVerificationEpsilon = 0.05;
    private const double LayoutBoundsMargin = 10.0;
    private const double LeaderAnchorDepthPaperMm = 10.0;
    private const double LeaderAnchorFarEdgeClearancePaperMm = 5.0;
    private const double LeaderAnchorNoOpEpsilon = 0.5;
    private const double LeaderLengthRegressionEpsilon = 0.01;

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
                MarkContext = markContext,
                ViewId = marksViewContext.ViewId ?? view.GetIdentifier().ID,
                ViewScale = marksViewContext.ViewScale,
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
        Model model,
        bool respectAxisConstraint = true)
    {
        var movedIds = new List<int>();

        foreach (var entry in entries)
        {
            var id = entry.Mark.GetIdentifier().ID;
            if (!placementsById.TryGetValue(id, out var placement))
                continue;

            var dx = placement.X - entry.CenterX;
            var dy = placement.Y - entry.CenterY;
            if (respectAxisConstraint && entry.Item.HasAxis && !entry.Item.HasLeaderLine)
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

            PerfTrace.Write(
                "api-mark",
                "mark_apply_placement",
                0,
                $"markId={id} requestedCenter=({placement.X:F1},{placement.Y:F1}) beforeCenter=({beforeCenterX:F1},{beforeCenterY:F1}) actualCenter=({actualCenterX:F1},{actualCenterY:F1}) requestedDelta=({dx:F1},{dy:F1}) actualDelta=({actualCenterX - beforeCenterX:F1},{actualCenterY - beforeCenterY:F1}) placingType={entry.Mark.Placing?.GetType().Name ?? "null"}");

            entry.CenterX = actualCenterX;
            entry.CenterY = actualCenterY;
            movedIds.Add(id);
        }

        return movedIds;
    }

    internal static LeaderAnchorOptimizationResult OptimizeLeaderAnchors(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        IReadOnlyDictionary<int, List<double[]>> partPolygonsByModelId,
        Model model,
        Tekla.Structures.Drawing.Drawing drawing)
    {
        var result = new LeaderAnchorOptimizationResult();
        var pending = new List<PendingLeaderAnchorOptimization>();

        foreach (var entry in entries)
        {
            if (!entry.Item.HasLeaderLine ||
                !entry.Item.SourceModelId.HasValue ||
                !partPolygonsByModelId.TryGetValue(entry.Item.SourceModelId.Value, out var polygon) ||
                entry.Mark.Placing is not LeaderLinePlacing leaderLinePlacing)
            {
                continue;
            }

            if (!TryResolvePreferredLeaderAnchorTarget(
                    entry.MarkContext,
                    entry.CenterX,
                    entry.CenterY,
                    entry.ViewScale,
                    polygon,
                    out var targetX,
                    out var targetY))
                continue;

            var markId = entry.Mark.GetIdentifier().ID;
            var oldAnchorX = leaderLinePlacing.StartPoint.X;
            var oldAnchorY = leaderLinePlacing.StartPoint.Y;
            if (Math.Abs(oldAnchorX - targetX) < LeaderAnchorNoOpEpsilon &&
                Math.Abs(oldAnchorY - targetY) < LeaderAnchorNoOpEpsilon)
            {
                continue;
            }

            var beforeCenterX = entry.CenterX;
            var beforeCenterY = entry.CenterY;
            var beforeInsertion = entry.Mark.InsertionPoint;
            entry.Mark.Placing = new LeaderLinePlacing(new Point(targetX, targetY, 0));
            entry.Mark.InsertionPoint = new Point(beforeInsertion.X, beforeInsertion.Y, beforeInsertion.Z);
            if (!entry.Mark.Modify())
                continue;

            pending.Add(new PendingLeaderAnchorOptimization
            {
                Entry = entry,
                MarkId = markId,
                OldAnchorX = oldAnchorX,
                OldAnchorY = oldAnchorY,
                TargetX = targetX,
                TargetY = targetY,
                BeforeCenterX = beforeCenterX,
                BeforeCenterY = beforeCenterY,
                BeforeInsertion = beforeInsertion,
            });
        }

        if (pending.Count == 0)
            return result;

        drawing.CommitChanges("(MCP) Optimize leader anchors");

        var rejected = new List<PendingLeaderAnchorOptimization>();
        var shifted = new List<PendingLeaderAnchorOptimization>();
        foreach (var item in pending)
        {
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model, out var actualMark, out _, out var actualCenterX, out var actualCenterY))
            {
                PerfTrace.Write(
                    "api-mark",
                    "mark_optimize_leader_anchor",
                    0,
                    $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) newAnchor=({item.TargetX:F1},{item.TargetY:F1}) reload=False");
                item.Entry.Item.AnchorX = item.TargetX;
                item.Entry.Item.AnchorY = item.TargetY;
                result.AcceptedIds.Add(item.MarkId);
                continue;
            }

            item.Entry.Mark = actualMark;
            item.ActualCenterX = actualCenterX;
            item.ActualCenterY = actualCenterY;
            var bodyShifted =
                Math.Abs(item.BodyShiftX) > MovementVerificationEpsilon ||
                Math.Abs(item.BodyShiftY) > MovementVerificationEpsilon;

            if (!bodyShifted)
            {
                item.Entry.CenterX = actualCenterX;
                item.Entry.CenterY = actualCenterY;
                item.Entry.Item.AnchorX = item.TargetX;
                item.Entry.Item.AnchorY = item.TargetY;
                result.AcceptedIds.Add(item.MarkId);
                PerfTrace.Write(
                    "api-mark",
                    "mark_optimize_leader_anchor",
                    0,
                    $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) actualCenter=({actualCenterX:F1},{actualCenterY:F1}) actualDelta=({item.BodyShiftX:F1},{item.BodyShiftY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) newAnchor=({item.TargetX:F1},{item.TargetY:F1})");
                continue;
            }

            var insertionPoint = actualMark.InsertionPoint;
            insertionPoint.X += item.BeforeCenterX - actualCenterX;
            insertionPoint.Y += item.BeforeCenterY - actualCenterY;
            actualMark.InsertionPoint = insertionPoint;
            item.CompensatedBody = actualMark.Modify();
            if (item.CompensatedBody)
                shifted.Add(item);
            else
                rejected.Add(item);
        }

        if (shifted.Count > 0)
            drawing.CommitChanges("(MCP) Compensate leader bodies");

        foreach (var item in shifted)
        {
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model, out var compensatedMark, out _, out var compensatedCenterX, out var compensatedCenterY))
            {
                rejected.Add(item);
                continue;
            }

            item.Entry.Mark = compensatedMark;
            item.FinalCenterX = compensatedCenterX;
            item.FinalCenterY = compensatedCenterY;
            var finalShiftX = compensatedCenterX - item.BeforeCenterX;
            var finalShiftY = compensatedCenterY - item.BeforeCenterY;
            var bodyStillShifted =
                Math.Abs(finalShiftX) > MovementVerificationEpsilon ||
                Math.Abs(finalShiftY) > MovementVerificationEpsilon;

            if (bodyStillShifted)
            {
                rejected.Add(item);
                continue;
            }

            item.Entry.CenterX = compensatedCenterX;
            item.Entry.CenterY = compensatedCenterY;
            item.Entry.Item.AnchorX = item.TargetX;
            item.Entry.Item.AnchorY = item.TargetY;
            result.AcceptedIds.Add(item.MarkId);
            PerfTrace.Write(
                "api-mark",
                "mark_optimize_leader_anchor",
                0,
                $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) shiftedCenter=({item.ActualCenterX:F1},{item.ActualCenterY:F1}) actualCenter=({compensatedCenterX:F1},{compensatedCenterY:F1}) actualDelta=({finalShiftX:F1},{finalShiftY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) newAnchor=({item.TargetX:F1},{item.TargetY:F1}) compensatedBody=True");
        }

        foreach (var item in rejected)
        {
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model, out var rejectedMark, out _, out _, out _))
                continue;

            item.Entry.Mark = rejectedMark;
            rejectedMark.Placing = new LeaderLinePlacing(new Point(item.OldAnchorX, item.OldAnchorY, 0));
            rejectedMark.InsertionPoint = new Point(item.BeforeInsertion.X, item.BeforeInsertion.Y, item.BeforeInsertion.Z);
            item.Reverted = rejectedMark.Modify();
        }

        if (rejected.Count > 0)
            drawing.CommitChanges("(MCP) Reject leader anchors");

        var needsBodyRestore = new List<PendingLeaderAnchorOptimization>();
        foreach (var item in rejected)
        {
            item.FinalCenterX = item.ActualCenterX;
            item.FinalCenterY = item.ActualCenterY;
            if (!item.Reverted ||
                !TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model, out var revertedMark, out _, out var revertedCenterX, out var revertedCenterY))
            {
                continue;
            }

            item.Entry.Mark = revertedMark;
            item.Entry.CenterX = revertedCenterX;
            item.Entry.CenterY = revertedCenterY;
            item.FinalCenterX = revertedCenterX;
            item.FinalCenterY = revertedCenterY;

            var restoreDx = item.BeforeCenterX - revertedCenterX;
            var restoreDy = item.BeforeCenterY - revertedCenterY;
            if (Math.Abs(restoreDx) <= MovementVerificationEpsilon &&
                Math.Abs(restoreDy) <= MovementVerificationEpsilon)
            {
                continue;
            }

            var insertionPoint = revertedMark.InsertionPoint;
            insertionPoint.X += restoreDx;
            insertionPoint.Y += restoreDy;
            revertedMark.InsertionPoint = insertionPoint;
            item.RestoredBody = revertedMark.Modify();
            if (item.RestoredBody)
                needsBodyRestore.Add(item);
        }

        if (needsBodyRestore.Count > 0)
            drawing.CommitChanges("(MCP) Restore leader bodies");

        foreach (var item in rejected)
        {
            if (item.RestoredBody &&
                TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model, out var restoredMark, out _, out var restoredCenterX, out var restoredCenterY))
            {
                item.Entry.Mark = restoredMark;
                item.Entry.CenterX = restoredCenterX;
                item.Entry.CenterY = restoredCenterY;
                item.FinalCenterX = restoredCenterX;
                item.FinalCenterY = restoredCenterY;
            }

            PerfTrace.Write(
                "api-mark",
                "mark_optimize_leader_anchor_reject",
                0,
                $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) shiftedCenter=({item.ActualCenterX:F1},{item.ActualCenterY:F1}) finalCenter=({item.FinalCenterX:F1},{item.FinalCenterY:F1}) bodyShift=({item.BodyShiftX:F1},{item.BodyShiftY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) rejectedAnchor=({item.TargetX:F1},{item.TargetY:F1}) reverted={item.Reverted} restoredBody={item.RestoredBody}");
            result.RejectedIds.Add(item.MarkId);
        }

        return result;
    }

    internal static LeaderAnchorOptimizationResult ApplyLeaderTextCleanup(
        IReadOnlyList<TeklaDrawingMarkLayoutEntry> entries,
        LeaderTextCleanupDryRunResult dryRun,
        IReadOnlyList<LeaderTextOverlapMark> preCleanupOverlapMarks,
        double ownEndIgnoreDistance,
        Model model,
        Tekla.Structures.Drawing.Drawing drawing)
    {
        var result = new LeaderAnchorOptimizationResult();
        if (dryRun.ImprovableMarks == 0)
            return result;

        var entryById = entries.ToDictionary(e => e.Mark.GetIdentifier().ID);
        var originalSeverityById = dryRun.Marks.ToDictionary(
            static m => m.MarkId, static m => m.CurrentSeverity);
        var preCleanupOverlapMarkById = preCleanupOverlapMarks.ToDictionary(static m => m.MarkId);
        var pending = new List<PendingLeaderAnchorOptimization>();

        foreach (var markResult in dryRun.Marks)
        {
            if (!markResult.HasImprovement)
                continue;
            if (!entryById.TryGetValue(markResult.MarkId, out var entry))
                continue;
            if (entry.Mark.Placing is not LeaderLinePlacing leaderLinePlacing)
                continue;

            var oldAnchorX = leaderLinePlacing.StartPoint.X;
            var oldAnchorY = leaderLinePlacing.StartPoint.Y;
            if (Math.Abs(oldAnchorX - markResult.BestAnchorX) < LeaderAnchorNoOpEpsilon &&
                Math.Abs(oldAnchorY - markResult.BestAnchorY) < LeaderAnchorNoOpEpsilon)
                continue;

            var beforeInsertion = entry.Mark.InsertionPoint;
            entry.Mark.Placing = new LeaderLinePlacing(new Point(markResult.BestAnchorX, markResult.BestAnchorY, 0));
            entry.Mark.InsertionPoint = new Point(beforeInsertion.X, beforeInsertion.Y, beforeInsertion.Z);
            if (!entry.Mark.Modify())
                continue;

            pending.Add(new PendingLeaderAnchorOptimization
            {
                Entry = entry,
                MarkId = markResult.MarkId,
                OldAnchorX = oldAnchorX,
                OldAnchorY = oldAnchorY,
                TargetX = markResult.BestAnchorX,
                TargetY = markResult.BestAnchorY,
                BeforeCenterX = entry.CenterX,
                BeforeCenterY = entry.CenterY,
                BeforeInsertion = beforeInsertion,
            });
        }

        if (pending.Count == 0)
            return result;

        drawing.CommitChanges("(MCP) LeaderTextCleanup");

        var toRevert = new List<PendingLeaderAnchorOptimization>();
        foreach (var item in pending)
        {
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model,
                    out var actualMark, out _, out var actualCenterX, out var actualCenterY))
            {
                toRevert.Add(item);
                PerfTrace.Write("api-mark", "mark_leader_text_cleanup", 0,
                    $"markId={item.MarkId} oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) newAnchor=({item.TargetX:F1},{item.TargetY:F1}) reload=False accepted=False");
                continue;
            }

            item.Entry.Mark = actualMark;
            item.ActualCenterX = actualCenterX;
            item.ActualCenterY = actualCenterY;

            var bodyShifted = Math.Abs(item.BodyShiftX) > MovementVerificationEpsilon ||
                              Math.Abs(item.BodyShiftY) > MovementVerificationEpsilon;
            if (bodyShifted)
            {
                toRevert.Add(item);
                continue;
            }

            var severityImproved = false;
            if (TryBuildLeaderTextOverlapMark(actualMark, item.Entry.ViewId, model, out var actualOverlapMark) &&
                preCleanupOverlapMarkById.ContainsKey(item.MarkId) &&
                originalSeverityById.TryGetValue(item.MarkId, out var origSeverity))
            {
                var actualSeverity = ScoreLeaderPolylineConflict(
                    actualOverlapMark, item.MarkId,
                    preCleanupOverlapMarks, ownEndIgnoreDistance);
                severityImproved = actualSeverity < origSeverity - 0.001;
                var actualAnchorX = actualMark.Placing is LeaderLinePlacing llp ? llp.StartPoint.X : item.TargetX;
                var actualAnchorY = actualMark.Placing is LeaderLinePlacing llp2 ? llp2.StartPoint.Y : item.TargetY;
                PerfTrace.Write("api-mark", "mark_leader_text_cleanup", 0,
                    $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) actualCenter=({actualCenterX:F1},{actualCenterY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) newAnchor=({actualAnchorX:F1},{actualAnchorY:F1}) origSeverity={origSeverity:F3} actualSeverity={actualSeverity:F3} severityImproved={severityImproved} accepted={severityImproved}");
            }
            else
            {
                PerfTrace.Write("api-mark", "mark_leader_text_cleanup", 0,
                    $"markId={item.MarkId} beforeCenter=({item.BeforeCenterX:F1},{item.BeforeCenterY:F1}) actualCenter=({actualCenterX:F1},{actualCenterY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) rejectedAnchor=({item.TargetX:F1},{item.TargetY:F1}) severityCheck=failed accepted=False");
            }

            if (!severityImproved)
            {
                toRevert.Add(item);
                continue;
            }

            item.Entry.CenterX = actualCenterX;
            item.Entry.CenterY = actualCenterY;
            item.Entry.Item.AnchorX = item.TargetX;
            item.Entry.Item.AnchorY = item.TargetY;
            result.AcceptedIds.Add(item.MarkId);
        }

        var reverted = new List<PendingLeaderAnchorOptimization>();
        foreach (var item in toRevert)
        {
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model,
                    out var revertMark, out _, out _, out _))
            {
                item.Reverted = TryRevertLeaderAnchor(item.Entry.Mark, item);
                PerfTrace.Write("api-mark", "mark_leader_text_cleanup", 0,
                    $"markId={item.MarkId} reloadForRevert=False oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) rejectedAnchor=({item.TargetX:F1},{item.TargetY:F1}) accepted=False reverted={item.Reverted}");
                result.RejectedIds.Add(item.MarkId);
                continue;
            }

            item.Entry.Mark = revertMark;
            item.Reverted = TryRevertLeaderAnchor(revertMark, item);
            if (item.Reverted)
                reverted.Add(item);
            result.RejectedIds.Add(item.MarkId);
        }

        if (reverted.Count > 0)
            drawing.CommitChanges("(MCP) LeaderTextCleanup revert");

        var needsBodyRestore = new List<PendingLeaderAnchorOptimization>();
        foreach (var item in reverted)
        {
            item.FinalCenterX = item.ActualCenterX;
            item.FinalCenterY = item.ActualCenterY;
            if (!TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model,
                    out var revertedMark, out _, out var revertedCenterX, out var revertedCenterY))
                continue;

            item.Entry.Mark = revertedMark;
            item.Entry.CenterX = revertedCenterX;
            item.Entry.CenterY = revertedCenterY;
            item.FinalCenterX = revertedCenterX;
            item.FinalCenterY = revertedCenterY;

            var restoreDx = item.BeforeCenterX - revertedCenterX;
            var restoreDy = item.BeforeCenterY - revertedCenterY;
            if (Math.Abs(restoreDx) <= MovementVerificationEpsilon &&
                Math.Abs(restoreDy) <= MovementVerificationEpsilon)
                continue;

            var insertionPoint = revertedMark.InsertionPoint;
            insertionPoint.X += restoreDx;
            insertionPoint.Y += restoreDy;
            revertedMark.InsertionPoint = insertionPoint;
            item.RestoredBody = revertedMark.Modify();
            if (item.RestoredBody)
                needsBodyRestore.Add(item);
        }

        if (needsBodyRestore.Count > 0)
            drawing.CommitChanges("(MCP) LeaderTextCleanup body restore");

        foreach (var item in reverted)
        {
            if (item.RestoredBody &&
                TryReloadMarkState(drawing, item.MarkId, item.Entry.ViewId, model,
                    out var restoredMark, out _, out var restoredCenterX, out var restoredCenterY))
            {
                item.Entry.Mark = restoredMark;
                item.Entry.CenterX = restoredCenterX;
                item.Entry.CenterY = restoredCenterY;
                item.FinalCenterX = restoredCenterX;
                item.FinalCenterY = restoredCenterY;
            }

            PerfTrace.Write("api-mark", "mark_leader_text_cleanup", 0,
                $"markId={item.MarkId} bodyShift=({item.BodyShiftX:F1},{item.BodyShiftY:F1}) finalCenter=({item.FinalCenterX:F1},{item.FinalCenterY:F1}) oldAnchor=({item.OldAnchorX:F1},{item.OldAnchorY:F1}) rejectedAnchor=({item.TargetX:F1},{item.TargetY:F1}) accepted=False reverted={item.Reverted} restoredBody={item.RestoredBody}");
        }

        return result;
    }

    private static bool TryBuildLeaderTextOverlapMark(
        Mark mark,
        int viewId,
        Model model,
        out LeaderTextOverlapMark overlapMark)
    {
        overlapMark = null!;
        try
        {
            var markId = mark.GetIdentifier().ID;
            var geometry = MarkGeometryResolver.Build(mark, model, viewId);
            if (geometry.Corners.Count < 3)
                return false;

            var leaderPolyline = BuildPrimaryLeaderPolyline(MarkLeaderLineReader.ReadSnapshots(mark));
            if (leaderPolyline.Count < 2)
                return false;

            overlapMark = new LeaderTextOverlapMark
            {
                MarkId = markId,
                TextPolygon = geometry.Corners.Select(static corner => new[] { corner[0], corner[1] }).ToList(),
                LeaderPolyline = leaderPolyline
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<double[]> BuildPrimaryLeaderPolyline(IReadOnlyList<LeaderLineSnapshot> leaderLines)
    {
        var result = new List<double[]>();
        var leaderLine = leaderLines
            .FirstOrDefault(static line => string.Equals(line.Type, "NormalLeaderLine", StringComparison.Ordinal))
            ?? leaderLines.FirstOrDefault();

        if (leaderLine?.StartPoint == null || leaderLine.EndPoint == null)
            return result;

        result.Add([leaderLine.StartPoint.X, leaderLine.StartPoint.Y]);
        result.AddRange(leaderLine.ElbowPoints
            .OrderBy(static point => point.Order)
            .Select(static point => new[] { point.X, point.Y }));
        result.Add([leaderLine.EndPoint.X, leaderLine.EndPoint.Y]);
        return result;
    }

    private static double ScoreLeaderPolylineConflict(
        LeaderTextOverlapMark actualMark,
        int ownMarkId,
        IReadOnlyList<LeaderTextOverlapMark> allMarks,
        double ownEndIgnoreDistance)
    {
        var testList = allMarks
            .Select(m => m.MarkId == ownMarkId ? actualMark : m)
            .ToList();
        return LeaderTextOverlapAnalyzer.Analyze(testList, ownEndIgnoreDistance).Conflicts
            .Where(c => c.MarkId == ownMarkId)
            .Sum(static c => c.Severity);
    }

    private static bool TryRevertLeaderAnchor(Mark mark, PendingLeaderAnchorOptimization item)
    {
        try
        {
            mark.Placing = new LeaderLinePlacing(new Point(item.OldAnchorX, item.OldAnchorY, 0));
            mark.InsertionPoint = new Point(item.BeforeInsertion.X, item.BeforeInsertion.Y, item.BeforeInsertion.Z);
            return mark.Modify();
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryResolvePreferredLeaderAnchorTarget(
        MarkContext markContext,
        double centerX,
        double centerY,
        double viewScale,
        IReadOnlyList<double[]> polygon,
        out double targetX,
        out double targetY)
    {
        targetX = 0.0;
        targetY = 0.0;

        var resolvedViewScale = viewScale > 0 ? viewScale : 1.0;
        var depthMm = LeaderAnchorDepthPaperMm * resolvedViewScale;
        var farEdgeClearanceMm = LeaderAnchorFarEdgeClearancePaperMm * resolvedViewScale;
        var snapshot = markContext.LeaderSnapshot;
        if (snapshot?.AnchorPoint != null)
        {
            var candidates = LeaderAnchorCandidateGenerator.CreateCandidates(
                polygon,
                snapshot,
                depthMm,
                farEdgeClearanceMm);
            var bestCandidate = LeaderAnchorCandidateScorer.SelectBestCandidate(candidates);
            if (bestCandidate?.AnchorPoint != null)
            {
                var referencePoint = snapshot.LeaderEndPoint ?? snapshot.InsertionPoint ?? snapshot.AnchorPoint;
                if (referencePoint == null || bestCandidate.LineLengthToLeaderEnd <= Distance(referencePoint.X, referencePoint.Y, snapshot.AnchorPoint.X, snapshot.AnchorPoint.Y) + LeaderLengthRegressionEpsilon)
                {
                    targetX = bestCandidate.AnchorPoint.X;
                    targetY = bestCandidate.AnchorPoint.Y;
                    return true;
                }

                return false;
            }

            return false;
        }

        return LeaderAnchorResolver.TryResolveAnchorTarget(
            polygon,
            centerX,
            centerY,
            depthMm,
            farEdgeClearanceMm,
            out targetX,
            out targetY);
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

    private static bool TryReloadMarkState(
        Tekla.Structures.Drawing.Drawing drawing,
        int markId,
        int viewId,
        Model model,
        out Mark mark,
        out Point insertionPoint,
        out double centerX,
        out double centerY)
    {
        mark = null!;
        insertionPoint = new Point();
        centerX = 0.0;
        centerY = 0.0;

        try
        {
            var drawingObjects = drawing.GetSheet().GetAllObjects();
            while (drawingObjects.MoveNext())
            {
                if (drawingObjects.Current is not Mark candidate ||
                    candidate.GetIdentifier().ID != markId)
                {
                    continue;
                }

                mark = candidate;
                return TryReloadMarkState(mark, viewId, model, out insertionPoint, out centerX, out centerY);
            }
        }
        catch
        {
            return false;
        }

        return false;
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

    private static double Distance(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
