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
        var result = api.FitViewsToSheet(request.Margin, request.Gap, request.TitleBlockHeight);
        var reserved = api.GetReservedAreas(request.Margin);
        WriteFitViewsToSheetResult(result, reserved);
        return true;
    }

    private bool HandleGetDrawingReservedAreas()
    {
        var result = new TeklaDrawingViewApi().GetReservedAreas(margin: 10.0);
        WriteJson(new
        {
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            margin = result.Margin,
            rawTableCount = result.RawTables.Count,
            rawTables = result.RawTables.Select(t => new
            {
                tableId = t.TableId,
                hasGeometry = t.HasGeometry,
                minX = t.Bounds?.MinX,
                minY = t.Bounds?.MinY,
                maxX = t.Bounds?.MaxX,
                maxY = t.Bounds?.MaxY
            }),
            filteredTableCount = result.FilteredTables.Count,
            filteredTables = result.FilteredTables.Select(t => new
            {
                tableId = t.TableId,
                hasGeometry = t.HasGeometry,
                minX = t.Bounds?.MinX,
                minY = t.Bounds?.MinY,
                maxX = t.Bounds?.MaxX,
                maxY = t.Bounds?.MaxY
            }),
            mergedCount = result.MergedAreas.Count,
            mergedAreas = result.MergedAreas.Select(r => new
            {
                minX = r.MinX,
                minY = r.MinY,
                maxX = r.MaxX,
                maxY = r.MaxY
            }),
            layoutTables = result.LayoutTables.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                fileName = t.FileName,
                scale = t.Scale,
                xOffset = t.XOffset,
                yOffset = t.YOffset,
                tableCorner = t.TableCorner,
                refCorner = t.RefCorner,
                overlapWithViews = t.OverlapWithViews
            }),
            drawingFrames = result.DrawingFrames.Select(f => new
            {
                active = f.Active,
                x = f.X,
                y = f.Y,
                w = f.W,
                h = f.H,
                corner = f.Corner
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
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            arranged = result.Arranged,
            views = result.Views.Select(v => new
            {
                id = v.Id,
                viewType = v.ViewType,
                originX = v.OriginX,
                originY = v.OriginY
            }),
            projectionApplied = result.ProjectionApplied,
            projectionSkipped = result.ProjectionSkipped,
            projectionDiagnostics = result.ProjectionDiagnostics,
            reservedAreas = reserved == null ? null : new
            {
                rawTableCount = reserved.RawTables.Count,
                rawTables = reserved.RawTables.Select(t => new
                {
                    tableId = t.TableId,
                    hasGeometry = t.HasGeometry,
                    minX = t.Bounds?.MinX,
                    minY = t.Bounds?.MinY,
                    maxX = t.Bounds?.MaxX,
                    maxY = t.Bounds?.MaxY
                }),
                filteredTableCount = reserved.FilteredTables.Count,
                filteredTables = reserved.FilteredTables.Select(t => new
                {
                    tableId = t.TableId,
                    hasGeometry = t.HasGeometry,
                    minX = t.Bounds?.MinX,
                    minY = t.Bounds?.MinY,
                    maxX = t.Bounds?.MaxX,
                    maxY = t.Bounds?.MaxY
                }),
                mergedCount = reserved.MergedAreas.Count,
                mergedAreas = reserved.MergedAreas.Select(r => new
                {
                    minX = r.MinX,
                    minY = r.MinY,
                    maxX = r.MaxX,
                    maxY = r.MaxY
                }),
                layoutTables = reserved.LayoutTables.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    fileName = t.FileName,
                    scale = t.Scale,
                    xOffset = t.XOffset,
                    yOffset = t.YOffset,
                    tableCorner = t.TableCorner,
                    refCorner = t.RefCorner,
                    overlapWithViews = t.OverlapWithViews
                }),
                drawingFrames = reserved.DrawingFrames.Select(f => new
                {
                    active = f.Active,
                    x = f.X,
                    y = f.Y,
                    w = f.W,
                    h = f.H,
                    corner = f.Corner
                })
            }
        });
    }
}
