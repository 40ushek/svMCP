using System;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDrawingCreationCommands(string command, string[] args)
    {
        var api = new TeklaDrawingCreationApi(_model);

        switch (command)
        {
            case "create_ga_drawing":
                return HandleCreateGaDrawing(api, args);

            case "create_single_part_drawing":
            {
                return TryCreateModelObjectDrawing(
                    api,
                    args,
                    static (api, modelObjectId, drawingProperties, openDrawing) =>
                        api.CreateSinglePartDrawing(modelObjectId, drawingProperties, openDrawing));
            }

            case "create_assembly_drawing":
            {
                return TryCreateModelObjectDrawing(
                    api,
                    args,
                    static (api, modelObjectId, drawingProperties, openDrawing) =>
                        api.CreateAssemblyDrawing(modelObjectId, drawingProperties, openDrawing));
            }

            default:
                return false;
        }
    }

    private bool HandleCreateGaDrawing(TeklaDrawingCreationApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseGaDrawingCreationRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.CreateGaDrawing(
            parseResult.Request.ViewName,
            parseResult.Request.DrawingProperties,
            parseResult.Request.OpenDrawing);
        if (!result.Created)
        {
            WriteGaDrawingCreationFailure(result.ErrorDetails, parseResult.Request.ViewName);
            return true;
        }

        WriteGaDrawingCreationResult(result);
        return true;
    }

    private bool TryCreateModelObjectDrawing(
        TeklaDrawingCreationApi api,
        string[] args,
        Func<TeklaDrawingCreationApi, int, string, bool, DrawingCreationResult> createDrawing)
    {
        var parseResult = DrawingCommandParsers.ParseModelObjectDrawingCreationRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = createDrawing(
            api,
            parseResult.Request.ModelObjectId,
            parseResult.Request.DrawingProperties,
            parseResult.Request.OpenDrawing);
        WriteDrawingCreationResult(result);
        return true;
    }

    private void WriteDrawingCreationResult(DrawingCreationResult result)
    {
        WriteJson(new
        {
            created = result.Created,
            opened = result.Opened,
            drawingId = result.DrawingId,
            drawingType = result.DrawingType,
            modelObjectId = result.ModelObjectId,
            drawingProperties = result.DrawingProperties
        });
    }

    private void WriteGaDrawingCreationFailure(string details, string viewName)
    {
        WriteJson(new
        {
            error = "Failed to create GA drawing",
            details,
            viewName
        });
    }

    private void WriteGaDrawingCreationResult(GaDrawingCreationResult result)
    {
        WriteJson(new
        {
            created = true,
            drawingType = "GA",
            viewName = result.ViewName,
            drawingProperties = result.DrawingProperties,
            openDrawing = result.OpenDrawing
        });
    }
}
