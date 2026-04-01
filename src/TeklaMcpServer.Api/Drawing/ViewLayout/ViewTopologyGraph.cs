using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum ProjectionMethod
{
    None,
    NeighborAxis,
    SectionSide,
    DetailAnchor
}

internal sealed class ViewTopologyGraph
{
    internal ViewTopologyGraph(
        BaseViewSelectionResult baseSelection,
        SemanticViewSet semanticViews,
        NeighborSet? neighbors,
        DetailRelationSet detailRelations)
    {
        BaseSelection = baseSelection;
        SemanticViews = semanticViews;
        Neighbors = neighbors;
        DetailRelations = detailRelations;
    }

    public BaseViewSelectionResult BaseSelection { get; }

    public View? BaseView => BaseSelection.View;

    public SemanticViewSet SemanticViews { get; }

    public NeighborSet? Neighbors { get; }

    public DetailRelationSet DetailRelations { get; }

    public IReadOnlyList<View> ResidualProjected
        => Neighbors != null
            ? Neighbors.ResidualProjected
            : (IReadOnlyList<View>)System.Array.Empty<View>();

    public bool TryGetNeighborRole(View view, out NeighborRole role)
    {
        if (Neighbors != null)
        {
            role = Neighbors.GetRole(view);
            return role != NeighborRole.Unknown;
        }

        role = NeighborRole.Unknown;
        return false;
    }

    public ProjectionMethod GetProjectionMethod(View view)
    {
        var viewId = view.GetIdentifier().ID;
        return ResolveProjectionMethod(
            SemanticViews.GetKind(viewId),
            Neighbors?.GetRole(viewId) ?? NeighborRole.Unknown,
            DetailRelations.TryGet(viewId, out _));
    }

    internal static ProjectionMethod ResolveProjectionMethod(
        ViewSemanticKind semanticKind,
        NeighborRole neighborRole,
        bool hasDetailRelation)
    {
        if (hasDetailRelation)
            return ProjectionMethod.DetailAnchor;

        if (semanticKind == ViewSemanticKind.Section)
            return ProjectionMethod.SectionSide;

        if (neighborRole != NeighborRole.Unknown)
            return ProjectionMethod.NeighborAxis;

        return ProjectionMethod.None;
    }

    public static ViewTopologyGraph Build(IReadOnlyList<View> views)
    {
        var baseSelection = BaseViewSelection.Select(views);
        var semanticViews = SemanticViewSet.Build(views);
        var neighbors = baseSelection.View != null
            ? StandardNeighborResolver.Build(views, semanticViews, baseSelection)
            : null;
        var detailRelations = DetailRelationResolver.Build(views, semanticViews.Details);

        return new ViewTopologyGraph(baseSelection, semanticViews, neighbors, detailRelations);
    }
}

