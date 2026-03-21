using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleViewCommands(string command, string[] args)
    {
        var api = new TeklaDrawingViewApi();

        switch (command)
        {
            case "get_drawing_views":
                return HandleGetDrawingViews(api);

            case "get_drawing_section_sides":
                return HandleGetDrawingSectionSides(api);

            case "get_drawing_detail_marks":
                return HandleGetDrawingDetailMarks(api);

            case "move_view":
                return HandleMoveView(api, args);

            case "set_view_scale":
                return HandleSetViewScale(api, args);

            case "fit_views_to_sheet":
                return HandleFitViewsToSheet(api, args);

            case "get_drawing_reserved_areas":
                return HandleGetDrawingReservedAreas();

            default:
                return false;
        }
    }

    private bool HandleGetDrawingViews(TeklaDrawingViewApi api)
    {
        var result = api.GetViews();
        WriteGetDrawingViewsResult(result);
        return true;
    }

    private bool HandleGetDrawingSectionSides(TeklaDrawingViewApi api)
    {
        var result = api.GetSectionPlacementSides();
        WriteJson(new
        {
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            baseView = new
            {
                id = result.BaseViewId,
                viewType = result.BaseViewType,
                selectionKind = result.BaseViewSelectionKind,
                reason = result.BaseViewReason,
                isFallback = result.BaseViewIsFallback
            },
            sections = result.Sections.Select(section => new
            {
                id = section.Id,
                name = section.Name,
                placementSide = section.PlacementSide,
                reason = section.Reason,
                isFallback = section.IsFallback,
                scale = section.Scale,
                width = section.Width,
                height = section.Height,
                referenceAxisX = section.ReferenceAxisX,
                referenceAxisY = section.ReferenceAxisY,
                viewAxisX = section.ViewAxisX,
                viewAxisY = section.ViewAxisY,
                viewNormal = section.ViewNormal
            })
        });
        return true;
    }

    private bool HandleGetDrawingDetailMarks(TeklaDrawingViewApi api)
    {
        var result = api.GetDetailMarks();
        WriteJson(new
        {
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            detailMarks = result.DetailMarks.Select(mark => new
            {
                id = mark.Id,
                ownerViewId = mark.OwnerViewId,
                ownerViewType = mark.OwnerViewType,
                ownerViewName = mark.OwnerViewName,
                markName = mark.MarkName,
                detailViewId = mark.DetailViewId,
                detailViewType = mark.DetailViewType,
                detailViewName = mark.DetailViewName,
                detailViewScale = mark.DetailViewScale,
                centerPoint = mark.CenterPoint,
                boundaryPoint = mark.BoundaryPoint,
                labelPoint = mark.LabelPoint
            })
        });
        return true;
    }

    private bool HandleMoveView(TeklaDrawingViewApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseMoveViewRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.MoveView(
            parseResult.Request.ViewId,
            parseResult.Request.Dx,
            parseResult.Request.Dy,
            parseResult.Request.Absolute);
        WriteMoveViewResult(result);
        return true;
    }

    private bool HandleSetViewScale(TeklaDrawingViewApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseSetViewScaleRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.SetViewScale(parseResult.Request.ViewIds, parseResult.Request.Scale);
        WriteSetViewScaleResult(result);
        return true;
    }

    private bool HandleFitViewsToSheet(TeklaDrawingViewApi api, string[] args)
    {
        var request = DrawingCommandParsers.ParseFitViewsToSheetRequest(args);
        var result = api.FitViewsToSheet(request.Margin, request.Gap, request.TitleBlockHeight, request.KeepScale);
        WriteFitViewsToSheetResult(result, result.ReservedAreas);
        return true;
    }

    private bool HandleGetDrawingReservedAreas()
    {
        var result = new TeklaDrawingViewApi().GetReservedAreas(margin: 10.0);
        WriteJson(new
        {
            sheetWidth  = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            margin      = result.Margin,
            sheetMargin = result.SheetMargin,
            tableCount  = result.Tables.Count,
            tables = result.Tables.Select(t => new
            {
                tableId          = t.TableId,
                name             = t.Name,
                overlapWithViews = t.OverlapWithViews,
                hasGeometry      = t.HasGeometry,
                minX = t.Bounds?.MinX,
                minY = t.Bounds?.MinY,
                maxX = t.Bounds?.MaxX,
                maxY = t.Bounds?.MaxY
            }),
            mergedCount = result.MergedAreas.Count,
            mergedAreas = result.MergedAreas.Select(r => new
            {
                minX = r.MinX, minY = r.MinY, maxX = r.MaxX, maxY = r.MaxY
            })
        });
        return true;
    }

    private void WriteGetDrawingViewsResult(DrawingViewsResult result)
    {
        WriteJson(new
        {
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            views = result.Views.Select(v => new
            {
                id = v.Id,
                viewType = v.ViewType,
                semanticKind = v.SemanticKind,
                name = v.Name,
                originX = v.OriginX,
                originY = v.OriginY,
                scale = v.Scale,
                width = v.Width,
                height = v.Height
            })
        });
    }

    private void WriteMoveViewResult(MoveViewResult result)
    {
        WriteJson(new
        {
            moved = result.Moved,
            viewId = result.ViewId,
            oldOriginX = result.OldOriginX,
            oldOriginY = result.OldOriginY,
            newOriginX = result.NewOriginX,
            newOriginY = result.NewOriginY
        });
    }

    private void WriteSetViewScaleResult(SetViewScaleResult result)
    {
        WriteJson(new
        {
            updatedCount = result.UpdatedCount,
            updatedIds = result.UpdatedIds,
            scale = result.Scale
        });
    }

    private void WriteFitViewsToSheetResult(FitViewsResult result, DrawingReservedAreasResult? reserved = null)
    {
        WriteJson(new
        {
            optimalScale = result.OptimalScale,
            scalePreserved = result.ScalePreserved,
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            arranged = result.Arranged,
            views = result.Views.Select(v => new
            {
                id = v.Id,
                viewType = v.ViewType,
                originX = v.OriginX,
                originY = v.OriginY,
                preferredPlacementSide = v.PreferredPlacementSide,
                actualPlacementSide = v.ActualPlacementSide,
                placementFallbackUsed = v.PlacementFallbackUsed
            }),
            projectionApplied = result.ProjectionApplied,
            projectionSkipped = result.ProjectionSkipped,
            projectionDiagnostics = result.ProjectionDiagnostics,
            reservedAreas = reserved == null ? null : new
            {
                sheetMargin = reserved.SheetMargin,
                tableCount  = reserved.Tables.Count,
                tables = reserved.Tables.Select(t => new
                {
                    tableId          = t.TableId,
                    name             = t.Name,
                    overlapWithViews = t.OverlapWithViews,
                    hasGeometry      = t.HasGeometry,
                    minX = t.Bounds?.MinX,
                    minY = t.Bounds?.MinY,
                    maxX = t.Bounds?.MaxX,
                    maxY = t.Bounds?.MaxY
                }),
                mergedCount = reserved.MergedAreas.Count,
                mergedAreas = reserved.MergedAreas.Select(r => new
                {
                    minX = r.MinX, minY = r.MinY, maxX = r.MaxX, maxY = r.MaxY
                })
            }
        });
    }
}
