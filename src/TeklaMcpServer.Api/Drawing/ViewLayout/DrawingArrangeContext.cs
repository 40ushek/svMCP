using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed class DrawingArrangeContext
{
    internal DrawingArrangeContext(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View>? views,
        double gap,
        IReadOnlyDictionary<int, (double Width, double Height)>? effectiveFrameSizes = null)
        : this(
            drawing,
            views ?? workspace?.RuntimeViews ?? System.Array.Empty<View>(),
            workspace?.SheetWidth ?? 0,
            workspace?.SheetHeight ?? 0,
            workspace?.Margin ?? 0,
            gap,
            workspace?.ReservedAreas,
            effectiveFrameSizes ?? workspace?.SelectedFrameSizesById)
    {
        Workspace = workspace ?? throw new System.ArgumentNullException(nameof(workspace));
    }

    public DrawingArrangeContext(
        Tekla.Structures.Drawing.Drawing drawing,
        IReadOnlyList<View> views,
        double sheetWidth,
        double sheetHeight,
        double margin,
        double gap,
        IReadOnlyList<ReservedRect>? reservedAreas = null,
        IReadOnlyDictionary<int, (double Width, double Height)>? effectiveFrameSizes = null)
    {
        Drawing = drawing ?? throw new System.ArgumentNullException(nameof(drawing));
        Views = views ?? throw new System.ArgumentNullException(nameof(views));
        SheetWidth = sheetWidth;
        SheetHeight = sheetHeight;
        Margin = margin;
        Gap = gap;
        ReservedAreas = reservedAreas ?? System.Array.Empty<ReservedRect>();
        EffectiveFrameSizes = effectiveFrameSizes ?? new Dictionary<int, (double Width, double Height)>();
    }

    internal DrawingLayoutWorkspace? Workspace { get; }

    public Tekla.Structures.Drawing.Drawing Drawing { get; }
    public IReadOnlyList<View> Views { get; }
    public double SheetWidth { get; }
    public double SheetHeight { get; }
    public double Margin { get; }
    public double Gap { get; }
    public IReadOnlyList<ReservedRect> ReservedAreas { get; }
    public IReadOnlyDictionary<int, (double Width, double Height)> EffectiveFrameSizes { get; }
}

internal static class DrawingArrangeContextSizing
{
    public static double GetWidth(DrawingArrangeContext context, View view)
        => context.EffectiveFrameSizes.TryGetValue(view.GetIdentifier().ID, out var size) ? size.Width : view.Width;

    public static double GetHeight(DrawingArrangeContext context, View view)
        => context.EffectiveFrameSizes.TryGetValue(view.GetIdentifier().ID, out var size) ? size.Height : view.Height;
}

