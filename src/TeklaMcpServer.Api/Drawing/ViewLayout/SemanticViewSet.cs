using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Groups views by semantic kind. Detail-like SectionViews are classified as Detail.
/// </summary>
internal sealed class SemanticViewSet
{
    public List<View> BaseProjected { get; } = new();
    public List<View> Sections { get; } = new();      // "normal" sections only (not detail-like)
    public List<View> Details { get; } = new();       // real DetailView + detail-like SectionView
    public List<View> Other { get; } = new();

    private readonly Dictionary<int, ViewSemanticKind> _kindById = new();

    public ViewSemanticKind GetKind(int viewId)
        => _kindById.TryGetValue(viewId, out var k) ? k : ViewSemanticKind.Other;

    public static SemanticViewSet Build(IEnumerable<View> views)
    {
        var set = new SemanticViewSet();
        foreach (var v in views)
        {
            var kind = ViewSemanticClassifier.Classify(v);
            set._kindById[v.GetIdentifier().ID] = kind;
            switch (kind)
            {
                case ViewSemanticKind.BaseProjected: set.BaseProjected.Add(v); break;
                case ViewSemanticKind.Section:       set.Sections.Add(v);      break;
                case ViewSemanticKind.Detail:        set.Details.Add(v);       break;
                default:                             set.Other.Add(v);         break;
            }
        }
        return set;
    }
}

