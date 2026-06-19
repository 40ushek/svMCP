using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class SharedLayoutContext
{
    public Tekla.Structures.Drawing.Drawing Drawing { get; set; }
    public DrawingLayoutWorkspace Workspace { get; set; }
    public List<View> CurrentViews { get; set; }
    public List<View> ArrangedViews { get; set; }
    public IReadOnlyDictionary<int, ReservedRect> ActualRects { get; set; }
    public IReadOnlyDictionary<int, (double X, double Y)> OffsetById { get; set; }
    public double OptimalScale { get; set; }
    public double EffectiveMargin { get; set; }
    public double Gap { get; set; }
    public bool PreserveExistingScales { get; set; }
    public bool KeepCurrentScales { get; set; }
    public bool AllowTeklaMutation { get; set; }
    public long InitMs { get; set; }
    public long ReservedMs { get; set; }
    public long CandidateFitMs { get; set; }
    public long ProbeMs { get; set; }
    public int CandidateAttempts { get; set; }
    public int ViewsCount { get; set; }
}
