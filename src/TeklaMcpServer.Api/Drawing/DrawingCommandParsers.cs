using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public static class DrawingCommandParsers
{
    public static List<int> ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new List<int>();

        return csv!
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => int.TryParse(x, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();
    }

    public static SetMarkContentParseResult ParseSetMarkContentRequest(
        string? targetIdsCsv,
        string? contentElementsCsv,
        string? fontName,
        string? fontColorRaw,
        string? fontHeightRaw)
    {
        if (string.IsNullOrWhiteSpace(targetIdsCsv))
            return SetMarkContentParseResult.Fail("Missing element IDs (drawing IDs or model IDs)");

        var targetIds = ParseIntList(targetIdsCsv).ToHashSet();
        if (targetIds.Count == 0)
            return SetMarkContentParseResult.Fail("No valid IDs provided");

        var requestedContentElements = string.IsNullOrWhiteSpace(contentElementsCsv)
            ? new List<string>()
            : contentElementsCsv!
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

        var updateContent = requestedContentElements.Count > 0;
        var updateFontName = !string.IsNullOrWhiteSpace(fontName);
        var updateFontColor = !string.IsNullOrWhiteSpace(fontColorRaw);
        var updateFontHeight = !string.IsNullOrWhiteSpace(fontHeightRaw);

        if (!updateContent && !updateFontName && !updateFontColor && !updateFontHeight)
            return SetMarkContentParseResult.Fail("No changes requested. Provide content elements and/or font attributes.");

        var parsedFontHeight = 0.0;
        if (updateFontHeight &&
            (!double.TryParse(fontHeightRaw, out parsedFontHeight) || parsedFontHeight <= 0))
        {
            return SetMarkContentParseResult.Fail("fontHeight must be a positive number");
        }

        var parsedColor = DrawingColors.Black;
        if (updateFontColor && !Enum.TryParse(fontColorRaw, true, out parsedColor))
        {
            return SetMarkContentParseResult.Fail("Invalid fontColor. Use DrawingColors enum values, e.g. Black, Red, Blue");
        }

        return SetMarkContentParseResult.Success(new SetMarkContentRequest
        {
            TargetIds = targetIds.ToList(),
            RequestedContentElements = requestedContentElements,
            UpdateContent = updateContent,
            UpdateFontName = updateFontName,
            FontName = fontName ?? string.Empty,
            UpdateFontColor = updateFontColor,
            FontColorValue = (int)parsedColor,
            UpdateFontHeight = updateFontHeight,
            FontHeight = parsedFontHeight
        });
    }

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
}

public sealed class SetMarkContentParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public SetMarkContentRequest Request { get; private set; } = new();

    public static SetMarkContentParseResult Success(SetMarkContentRequest request) =>
        new() { IsValid = true, Request = request };

    public static SetMarkContentParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class ModelObjectDrawingCreationRequest
{
    public int ModelObjectId { get; set; }
    public string DrawingProperties { get; set; } = "standard";
    public bool OpenDrawing { get; set; } = true;
}

public sealed class ModelObjectDrawingCreationParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public ModelObjectDrawingCreationRequest Request { get; private set; } = new();

    public static ModelObjectDrawingCreationParseResult Success(ModelObjectDrawingCreationRequest request) =>
        new() { IsValid = true, Request = request };

    public static ModelObjectDrawingCreationParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}
