using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Groups SectionViews by their resolved PlacementSide.
/// </summary>
internal sealed class SectionGroupSet
{
    public List<View> Left    { get; } = new();
    public List<View> Right   { get; } = new();
    public List<View> Top     { get; } = new();
    public List<View> Bottom  { get; } = new();
    public List<View> Unknown { get; } = new();

    private readonly Dictionary<int, SectionPlacementSideResult> _resultById = new();

    public SectionPlacementSideResult? GetResult(int viewId)
        => _resultById.TryGetValue(viewId, out var r) ? r : null;

    public static SectionGroupSet Build(
        IEnumerable<View> sections,
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        SectionPlacementSideResolver resolver,
        DrawingLayoutWorkspace? workspace = null)
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

        return set;
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

