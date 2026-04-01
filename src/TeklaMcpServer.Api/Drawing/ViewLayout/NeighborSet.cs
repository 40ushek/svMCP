using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class NeighborSet
{
    private readonly Dictionary<int, NeighborRole> _roleById = new();

    public NeighborSet(View baseView)
    {
        BaseView = baseView;
    }

    public View BaseView { get; }

    public View? TopNeighbor { get; set; }

    public View? BottomNeighbor { get; set; }

    public View? SideNeighborLeft { get; set; }

    public View? SideNeighborRight { get; set; }

    public List<View> ResidualProjected { get; } = new();

    public NeighborRole GetRole(View view)
        => GetRole(view.GetIdentifier().ID);

    public NeighborRole GetRole(int viewId)
        => _roleById.TryGetValue(viewId, out var role) ? role : NeighborRole.Unknown;

    public void SetRole(View view, NeighborRole role)
        => _roleById[view.GetIdentifier().ID] = role;
}
