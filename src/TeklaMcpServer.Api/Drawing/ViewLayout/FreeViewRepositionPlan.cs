using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout
{
    internal sealed class FreeViewRepositionDecision
    {
        public int ViewId { get; set; }
        public string ViewKind { get; set; } = string.Empty;
        public bool AnchorDriven { get; set; }
        public double? PlannedOriginX { get; set; }
        public double? PlannedOriginY { get; set; }
        public ReservedRect? PlannedRect { get; set; }
        public string Reason { get; set; } = string.Empty;
        // True only when source origin came from arrangedById (not snapshot fallback)
        public bool SourceFromArranged { get; set; }
    }

    internal sealed class FreeViewRepositionPlan
    {
        public IReadOnlyList<FreeViewRepositionDecision> Decisions { get; set; }
            = new FreeViewRepositionDecision[0];
    }
}
