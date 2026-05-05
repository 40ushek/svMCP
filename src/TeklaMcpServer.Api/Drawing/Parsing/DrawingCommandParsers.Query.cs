using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static FindDrawingsParseResult ParseFindDrawingsRequest(string[] args)
    {
        var nameContains = args.Length > 1 ? args[1] : string.Empty;
        var markContains = args.Length > 2 ? args[2] : string.Empty;

        if (string.IsNullOrWhiteSpace(nameContains) && string.IsNullOrWhiteSpace(markContains))
            return FindDrawingsParseResult.Fail("Provide at least one filter: nameContains or markContains");

        return FindDrawingsParseResult.Success(new FindDrawingsRequest
        {
            NameContains = nameContains,
            MarkContains = markContains
        });
    }

    public static OpenDrawingParseResult ParseOpenDrawingRequest(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            return OpenDrawingParseResult.Fail("Missing drawing GUID");

        if (!Guid.TryParse(args[1], out var requestedGuid))
            return OpenDrawingParseResult.Fail("Invalid drawing GUID format");

        return OpenDrawingParseResult.Success(new OpenDrawingRequest
        {
            RequestedGuid = requestedGuid
        });
    }

    public static ExportDrawingsPdfParseResult ParseExportDrawingsPdfRequest(
        string[] args,
        string defaultOutputDirectory)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            return ExportDrawingsPdfParseResult.Fail(
                "Missing drawing GUID list (comma-separated)");
        }

        var requestedGuids = args[1]
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedGuids.Count == 0)
            return ExportDrawingsPdfParseResult.Fail("No valid drawing GUIDs provided");

        var outputDirectory = (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
            ? args[2]
            : defaultOutputDirectory;

        return ExportDrawingsPdfParseResult.Success(new ExportDrawingsPdfRequest
        {
            RequestedGuids = requestedGuids.ToList(),
            OutputDirectory = outputDirectory
        });
    }

    public static FindDrawingsByPropertiesParseResult ParseFindDrawingsByPropertiesRequest(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            return FindDrawingsByPropertiesParseResult.Fail("Missing filters JSON");

        var filters = DrawingPropertyFilterParser.Parse(args[1]);
        if (filters.Count == 0)
        {
            return FindDrawingsByPropertiesParseResult.Fail(
                "filtersJson must be a JSON array like [{\\\"property\\\":\\\"Name\\\",\\\"operator\\\":\\\"contains\\\",\\\"value\\\":\\\"GA\\\"}]");
        }

        return FindDrawingsByPropertiesParseResult.Success(new FindDrawingsByPropertiesRequest
        {
            Filters = filters
        });
    }
}
