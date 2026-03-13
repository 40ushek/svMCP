using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDrawingInteractionCommands(string command, string[] args)
    {
        TeklaDrawingInteractionApi? interactionApi = null;
        TeklaDrawingInteractionApi GetInteractionApi() => interactionApi ??= new TeklaDrawingInteractionApi(_model);

        switch (command)
        {
            case "select_drawing_objects":
                return HandleSelectDrawingObjects(GetInteractionApi(), args);

            case "filter_drawing_objects":
                return HandleFilterDrawingObjects(GetInteractionApi(), args);

            case "get_drawing_context":
                return HandleGetDrawingContext(GetInteractionApi());

            case "get_sheet_objects_debug":
                return HandleGetSheetObjectsDebug(GetInteractionApi());

            default:
                return false;
        }
    }

    private bool HandleSelectDrawingObjects(TeklaDrawingInteractionApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseSelectDrawingObjectsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.SelectObjectsByModelIds(parseResult.Request.TargetModelIds);
        if (result.SelectedDrawingObjectIds.Count == 0)
        {
            WriteRawJson(NoMatchingModelIdsInDrawingErrorJson);
            return true;
        }

        WriteSelectDrawingObjectsResult(result);
        return true;
    }

    private bool HandleFilterDrawingObjects(TeklaDrawingInteractionApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseFilterDrawingObjectsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.FilterObjects(
            parseResult.Request.ObjectType,
            parseResult.Request.SpecificType);
        if (!result.IsKnownType)
        {
            WriteUnknownDrawingTypeError(parseResult.Request.ObjectType);
            return true;
        }

        WriteFilterDrawingObjectsResult(result);
        return true;
    }

    private bool HandleGetDrawingContext(TeklaDrawingInteractionApi api)
    {
        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.GetDrawingContext();
        WriteDrawingContextResult(result);
        return true;
    }

    private bool HandleGetSheetObjectsDebug(TeklaDrawingInteractionApi api)
    {
        if (!EnsureActiveDrawing())
        {
            return true;
        }

        var result = api.GetSheetObjectsDebug();
        WriteSheetObjectsDebugResult(result);
        return true;
    }

    private void WriteSelectDrawingObjectsResult(SelectDrawingObjectsResult result)
    {
        WriteJson(new
        {
            selectedCount = result.SelectedDrawingObjectIds.Count,
            selectedDrawingObjectIds = result.SelectedDrawingObjectIds,
            selectedModelIds = result.SelectedModelIds
        });
    }

    private void WriteUnknownDrawingTypeError(string objectType)
    {
        WriteJson(new
        {
            error = $"Unknown drawing type: {objectType}",
            hint = "Use Tekla.Structures.Drawing type names, e.g. Mark, Part, DimensionBase, Text."
        });
    }

    private void WriteFilterDrawingObjectsResult(FilterDrawingObjectsResult result)
    {
        WriteJson(MapDrawingObjects(result.Objects));
    }

    private void WriteDrawingContextResult(DrawingContextResult result)
    {
        WriteJson(new
        {
            drawing = new
            {
                guid = result.Drawing.Guid,
                name = result.Drawing.Name,
                mark = result.Drawing.Mark,
                type = result.Drawing.Type,
                status = result.Drawing.Status
            },
            selectedCount = result.SelectedObjects.Count,
            selectedObjects = MapDrawingObjects(result.SelectedObjects)
        });
    }

    private void WriteSheetObjectsDebugResult(SheetObjectsDebugResult result)
    {
        WriteJson(new
        {
            drawing = new
            {
                guid = result.Drawing.Guid,
                name = result.Drawing.Name,
                mark = result.Drawing.Mark,
                type = result.Drawing.Type,
                status = result.Drawing.Status
            },
            sheetWidth = result.SheetWidth,
            sheetHeight = result.SheetHeight,
            totalObjectsScanned = result.TotalObjectsScanned,
            allObjectCount = result.AllObjects.Count,
            allObjects = result.AllObjects.Select(x => new
            {
                id = x.Id,
                type = x.Type,
                modelId = x.ModelId,
                isSheetLevel = x.IsSheetLevel,
                ownerViewType = x.OwnerViewType,
                ownerViewName = x.OwnerViewName,
                hasBoundingBox = x.HasBoundingBox,
                bboxMinX = x.BboxMinX,
                bboxMinY = x.BboxMinY,
                bboxMaxX = x.BboxMaxX,
                bboxMaxY = x.BboxMaxY
            }),
            reservedAreaCandidateCount = result.ReservedAreaCandidates.Count,
            reservedAreaCandidates = result.ReservedAreaCandidates.Select(x => new
            {
                minX = x.MinX,
                minY = x.MinY,
                maxX = x.MaxX,
                maxY = x.MaxY,
                width = x.Width,
                height = x.Height
            })
        });
    }

    private static IEnumerable<object> MapDrawingObjects(
        IEnumerable<DrawingObjectItem> drawingObjects)
    {
        return drawingObjects.Select(x => (object)new
        {
            id = x.Id,
            type = x.Type,
            modelId = x.ModelId
        });
    }
}
