using System;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static ModelObjectDrawingCreationParseResult ParseModelObjectDrawingCreationRequest(
        string? modelObjectIdRaw,
        string? drawingPropertiesRaw,
        string? openDrawingRaw)
    {
        if (!int.TryParse(modelObjectIdRaw, out var modelObjectId) || modelObjectId <= 0)
            return ModelObjectDrawingCreationParseResult.Fail("modelObjectId must be a positive integer");

        var drawingProperties = string.IsNullOrWhiteSpace(drawingPropertiesRaw)
            ? "standard"
            : drawingPropertiesRaw!;

        var openDrawing = true;
        if (!string.IsNullOrWhiteSpace(openDrawingRaw) && bool.TryParse(openDrawingRaw, out var parsedOpen))
            openDrawing = parsedOpen;

        return ModelObjectDrawingCreationParseResult.Success(new ModelObjectDrawingCreationRequest
        {
            ModelObjectId = modelObjectId,
            DrawingProperties = drawingProperties,
            OpenDrawing = openDrawing
        });
    }

    public static ModelObjectDrawingCreationParseResult ParseModelObjectDrawingCreationRequest(string[] args)
    {
        return ParseModelObjectDrawingCreationRequest(
            args.Length > 1 ? args[1] : string.Empty,
            args.Length > 2 ? args[2] : string.Empty,
            args.Length > 3 ? args[3] : string.Empty);
    }

    public static GaDrawingCreationParseResult ParseGaDrawingCreationRequest(string[] args)
    {
        var drawingProperties = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? args[1]
            : "standard";

        var openDrawing = true;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) && bool.TryParse(args[2], out var parsedOpen))
            openDrawing = parsedOpen;

        var viewName = args.Length > 3 ? args[3] : string.Empty;
        if (string.IsNullOrWhiteSpace(viewName))
        {
            return GaDrawingCreationParseResult.Fail(
                "viewName is required for this Tekla version. Pass a saved model view name.");
        }

        return GaDrawingCreationParseResult.Success(new GaDrawingCreationRequest
        {
            DrawingProperties = drawingProperties,
            OpenDrawing = openDrawing,
            ViewName = viewName
        });
    }
}
