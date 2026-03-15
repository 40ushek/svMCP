using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static SelectDrawingObjectsParseResult ParseSelectDrawingObjectsRequest(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            return SelectDrawingObjectsParseResult.Fail(
                "Missing model object IDs. Use comma-separated IDs.");
        }

        var targetModelIds = ParseIntList(args[1]).ToHashSet();
        if (targetModelIds.Count == 0)
            return SelectDrawingObjectsParseResult.Fail("No valid model object IDs provided");

        return SelectDrawingObjectsParseResult.Success(new SelectDrawingObjectsRequest
        {
            TargetModelIds = targetModelIds.ToList()
        });
    }

    public static FilterDrawingObjectsParseResult ParseFilterDrawingObjectsRequest(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            return FilterDrawingObjectsParseResult.Fail(
                "Missing objectType. Example: Mark, Part, DimensionBase");
        }

        return FilterDrawingObjectsParseResult.Success(new FilterDrawingObjectsRequest
        {
            ObjectType = args[1],
            SpecificType = args.Length > 2 ? args[2] : string.Empty
        });
    }
}
