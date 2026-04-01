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
        SectionPlacementSideResolver resolver)
    {
        var set = new SectionGroupSet();
        foreach (var v in sections)
        {
            var result = resolver.Resolve(drawing, baseView, v);
            set._resultById[v.GetIdentifier().ID] = result;
            switch (result.PlacementSide)
            {
                case SectionPlacementSide.Left:   set.Left.Add(v);    break;
                case SectionPlacementSide.Right:  set.Right.Add(v);   break;
                case SectionPlacementSide.Top:    set.Top.Add(v);     break;
                case SectionPlacementSide.Bottom: set.Bottom.Add(v);  break;
                default:                          set.Unknown.Add(v); break;
            }
        }
        return set;
    }
}

