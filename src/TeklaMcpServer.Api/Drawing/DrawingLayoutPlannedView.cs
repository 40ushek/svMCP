namespace TeklaMcpServer.Api.Drawing;

// Planned view is intentionally separate from DrawingLayoutCandidateView:
// it requires a resolved layout rect, while candidate views keep nullable
// runtime/snapshot fallback fields.
internal sealed class DrawingLayoutPlannedView
{
    public int Id { get; set; }

    public string ViewType { get; set; } = string.Empty;

    public string SemanticKind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double OriginX { get; set; }

    public double OriginY { get; set; }

    public double Scale { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public ReservedRect LayoutRect { get; set; } = new(0, 0, 0, 0);

    public string PreferredPlacementSide { get; set; } = string.Empty;

    public string ActualPlacementSide { get; set; } = string.Empty;

    public bool PlacementFallbackUsed { get; set; }
}
