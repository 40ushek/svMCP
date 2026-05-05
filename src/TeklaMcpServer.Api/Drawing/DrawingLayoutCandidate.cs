using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutCandidate
{
    public string Name { get; set; } = string.Empty;

    public DrawingInfo Drawing { get; set; } = new();

    public DrawingSheetContext Sheet { get; set; } = new();

    public DrawingReservedLayoutContext ReservedLayout { get; set; } = new();

    public List<DrawingLayoutCandidateView> Views { get; set; } = new();

    public List<string> Diagnostics { get; set; } = new();

    public DrawingContext ToDrawingContext()
        => new()
        {
            Drawing = Drawing,
            Sheet = Sheet,
            ReservedLayout = ReservedLayout,
            Views = Views.ConvertAll(static view => view.ToDrawingViewInfo()),
            Warnings = new List<string>(Diagnostics)
        };
}

internal sealed class DrawingLayoutCandidateView
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

    public double? BBoxMinX { get; set; }

    public double? BBoxMinY { get; set; }

    public double? BBoxMaxX { get; set; }

    public double? BBoxMaxY { get; set; }

    public ReservedRect? LayoutRect { get; set; }

    public string PreferredPlacementSide { get; set; } = string.Empty;

    public string ActualPlacementSide { get; set; } = string.Empty;

    public bool PlacementFallbackUsed { get; set; }

    public DrawingViewInfo ToDrawingViewInfo()
        => new()
        {
            Id = Id,
            ViewType = ViewType,
            SemanticKind = SemanticKind,
            Name = Name,
            OriginX = OriginX,
            OriginY = OriginY,
            Scale = Scale,
            Width = Width,
            Height = Height,
            BBoxMinX = LayoutRect?.MinX ?? BBoxMinX,
            BBoxMinY = LayoutRect?.MinY ?? BBoxMinY,
            BBoxMaxX = LayoutRect?.MaxX ?? BBoxMaxX,
            BBoxMaxY = LayoutRect?.MaxY ?? BBoxMaxY
        };
}
