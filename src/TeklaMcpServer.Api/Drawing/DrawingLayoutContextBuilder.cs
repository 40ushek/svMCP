using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing.ViewLayout;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutContextBuilder
{
    public DrawingContext Build(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingViewsResult? views = null,
        double? margin = null,
        double titleBlockHeight = 0.0,
        IReadOnlyCollection<int>? excludeViewIds = null)
    {
        var viewResult = views ?? TeklaDrawingViewApi.BuildViewsResult(drawing);
        var (sheetMargin, tables) = DrawingReservedAreaReader.ReadLayoutInfo();
        var effectiveMargin = margin ?? sheetMargin ?? 10.0;
        // When caller does not specify excludeViewIds, auto-exclude all drawing views so that
        // view bounding boxes are not included in ReservedLayout.Areas. The scorer uses Areas
        // only for fixed obstacles (tables, margins); view-view overlap is tracked separately.
        var effectiveExcludeViewIds = ResolveExcludeViewIds(viewResult, excludeViewIds);
        var mergedAreas = DrawingReservedAreaReader.Read(
            drawing,
            effectiveMargin,
            titleBlockHeight,
            effectiveExcludeViewIds,
            tables);

        var reservedAreas = new DrawingReservedAreasResult
        {
            SheetWidth = drawing.Layout.SheetSize.Width,
            SheetHeight = drawing.Layout.SheetSize.Height,
            Margin = effectiveMargin,
            SheetMargin = sheetMargin,
            Tables = tables,
            MergedAreas = mergedAreas
        };

        return new DrawingContextBuilder().Build(CreateDrawingInfo(drawing), viewResult, reservedAreas);
    }

    internal static IReadOnlyCollection<int> ResolveExcludeViewIds(
        DrawingViewsResult viewResult,
        IReadOnlyCollection<int>? excludeViewIds)
    {
        if (viewResult == null)
            throw new System.ArgumentNullException(nameof(viewResult));

        return excludeViewIds ?? new HashSet<int>(viewResult.Views.Select(static v => v.Id));
    }

    private static DrawingInfo CreateDrawingInfo(Tekla.Structures.Drawing.Drawing drawing)
    {
        return new DrawingInfo
        {
            Guid = drawing.GetIdentifier().GUID.ToString(),
            Name = drawing.Name,
            Mark = drawing.Mark,
            Title1 = drawing.Title1,
            Title2 = drawing.Title2,
            Title3 = drawing.Title3,
            Type = drawing.GetType().Name,
            Status = drawing.UpToDateStatus.ToString()
        };
    }
}
