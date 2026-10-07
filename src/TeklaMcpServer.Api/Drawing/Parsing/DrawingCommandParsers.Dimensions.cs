using System;
using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
    public static CreateDimensionsBatchRequest ParseCreateDimensionsBatchRequest(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var viewId) || viewId <= 0)
            throw new ArgumentException("create_dimensions_batch requires viewId, contextId and chainsJson");
        using var document = JsonDocument.Parse(args[3]);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("chainsJson must be an array");
        if (document.RootElement.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.Object &&
            entry.EnumerateObject().Any(property => property.Name.Equals("dropPoints", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dropPointIds", StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Per-point removals are not supported; select a prepared chain or supply explicit pointIds");
        return new CreateDimensionsBatchRequest {
            ViewId = viewId, ContextId = args[2],
            Chains = JsonSerializer.Deserialize<System.Collections.Generic.List<BatchDimensionChain>>(
                args[3], new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("chainsJson must be an array"),
            RuleSet = args.Length > 4 && !string.IsNullOrWhiteSpace(args[4])
                ? TeklaMcpServer.Shared.DimensionPreviewQuestions.NormalizeRuleSet(args[4]) : string.Empty,
            ChainView = TeklaMcpServer.Shared.DimensionPreviewQuestions.NormalizeChainView(args.Length > 5 ? args[5] : null)
        };
    }

    public static CreateDimensionParseResult ParseCreateDimensionRequest(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], out var viewId))
        {
            return CreateDimensionParseResult.Fail("Usage: create_dimension <viewId> <pointsJson-or-empty> <direction> [distance] [attributesFile] [paperGapMm] [excludePrefixes] [excludeMaterials] [contextId] [pointIdsJson] [row] [ruleSet]");
        }

        var ruleSet = args.Length > 12 ? args[12].Trim() : string.Empty;
        if (ruleSet.Length > 0)
        {
            try { ruleSet = TeklaMcpServer.Shared.DimensionPreviewQuestions.NormalizeRuleSet(ruleSet); }
            catch (ArgumentException ex) { return CreateDimensionParseResult.Fail(ex.Message); }
        }
        var pointsJson = args.Length > 2 ? args[2] : "[]";
        var direction = args.Length > 3 ? args[3] : "horizontal";
        double? distance = null;
        if (args.Length > 4 && !string.IsNullOrWhiteSpace(args[4]))
        {
            if (!double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDistance))
                return CreateDimensionParseResult.Fail("distance must be a number when supplied");
            distance = parsedDistance;
        }
        var attributesFile = args.Length > 5 ? args[5] : string.Empty;
        double? paperGapMm = null;
        if (args.Length > 6 && !string.IsNullOrWhiteSpace(args[6]))
        {
            if (!double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedGap))
                return CreateDimensionParseResult.Fail("paperGapMm must be a number when supplied");
            paperGapMm = parsedGap;
        }
        if (distance.HasValue && paperGapMm.HasValue)
            return CreateDimensionParseResult.Fail("Specify either distance or paperGapMm, not both");

        var row = 1;
        if (!distance.HasValue && !paperGapMm.HasValue &&
            args.Length > 11 && !string.IsNullOrWhiteSpace(args[11]) &&
            (!int.TryParse(args[11], NumberStyles.None, CultureInfo.InvariantCulture, out row) || row < 1))
            return CreateDimensionParseResult.Fail("row must be a positive integer");

        var contextId = args.Length > 9 ? args[9].Trim() : string.Empty;
        var pointIdsJson = args.Length > 10 ? args[10] : string.Empty;
        var hasContextId = contextId.Length > 0;
        var hasPointIds = !string.IsNullOrWhiteSpace(pointIdsJson);
        if (hasContextId != hasPointIds)
            return CreateDimensionParseResult.Fail("contextId and pointIdsJson must be supplied together");

        double[] points;
        string[] pointIds = Array.Empty<string>();
        try
        {
            points = string.IsNullOrWhiteSpace(pointsJson)
                ? Array.Empty<double>()
                : JsonSerializer.Deserialize<double[]>(pointsJson) ?? Array.Empty<double>();
        }
        catch
        {
            return CreateDimensionParseResult.Fail("pointsJson must be a JSON array of numbers");
        }

        if (hasContextId)
        {
            try { pointIds = JsonSerializer.Deserialize<string[]>(pointIdsJson) ?? Array.Empty<string>(); }
            catch { return CreateDimensionParseResult.Fail("pointIdsJson must be a JSON array of strings"); }
            if (pointIds.Length < 2 || pointIds.Any(string.IsNullOrWhiteSpace))
                return CreateDimensionParseResult.Fail("pointIdsJson must contain at least two non-empty point ids");
            if (points.Length > 0)
                return CreateDimensionParseResult.Fail("Supply either pointsJson or contextId with pointIdsJson, not both");
            if ((args.Length > 7 && !string.IsNullOrWhiteSpace(args[7])) ||
                (args.Length > 8 && !string.IsNullOrWhiteSpace(args[8])))
                return CreateDimensionParseResult.Fail("excludePrefixes/excludeMaterials are part of contextId; do not pass filters with point ids");
        }
        else if (points.Length == 0)
        {
            return CreateDimensionParseResult.Fail("Supply pointsJson or contextId with pointIdsJson");
        }

        return CreateDimensionParseResult.Success(new CreateDimensionRequest
        {
            ViewId = viewId,
            Points = points,
            ContextId = contextId,
            PointIds = pointIds,
            Direction = direction,
            Distance = distance,
            AttributesFile = attributesFile,
            PaperGapMm = paperGapMm,
            Row = row,
            RuleSet = ruleSet,
            ExcludePrefixes = args.Length > 7 ? args[7] : string.Empty,
            ExcludeMaterials = args.Length > 8 ? args[8] : string.Empty
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

        // args[4] not present → default [1,2,5]; empty string → include all (no filter); otherwise parse list
        int[] includeMaterialTypes;
        if (args.Length <= 4)
        {
            includeMaterialTypes = new int[] { 1, 2, 5 };
        }
        else if (string.IsNullOrWhiteSpace(args[4]))
        {
            includeMaterialTypes = System.Array.Empty<int>();
        }
        else
        {
            var parts = args[4].Split(',');
            var parsed = new System.Collections.Generic.List<int>();
            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out var mt))
                    parsed.Add(mt);
            }
            includeMaterialTypes = parsed.ToArray();
        }

        return PlaceControlDiagonalsParseResult.Success(new PlaceControlDiagonalsRequest
        {
            ViewId = viewId,
            Distance = distance,
            AttributesFile = attributesFile,
            IncludeMaterialTypes = includeMaterialTypes
        });
    }

    public static PlaceContourAngleDimensionsParseResult ParsePlaceContourAngleDimensionsRequest(string[] args)
    {
        int? viewId = null;
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
        {
            if (!int.TryParse(args[1], out var parsedViewId))
                return PlaceContourAngleDimensionsParseResult.Fail("viewId must be an integer");
            viewId = parsedViewId;
        }

        var distance = 4.0;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) || distance < 0)
                return PlaceContourAngleDimensionsParseResult.Fail("distance must be a non-negative number");
        }

        var attributesFile = (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]))
            ? args[3]
            : "standard";

        var skipRightAngles = true;
        if (args.Length > 4 && !string.IsNullOrWhiteSpace(args[4]))
        {
            if (!bool.TryParse(args[4], out skipRightAngles))
                return PlaceContourAngleDimensionsParseResult.Fail("skipRightAngles must be true or false");
        }

        return PlaceContourAngleDimensionsParseResult.Success(new PlaceContourAngleDimensionsRequest
        {
            ViewId = viewId,
            Distance = distance,
            AttributesFile = attributesFile,
            SkipRightAngles = skipRightAngles
        });
    }

    public static PlaceContourRadiusDimensionsParseResult ParsePlaceContourRadiusDimensionsRequest(string[] args)
    {
        int? viewId = null;
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
        {
            if (!int.TryParse(args[1], out var parsedViewId))
                return PlaceContourRadiusDimensionsParseResult.Fail("viewId must be an integer");
            viewId = parsedViewId;
        }

        var distance = 0.0;
        if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) || distance < 0)
                return PlaceContourRadiusDimensionsParseResult.Fail("distance must be a non-negative number");
        }

        var attributesFile = (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]))
            ? args[3]
            : "standard";

        return PlaceContourRadiusDimensionsParseResult.Success(new PlaceContourRadiusDimensionsRequest
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

    public static DimensionContextsParseResult ParseDimensionContextsRequest(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
            return DimensionContextsParseResult.Fail("Usage: get_dimension_contexts <viewId>");

        return DimensionContextsParseResult.Success(new DimensionContextsRequest
        {
            ViewId = viewId
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

    public static DeleteDimensionsBatchParseResult ParseDeleteDimensionsBatchRequest(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            return DeleteDimensionsBatchParseResult.Fail("Usage: delete_dimensions_batch <dimensionIdsJson>");
        int[] ids;
        try
        {
            ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(args[1])
                ?? throw new ArgumentException("dimensionIds must be a JSON array of integers");
        }
        catch (System.Text.Json.JsonException)
        {
            return DeleteDimensionsBatchParseResult.Fail("dimensionIds must be a JSON array of integers");
        }
        if (ids.Length == 0)
            return DeleteDimensionsBatchParseResult.Fail("dimensionIds must not be empty");

        return DeleteDimensionsBatchParseResult.Success(new DeleteDimensionsBatchRequest
        {
            DimensionIds = ids
        });
    }

    public static CombineDimensionsParseResult ParseCombineDimensionsRequest(string[] args)
    {
        int? viewId = null;
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
        {
            if (!int.TryParse(args[1], out var parsedViewId))
                return CombineDimensionsParseResult.Fail("viewId must be an integer");
            viewId = parsedViewId;
        }

        var dimensionIdsRaw = args.Length > 2 ? args[2] : string.Empty;
        var dimensionIds = ParseIntList(dimensionIdsRaw);
        if (!string.IsNullOrWhiteSpace(dimensionIdsRaw) && dimensionIds.Count == 0)
            return CombineDimensionsParseResult.Fail("dimensionIds must be a comma-separated list of integers");

        var previewOnly = false;
        if (args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) &&
            !bool.TryParse(args[3], out previewOnly))
        {
            return CombineDimensionsParseResult.Fail("previewOnly must be true or false");
        }

        return CombineDimensionsParseResult.Success(new CombineDimensionsRequest
        {
            ViewId = viewId,
            DimensionIds = dimensionIds,
            PreviewOnly = previewOnly
        });
    }
}
