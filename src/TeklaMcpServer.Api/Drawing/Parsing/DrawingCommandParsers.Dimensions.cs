using System;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static CreateDimensionParseResult ParseCreateDimensionRequest(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], out var viewId))
        {
            return CreateDimensionParseResult.Fail("Usage: create_dimension <viewId> <pointsJson> <direction> <distance> [attributesFile]");
        }

        var pointsJson = args.Length > 2 ? args[2] : "[]";
        var direction = args.Length > 3 ? args[3] : "horizontal";
        var distance = args.Length > 4 && double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDistance)
            ? parsedDistance
            : 50.0;
        var attributesFile = args.Length > 5 ? args[5] : string.Empty;

        double[] points;
        try
        {
            points = JsonSerializer.Deserialize<double[]>(pointsJson) ?? Array.Empty<double>();
        }
        catch
        {
            return CreateDimensionParseResult.Fail("pointsJson must be a JSON array of numbers");
        }

        return CreateDimensionParseResult.Success(new CreateDimensionRequest
        {
            ViewId = viewId,
            Points = points,
            Direction = direction,
            Distance = distance,
            AttributesFile = attributesFile
        });
    }

    public static PlaceControlDiagonalsParseResult ParsePlaceControlDiagonalsRequest(string[] args)
    {
        int? viewId = null;
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
        {
            if (!int.TryParse(args[1], out var parsedViewId))
                return PlaceControlDiagonalsParseResult.Fail("viewId must be an integer");
            viewId = parsedViewId;
        }

        var distance = 60.0;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) || distance <= 0)
                return PlaceControlDiagonalsParseResult.Fail("distance must be a positive number");
        }

        var attributesFile = (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]))
            ? args[3]
            : "standard";

        return PlaceControlDiagonalsParseResult.Success(new PlaceControlDiagonalsRequest
        {
            ViewId = viewId,
            Distance = distance,
            AttributesFile = attributesFile
        });
    }

    public static MoveDimensionParseResult ParseMoveDimensionRequest(string[] args)
    {
        if (args.Length < 3 ||
            !int.TryParse(args[1], out var dimensionId) ||
            !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            return MoveDimensionParseResult.Fail("Usage: move_dimension <dimensionId> <delta>");
        }

        return MoveDimensionParseResult.Success(new MoveDimensionRequest
        {
            DimensionId = dimensionId,
            Delta = delta
        });
    }

    public static DeleteDimensionParseResult ParseDeleteDimensionRequest(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var dimensionId))
            return DeleteDimensionParseResult.Fail("Usage: delete_dimension <dimensionId>");

        return DeleteDimensionParseResult.Success(new DeleteDimensionRequest
        {
            DimensionId = dimensionId
        });
    }
}
