using System.Collections.Generic;
using Tekla.Structures.Drawing;
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
        var mergedAreas = DrawingReservedAreaReader.Read(
            drawing,
            effectiveMargin,
            titleBlockHeight,
            excludeViewIds,
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

    private static DrawingInfo CreateDrawingInfo(Tekla.Structures.Drawing.Drawing drawing)
    {
        return new DrawingInfo
        {
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
