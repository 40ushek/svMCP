using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    private readonly struct MainSkeletonRect
    {
        public MainSkeletonRect(string role, ReservedRect rect)
        {
            Role = role;
            Rect = rect;
        }

        public string Role { get; }
        public ReservedRect Rect { get; }
    }

    private readonly struct MainSkeletonNeighborSpec
    {
        public MainSkeletonNeighborSpec(
            string role,
            View? view,
            RelativePlacement placement,
            bool allowSheetTopFallback,
            double width = 0,
            double height = 0)
        {
            Role = role;
            View = view;
            Placement = placement;
            AllowSheetTopFallback = allowSheetTopFallback;
            Width = width;
            Height = height;
        }

        public string Role { get; }
        public View? View { get; }
        public RelativePlacement Placement { get; }
        public bool AllowSheetTopFallback { get; }
        public double Width { get; }
        public double Height { get; }
    }

    private sealed class MainSkeletonPlacementState
    {
        public ReservedRect? TopRect { get; private set; }
        public ReservedRect? BottomRect { get; private set; }
        public ReservedRect? LeftRect { get; private set; }
        public ReservedRect? RightRect { get; private set; }

        public void SetPlaced(string role, ReservedRect rect)
        {
            switch (role)
            {
                case "top":
                    TopRect = rect;
                    break;
                case "bottom":
                    BottomRect = rect;
                    break;
                case "left":
                    LeftRect = rect;
                    break;
                case "right":
                    RightRect = rect;
                    break;
            }
        }

        public void Clear(string role)
        {
            switch (role)
            {
                case "top":
                    TopRect = null;
                    break;
                case "bottom":
                    BottomRect = null;
                    break;
                case "left":
                    LeftRect = null;
                    break;
                case "right":
                    RightRect = null;
                    break;
            }
        }

        public ReservedRect? GetPlacedRect(string role)
            => role switch
            {
                "top" => TopRect,
                "bottom" => BottomRect,
                "left" => LeftRect,
                "right" => RightRect,
                _ => null
            };

        public bool TryGetPlacedRect(string role, out ReservedRect rect)
        {
            var placedRect = GetPlacedRect(role);
            if (placedRect != null)
            {
                rect = placedRect;
                return true;
            }

            rect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        public ReservedRect GetAnchorOrBase(string role, ReservedRect baseRect)
            => GetPlacedRect(role) ?? baseRect;
    }

    private static MainSkeletonNeighborSpec[] CreateMainSkeletonNeighborSpecs(
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor)
        => new[]
        {
            new MainSkeletonNeighborSpec("top", top, RelativePlacement.Top, allowSheetTopFallback: true),
            new MainSkeletonNeighborSpec("bottom", bottom, RelativePlacement.Bottom, allowSheetTopFallback: false),
            new MainSkeletonNeighborSpec("left", leftNeighbor, RelativePlacement.Left, allowSheetTopFallback: false),
            new MainSkeletonNeighborSpec("right", rightNeighbor, RelativePlacement.Right, allowSheetTopFallback: false)
        };

    private static MainSkeletonNeighborSpec[] CreateStrictMainSkeletonNeighborSpecs(
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor,
        double topWidth,
        double topHeight,
        double bottomWidth,
        double bottomHeight,
        double leftNeighborWidth,
        double leftNeighborHeight,
        double rightNeighborWidth,
        double rightNeighborHeight)
        => new[]
        {
            new MainSkeletonNeighborSpec("top", top, RelativePlacement.Top, allowSheetTopFallback: true, topWidth, topHeight),
            new MainSkeletonNeighborSpec("bottom", bottom, RelativePlacement.Bottom, allowSheetTopFallback: false, bottomWidth, bottomHeight),
            new MainSkeletonNeighborSpec("left", leftNeighbor, RelativePlacement.Left, allowSheetTopFallback: false, leftNeighborWidth, leftNeighborHeight),
            new MainSkeletonNeighborSpec("right", rightNeighbor, RelativePlacement.Right, allowSheetTopFallback: false, rightNeighborWidth, rightNeighborHeight)
        };

    private static MainSkeletonPlacementState CreateMainSkeletonPlacementState(
        bool topPlaced,
        bool bottomPlaced,
        bool leftPlaced,
        bool rightPlaced,
        ReservedRect topRect,
        ReservedRect bottomRect,
        ReservedRect leftRect,
        ReservedRect rightRect)
    {
        var placements = new MainSkeletonPlacementState();
        if (topPlaced)
            placements.SetPlaced("top", topRect);
        if (bottomPlaced)
            placements.SetPlaced("bottom", bottomRect);
        if (leftPlaced)
            placements.SetPlaced("left", leftRect);
        if (rightPlaced)
            placements.SetPlaced("right", rightRect);
        return placements;
    }

    private static void CopyMainSkeletonPlacementStateToLegacy(
        MainSkeletonPlacementState placements,
        ref bool topPlaced,
        ref bool bottomPlaced,
        ref bool leftPlaced,
        ref bool rightPlaced,
        ref ReservedRect topRect,
        ref ReservedRect bottomRect,
        ref ReservedRect leftRect,
        ref ReservedRect rightRect)
    {
        topPlaced = placements.TopRect != null;
        bottomPlaced = placements.BottomRect != null;
        leftPlaced = placements.LeftRect != null;
        rightPlaced = placements.RightRect != null;
        topRect = placements.TopRect ?? new ReservedRect(0, 0, 0, 0);
        bottomRect = placements.BottomRect ?? new ReservedRect(0, 0, 0, 0);
        leftRect = placements.LeftRect ?? new ReservedRect(0, 0, 0, 0);
        rightRect = placements.RightRect ?? new ReservedRect(0, 0, 0, 0);
    }

    internal static bool TryValidateMainSkeletonSpacing(
        ReservedRect baseRect,
        ReservedRect? topRect,
        ReservedRect? bottomRect,
        ReservedRect? leftRect,
        ReservedRect? rightRect,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas,
        out string reason,
        out string role,
        out ReservedRect failingRect)
        => TryValidateMainSkeletonSpacingCore(
            baseRect,
            CreateMainSkeletonRects(baseRect, topRect, bottomRect, leftRect, rightRect),
            sheetWidth,
            sheetHeight,
            margin,
            gap,
            reservedAreas,
            out reason,
            out role,
            out failingRect);

    private static bool TryValidateMainSkeletonSpacing(
        ReservedRect baseRect,
        MainSkeletonPlacementState placements,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas,
        out string reason,
        out string role,
        out ReservedRect failingRect)
        => TryValidateMainSkeletonSpacingCore(
            baseRect,
            CreateMainSkeletonRects(baseRect, placements.TopRect, placements.BottomRect, placements.LeftRect, placements.RightRect),
            sheetWidth,
            sheetHeight,
            margin,
            gap,
            reservedAreas,
            out reason,
            out role,
            out failingRect);

    private static bool TryValidateMainSkeletonSpacingCore(
        ReservedRect baseRect,
        List<MainSkeletonRect> placements,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect> reservedAreas,
        out string reason,
        out string role,
        out ReservedRect failingRect)
    {
        reason = string.Empty;
        role = string.Empty;
        failingRect = baseRect;

        foreach (var placement in placements)
        {
            if (!IsWithinArea(placement.Rect, margin, sheetWidth - margin, margin, sheetHeight - margin))
            {
                reason = $"main-skeleton-out-of-sheet-{placement.Role}";
                role = placement.Role;
                failingRect = placement.Rect;
                return false;
            }

            if (IntersectsAny(placement.Rect, reservedAreas))
            {
                reason = $"main-skeleton-reserved-overlap-{placement.Role}";
                role = placement.Role;
                failingRect = placement.Rect;
                return false;
            }
        }

        for (var i = 0; i < placements.Count; i++)
        {
            for (var j = i + 1; j < placements.Count; j++)
            {
                if (!Intersects(placements[i].Rect, placements[j].Rect))
                    continue;

                reason = $"main-skeleton-overlap-{placements[j].Role}";
                role = placements[j].Role;
                failingRect = placements[j].Rect;
                return false;
            }
        }

        foreach (var placement in placements)
        {
            if (placement.Role != "base"
                && !HasMainSkeletonGap(baseRect, placement.Role, placement.Rect, gap))
            {
                reason = $"main-skeleton-gap-{placement.Role}";
                role = placement.Role;
                failingRect = placement.Rect;
                return false;
            }
        }

        return true;
    }

    private static List<MainSkeletonRect> CreateMainSkeletonRects(
        ReservedRect baseRect,
        ReservedRect? topRect,
        ReservedRect? bottomRect,
        ReservedRect? leftRect,
        ReservedRect? rightRect)
    {
        var placements = new List<MainSkeletonRect>
        {
            new("base", baseRect)
        };

        if (topRect != null)
            placements.Add(new MainSkeletonRect("top", topRect));
        if (bottomRect != null)
            placements.Add(new MainSkeletonRect("bottom", bottomRect));
        if (leftRect != null)
            placements.Add(new MainSkeletonRect("left", leftRect));
        if (rightRect != null)
            placements.Add(new MainSkeletonRect("right", rightRect));

        return placements;
    }

    private static bool HasMainSkeletonGap(
        ReservedRect baseRect,
        string role,
        ReservedRect rect,
        double gap)
        => role switch
        {
            "top" => rect.MinY - baseRect.MaxY >= gap,
            "bottom" => baseRect.MinY - rect.MaxY >= gap,
            "left" => baseRect.MinX - rect.MaxX >= gap,
            "right" => rect.MinX - baseRect.MaxX >= gap,
            _ => true
        };

    private static View ResolveMainSkeletonView(NeighborSet neighbors, string role)
        => role switch
        {
            "top" => neighbors.TopNeighbor ?? neighbors.BaseView,
            "bottom" => neighbors.BottomNeighbor ?? neighbors.BaseView,
            "left" => neighbors.SideNeighborLeft ?? neighbors.BaseView,
            "right" => neighbors.SideNeighborRight ?? neighbors.BaseView,
            _ => neighbors.BaseView
        };

    private static string ToAttemptedZone(string role)
        => role switch
        {
            "top" => RelativePlacement.Top.ToString(),
            "bottom" => RelativePlacement.Bottom.ToString(),
            "left" => RelativePlacement.Left.ToString(),
            "right" => RelativePlacement.Right.ToString(),
            _ => "Center"
        };

    private static View? GetMainSkeletonNeighborView(
        string role,
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor)
        => role switch
        {
            "top" => top,
            "bottom" => bottom,
            "left" => leftNeighbor,
            "right" => rightNeighbor,
            _ => null
        };

    internal static bool TryDeferMainSkeletonNeighbor(
        string role,
        string reason,
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor,
        ref bool topPlaced,
        ref bool bottomPlaced,
        ref bool leftPlaced,
        ref bool rightPlaced,
        ref ReservedRect topRect,
        ref ReservedRect bottomRect,
        ref ReservedRect leftRect,
        ref ReservedRect rightRect,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        var placements = CreateMainSkeletonPlacementState(
            topPlaced,
            bottomPlaced,
            leftPlaced,
            rightPlaced,
            topRect,
            bottomRect,
            leftRect,
            rightRect);

        var deferred = TryDeferMainSkeletonNeighborCore(
            role,
            reason,
            top,
            bottom,
            leftNeighbor,
            rightNeighbor,
            placements,
            occupied,
            planned);

        CopyMainSkeletonPlacementStateToLegacy(
            placements,
            ref topPlaced,
            ref bottomPlaced,
            ref leftPlaced,
            ref rightPlaced,
            ref topRect,
            ref bottomRect,
            ref leftRect,
            ref rightRect);
        return deferred;
    }

    private static bool TryDeferMainSkeletonNeighborCore(
        string role,
        string reason,
        View? top,
        View? bottom,
        View? leftNeighbor,
        View? rightNeighbor,
        MainSkeletonPlacementState placements,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned)
    {
        void RemovePlacement(View view, ReservedRect rect)
        {
            planned.RemoveAll(item => ReferenceEquals(item.View, view));
            occupied.Remove(rect);
        }

        var view = GetMainSkeletonNeighborView(role, top, bottom, leftNeighbor, rightNeighbor);
        if (view == null || !placements.TryGetPlacedRect(role, out var rect))
            return false;

        RemovePlacement(view, rect);
        placements.Clear(role);
        PerfTrace.Write("api-view", "main_skeleton_defer", 0, $"role={role} reason={reason}");
        return true;
    }

    private static void CommitMainSkeletonPlacement(
        MainSkeletonPlacementState placements,
        string role,
        List<ReservedRect> occupied,
        ReservedRect rect)
    {
        placements.SetPlaced(role, rect);
        occupied.Add(rect);
    }

    private static void CommitPlannedMainSkeletonPlacement(
        MainSkeletonPlacementState placements,
        string role,
        List<PlannedPlacement> planned,
        List<ReservedRect> occupied,
        View view,
        ReservedRect rect)
    {
        AddPlannedAndOccupiedRect(planned, occupied, view, rect);
        placements.SetPlaced(role, rect);
    }

    private static bool TryCreateCenteredRelativeRect(
        ReservedRect anchorRect,
        RelativePlacement placement,
        double width,
        double height,
        double gap,
        out ReservedRect rect)
    {
        rect = placement switch
        {
            RelativePlacement.Top => new ReservedRect(
                CenterX(anchorRect) - width / 2.0,
                anchorRect.MaxY + gap,
                CenterX(anchorRect) + width / 2.0,
                anchorRect.MaxY + gap + height),
            RelativePlacement.Bottom => new ReservedRect(
                CenterX(anchorRect) - width / 2.0,
                anchorRect.MinY - gap - height,
                CenterX(anchorRect) + width / 2.0,
                anchorRect.MinY - gap),
            RelativePlacement.Left => new ReservedRect(
                anchorRect.MinX - gap - width,
                CenterY(anchorRect) - height / 2.0,
                anchorRect.MinX - gap,
                CenterY(anchorRect) + height / 2.0),
            RelativePlacement.Right => new ReservedRect(
                anchorRect.MaxX + gap,
                CenterY(anchorRect) - height / 2.0,
                anchorRect.MaxX + gap + width,
                CenterY(anchorRect) + height / 2.0),
            _ => new ReservedRect(0, 0, 0, 0)
        };

        return placement is RelativePlacement.Top
            or RelativePlacement.Bottom
            or RelativePlacement.Left
            or RelativePlacement.Right;
    }

    private static ReservedRect? FindStrictMainSkeletonNeighborRect(
        MainSkeletonNeighborSpec spec,
        ViewPlacementSearchArea searchArea,
        double gap,
        IReadOnlyList<ReservedRect> occupied)
        => FindCenteredRelativeRectInSearchArea(
            searchArea.BaseRect,
            spec.Placement,
            spec.Width,
            spec.Height,
            gap,
            searchArea,
            occupied);

    private static bool TryValidateMainSkeletonNeighborRect(
        ReservedRect rect,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
        => IsWithinArea(rect, searchArea.FreeMinX, searchArea.FreeMaxX, searchArea.FreeMinY, searchArea.FreeMaxY)
            && !IntersectsAny(rect, occupied);

    private static bool TryGetOptionalMainSkeletonNeighborView(
        MainSkeletonNeighborSpec spec,
        MainSkeletonPlacementState placements,
        out string role,
        out View? view)
    {
        role = spec.Role;
        if (spec.View != null)
        {
            view = spec.View;
            return true;
        }

        placements.Clear(role);
        view = null;
        return false;
    }

    private static bool TryFindOptionalMainSkeletonNeighborRect(
        MainSkeletonNeighborSpec spec,
        MainSkeletonPlacementState placements,
        System.Func<View, ReservedRect?> findRect,
        out string role,
        out View? view,
        out ReservedRect rect)
    {
        if (!TryGetOptionalMainSkeletonNeighborView(spec, placements, out role, out view) || view == null)
        {
            rect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        var foundRect = findRect(view);
        if (foundRect != null)
        {
            rect = foundRect;
            return true;
        }

        rect = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static bool TryHandleOptionalMainSkeletonNeighborCore(
        MainSkeletonNeighborSpec spec,
        MainSkeletonPlacementState placements,
        System.Func<View, ReservedRect?> findRect,
        System.Action<string, View, ReservedRect> onPlaced,
        System.Action<string, View> onRejected)
    {
        if (TryFindOptionalMainSkeletonNeighborRect(spec, placements, findRect, out var role, out var view, out var rect))
        {
            onPlaced(role, view!, rect);
            return true;
        }

        if (view == null)
            return true;

        placements.Clear(role);
        onRejected(role, view);
        return false;
    }

    private static void RejectOptionalPlannedMainSkeletonNeighbor(
        string mode,
        string role,
        DrawingArrangeContext context,
        List<PlannedPlacement> planned,
        View view,
        System.Action<View>? onRejected = null)
    {
        TracePlanReject(mode, role, context, planned, null);
        onRejected?.Invoke(view);
    }

    private static bool TryPlaceOptionalPlannedMainSkeletonNeighborCore(
        DrawingArrangeContext context,
        string mode,
        MainSkeletonNeighborSpec spec,
        MainSkeletonPlacementState placements,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        System.Func<View, ReservedRect?> findRect,
        System.Action<View>? onRejected = null)
        => TryHandleOptionalMainSkeletonNeighborCore(
            spec,
            placements,
            findRect,
            (role, view, rect) => CommitPlannedMainSkeletonPlacement(placements, role, planned, occupied, view, rect),
            (role, view) => RejectOptionalPlannedMainSkeletonNeighbor(mode, role, context, planned, view, onRejected));

    private static bool TryPlaceOptionalStrictMainSkeletonNeighbor(
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        ViewPlacementSearchArea searchArea,
        double gap,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        MainSkeletonPlacementState placements)
        => TryPlaceOptionalPlannedMainSkeletonNeighborCore(
            context,
            "strict",
            spec,
            placements,
            occupied,
            planned,
            _ => FindStrictMainSkeletonNeighborRect(spec, searchArea, gap, occupied));

    private static void TryPlaceOptionalRelaxedMainSkeletonNeighbor(
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        ViewPlacementSearchArea searchArea,
        List<ReservedRect> occupied,
        List<PlannedPlacement> planned,
        List<View> deferred,
        MainSkeletonPlacementState placements)
        => TryPlaceOptionalPlannedMainSkeletonNeighborCore(
            context,
            "relaxed",
            spec,
            placements,
            occupied,
            planned,
            view => FindRelaxedMainSkeletonNeighborRect(context, spec, view, searchArea, occupied),
            deferred.Add);

    private static ReservedRect? FindRelaxedMainSkeletonNeighborRect(
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        View view,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
    {
        return FindRelaxedMainSkeletonRelativeRect(context, spec, view, searchArea, occupied)
            ?? FindRelaxedMainSkeletonSheetTopFallbackRect(context, spec, view, searchArea, occupied);
    }

    private static ReservedRect? FindRelaxedMainSkeletonRelativeRect(
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        View view,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
        => FindRelativeRectInSearchArea(
            context,
            view,
            searchArea.BaseRect,
            searchArea,
            occupied,
            spec.Placement);

    private static ReservedRect? FindRelaxedMainSkeletonSheetTopFallbackRect(
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        View view,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
    {
        if (!spec.AllowSheetTopFallback || spec.Placement != RelativePlacement.Top)
            return null;

        return FindTopViewAtSheetTopInSearchArea(
            context,
            view,
            searchArea,
            occupied);
    }

    private static ReservedRect? FindTopViewAtSheetTopInSearchArea(
        DrawingArrangeContext context,
        View view,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied)
        => TryFindTopViewAtSheetTop(
            context,
            view,
            searchArea,
            occupied,
            out var rect)
            ? rect
            : null;

    private static void TryPlaceOptionalDiagnosticMainSkeletonNeighbor(
        List<DrawingFitConflict> conflicts,
        DrawingArrangeContext context,
        MainSkeletonNeighborSpec spec,
        ViewPlacementSearchArea searchArea,
        List<ReservedRect> occupied,
        MainSkeletonPlacementState placements)
    {
        TryHandleOptionalMainSkeletonNeighborCore(
            spec,
            placements,
            view => FindRelaxedMainSkeletonNeighborRect(context, spec, view, searchArea, occupied),
            (role, _, rect) => CommitMainSkeletonPlacement(placements, role, occupied, rect),
            (_, view) => DiagnoseOptionalMainSkeletonNeighborFailure(conflicts, context, spec, searchArea, occupied, view));
    }
}

