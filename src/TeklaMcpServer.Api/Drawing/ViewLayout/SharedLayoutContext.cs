using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class SharedLayoutContext
{
    public Tekla.Structures.Drawing.Drawing Drawing { get; set; } = null!;
    public DrawingLayoutWorkspace Workspace { get; set; } = null!;
    public List<View> CurrentViews { get; set; } = new();
    public List<View> ArrangedViews { get; set; } = new();
    public IReadOnlyDictionary<int, ReservedRect> ActualRects { get; set; } = new Dictionary<int, ReservedRect>();
    public IReadOnlyDictionary<int, (double X, double Y)> OffsetById { get; set; } = new Dictionary<int, (double X, double Y)>();
    public double OptimalScale { get; set; }
    public double EffectiveMargin { get; set; }
    public double Gap { get; set; }
    public bool PreserveExistingScales { get; set; }
    public bool KeepCurrentScales { get; set; }
    public bool AllowTeklaMutation { get; set; }
    public IReadOnlyList<ReservedRect> ExtraReservedAreas { get; set; } = System.Array.Empty<ReservedRect>();
}
