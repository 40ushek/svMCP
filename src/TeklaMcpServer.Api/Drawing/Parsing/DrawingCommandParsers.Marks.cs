using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
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

    public static SetMarkContentParseResult ParseSetMarkContentRequest(string[] args)
    {
        return ParseSetMarkContentRequest(
            args.Length > 1 ? args[1] : string.Empty,
            args.Length > 2 ? args[2] : string.Empty,
            args.Length > 3 ? args[3] : string.Empty,
            args.Length > 4 ? args[4] : string.Empty,
            args.Length > 5 ? args[5] : string.Empty);
    }

    public static CreatePartMarksRequest ParseCreatePartMarksRequest(string[] args)
    {
        return new CreatePartMarksRequest
        {
            ContentAttributesCsv = args.Length > 1 ? args[1] : string.Empty,
            MarkAttributesFile = args.Length > 2 ? args[2] : string.Empty,
            FrameType = args.Length > 3 ? args[3] : string.Empty,
            ArrowheadType = args.Length > 4 ? args[4] : string.Empty
        };
    }

    public static NonNegativeDoubleParseResult ParseArrangeMarksGap(string[] args) =>
        ParseOptionalNonNegativeDoubleArg(args, 1, 2.0, "gap");

    public static NonNegativeDoubleParseResult ParseResolveMarkOverlapsMargin(string[] args) =>
        ParseOptionalNonNegativeDoubleArg(args, 1, 2.0, "margin");
}
