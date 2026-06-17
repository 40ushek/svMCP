using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Groups SectionViews by their resolved PlacementSide.
/// SmallAnchorDriven contains detail-like sections that are removed from the
/// main stack for later free/anchor-aware placement.
/// </summary>
internal sealed class SectionGroupSet
{
    public const double SmallSectionRatio = 0.4;

    public List<View> Left              { get; } = new();
    public List<View> Right             { get; } = new();
    public List<View> Top               { get; } = new();
    public List<View> Bottom            { get; } = new();
    public List<View> Unknown           { get; } = new();
    public List<View> SmallAnchorDriven { get; } = new();

    private readonly Dictionary<int, SectionPlacementSideResult> _resultById = new();

    public SectionPlacementSideResult? GetResult(int viewId)
        => _resultById.TryGetValue(viewId, out var r) ? r : null;

    public static SectionGroupSet Build(
        IEnumerable<View> sections,
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        SectionPlacementSideResolver resolver,
        DrawingLayoutWorkspace? workspace = null,
        IEnumerable<View>? baseProjectedViews = null)
    {
        var set = new SectionGroupSet();
        // Sections whose parent is another SectionView are deferred until
        // their parent's side is known, then inherit it.
        var pendingParent = new List<(View view, int parentId)>();

        foreach (var v in sections)
        {
            var id = v.GetIdentifier().ID;
            var item = workspace?.TryGetView(id);

            if (item?.ParentViewId is { } parentId
                && workspace?.TryGetRuntimeView(parentId) is { ViewType: View.ViewTypes.SectionView })
            {
                pendingParent.Add((v, parentId));
                continue;
            }

            var result = resolver.Resolve(drawing, baseView, v);
            set._resultById[id] = result;
            AddToGroup(set, v, result.PlacementSide);
        }

        // Inherit placement side from parent (parent was resolved in the pass above).
        foreach (var (v, parentId) in pendingParent)
        {
            var id = v.GetIdentifier().ID;
            var inheritedSide = set._resultById.TryGetValue(parentId, out var pr)
                ? pr.PlacementSide
                : SectionPlacementSide.Unknown;
            set._resultById[id] = new SectionPlacementSideResult { PlacementSide = inheritedSide, Reason = $"inherited-from-parent:{parentId}" };
            AddToGroup(set, v, inheritedSide);
        }

        if (workspace != null)
            FilterSmallSections(set, workspace, baseProjectedViews ?? System.Array.Empty<View>());

        return set;
    }

    private static void FilterSmallSections(
        SectionGroupSet set,
        DrawingLayoutWorkspace workspace,
        IEnumerable<View> baseProjectedViews)
    {
        var baseList = baseProjectedViews.ToList();
        var verticalReference = Median(baseList.Select(v => v.Height));
        var horizontalReference = Median(baseList.Select(v => v.Width));

        FilterGroup(set.Left, isVerticalStack: true, set, workspace, verticalReference);
        FilterGroup(set.Right, isVerticalStack: true, set, workspace, verticalReference);
        FilterGroup(set.Top, isVerticalStack: false, set, workspace, horizontalReference);
        FilterGroup(set.Bottom, isVerticalStack: false, set, workspace, horizontalReference);
    }

    private static void FilterGroup(
        List<View> group,
        bool isVerticalStack,
        SectionGroupSet set,
        DrawingLayoutWorkspace workspace,
        double referenceMedian)
    {
        if (group.Count == 0 || referenceMedian <= 0)
            return;

        double SizeAlong(View v) => isVerticalStack ? v.Height : v.Width;

        var threshold = referenceMedian * SmallSectionRatio;
        var outliers = group
            .Where(v =>
            {
                var item = workspace.TryGetView(v.GetIdentifier().ID);
                return HasParentAnchor(item) && SizeAlong(v) < threshold;
            })
            .ToList();

        foreach (var v in outliers)
        {
            group.Remove(v);
            set.SmallAnchorDriven.Add(v);
        }
    }

    private static bool HasParentAnchor(DrawingLayoutViewItem? item)
        => item?.ParentAnchorX != null && item.ParentAnchorY != null;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(v => v > 0).OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return 0;
        return sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5;
    }

    private static void AddToGroup(SectionGroupSet set, View v, SectionPlacementSide side)
    {
        switch (side)
        {
            case SectionPlacementSide.Left:   set.Left.Add(v);    break;
            case SectionPlacementSide.Right:  set.Right.Add(v);   break;
            case SectionPlacementSide.Top:    set.Top.Add(v);     break;
            case SectionPlacementSide.Bottom: set.Bottom.Add(v);  break;
            default:                          set.Unknown.Add(v); break;
        }
    }
}
