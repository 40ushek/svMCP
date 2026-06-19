using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal sealed class DrawingLayoutVariantResult
{
    public IReadOnlyList<DrawingLayoutCandidate> Candidates { get; set; }
    public List<ArrangedView> Arranged { get; set; }
    public List<ArrangedView> ArrangedBeforeFree { get; set; }
    public ProjectionAlignmentResult ProjectionResult { get; set; }
    public long ArrangeMs { get; set; }
    public long PostAdjustMs { get; set; }
    public long ProjectionMs { get; set; }
    public bool DetailScalesChanged { get; set; }
    public IReadOnlyDictionary<int, (double X, double Y)> OffsetById { get; set; }
    public long FinalCommitMs { get; set; }
}
