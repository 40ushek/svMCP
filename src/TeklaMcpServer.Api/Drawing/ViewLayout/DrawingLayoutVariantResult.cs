using System.Collections.Generic;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutVariantResult
{
    public IReadOnlyList<DrawingLayoutCandidate> Candidates { get; set; } = System.Array.Empty<DrawingLayoutCandidate>();
    public List<ArrangedView> Arranged { get; set; } = new();
    public List<ArrangedView> ArrangedBeforeFree { get; set; } = new();
    public ProjectionAlignmentResult ProjectionResult { get; set; } = new();
    public long ArrangeMs { get; set; }
    public long PostAdjustMs { get; set; }
    public long ProjectionMs { get; set; }
    public long FinalCommitMs { get; set; }
    // Final runtime views after all variant phases — used to build the apply baseline
    // from the winning variant, not from the last-executed variant's side effects.
    public List<View> FinalRuntimeViews { get; set; } = new();
}
