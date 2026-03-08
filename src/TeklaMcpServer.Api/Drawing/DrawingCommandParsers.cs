using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public static class DrawingCommandParsers
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
                "filtersJson must be a JSON array like [{\\\"property\\\":\\\"Name\\\",\\\"value\\\":\\\"GA\\\"}]");
        }

        return FindDrawingsByPropertiesParseResult.Success(new FindDrawingsByPropertiesRequest
        {
            Filters = filters
        });
    }

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

    public static int? ParseOptionalViewId(string[] args, int index = 1)
    {
        if (args.Length > index && int.TryParse(args[index], out var viewId))
            return viewId;

        return null;
    }

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

    public static MoveViewParseResult ParseMoveViewRequest(string[] args)
    {
        if (args.Length < 4)
            return MoveViewParseResult.Fail("Usage: move_view <viewId> <dx> <dy> [abs]");

        if (!int.TryParse(args[1], out var viewId))
            return MoveViewParseResult.Fail("viewId must be an integer");

        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx) ||
            !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var dy))
        {
            return MoveViewParseResult.Fail("dx and dy must be numbers");
        }

        var absolute = args.Length > 4 && string.Equals(args[4], "abs", StringComparison.OrdinalIgnoreCase);
        return MoveViewParseResult.Success(new MoveViewRequest
        {
            ViewId = viewId,
            Dx = dx,
            Dy = dy,
            Absolute = absolute
        });
    }

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

    public static SetViewScaleParseResult ParseSetViewScaleRequest(string[] args)
    {
        if (args.Length < 3)
            return SetViewScaleParseResult.Fail("Usage: set_view_scale <viewIdsCsv> <scale>");

        var ids = ParseIntList(args[1]);
        if (ids.Count == 0)
            return SetViewScaleParseResult.Fail("No valid view IDs provided");

        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
            return SetViewScaleParseResult.Fail("scale must be a positive number");

        return SetViewScaleParseResult.Success(new SetViewScaleRequest
        {
            ViewIds = ids,
            Scale = scale
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

    public static FitViewsToSheetRequest ParseFitViewsToSheetRequest(string[] args)
    {
        var request = new FitViewsToSheetRequest
        {
            Margin = 10.0,
            Gap = 8.0,
            TitleBlockHeight = 0.0
        };

        if (args.Length > 1 &&
            double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var margin))
        {
            request.Margin = margin;
        }

        if (args.Length > 2 &&
            double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var gap))
        {
            request.Gap = gap;
        }

        if (args.Length > 3 &&
            double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var titleBlockHeight))
        {
            request.TitleBlockHeight = titleBlockHeight;
        }

        return request;
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

    public static PartGeometryInViewParseResult ParsePartGeometryInViewRequest(string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            return PartGeometryInViewParseResult.Fail("Usage: get_part_geometry_in_view <viewId> <modelId>");
        }

        return PartGeometryInViewParseResult.Success(new PartGeometryInViewRequest
        {
            ViewId = viewId,
            ModelId = modelId
        });
    }

    public static GridAxesParseResult ParseGridAxesRequest(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var viewId))
            return GridAxesParseResult.Fail("Usage: get_grid_axes <viewId>");

        return GridAxesParseResult.Success(new GridAxesRequest
        {
            ViewId = viewId
        });
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

    private static NonNegativeDoubleParseResult ParseOptionalNonNegativeDoubleArg(
        string[] args,
        int index,
        double defaultValue,
        string argumentName)
    {
        var value = defaultValue;
        if (args.Length > index)
        {
            if (!double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return NonNegativeDoubleParseResult.Fail($"{argumentName} must be a number");

            if (value < 0)
                return NonNegativeDoubleParseResult.Fail($"{argumentName} must be >= 0");
        }

        return NonNegativeDoubleParseResult.Success(value);
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

public sealed class FindDrawingsRequest
{
    public string NameContains { get; set; } = string.Empty;
    public string MarkContains { get; set; } = string.Empty;
}

public sealed class FindDrawingsParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public FindDrawingsRequest Request { get; private set; } = new();

    public static FindDrawingsParseResult Success(FindDrawingsRequest request) =>
        new() { IsValid = true, Request = request };

    public static FindDrawingsParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class OpenDrawingRequest
{
    public Guid RequestedGuid { get; set; }
}

public sealed class OpenDrawingParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public OpenDrawingRequest Request { get; private set; } = new();

    public static OpenDrawingParseResult Success(OpenDrawingRequest request) =>
        new() { IsValid = true, Request = request };

    public static OpenDrawingParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class ExportDrawingsPdfRequest
{
    public List<string> RequestedGuids { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
}

public sealed class ExportDrawingsPdfParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public ExportDrawingsPdfRequest Request { get; private set; } = new();

    public static ExportDrawingsPdfParseResult Success(ExportDrawingsPdfRequest request) =>
        new() { IsValid = true, Request = request };

    public static ExportDrawingsPdfParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class FindDrawingsByPropertiesRequest
{
    public List<DrawingPropertyFilter> Filters { get; set; } = new();
}

public sealed class FindDrawingsByPropertiesParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public FindDrawingsByPropertiesRequest Request { get; private set; } = new();

    public static FindDrawingsByPropertiesParseResult Success(FindDrawingsByPropertiesRequest request) =>
        new() { IsValid = true, Request = request };

    public static FindDrawingsByPropertiesParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class SelectDrawingObjectsRequest
{
    public List<int> TargetModelIds { get; set; } = new();
}

public sealed class SelectDrawingObjectsParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public SelectDrawingObjectsRequest Request { get; private set; } = new();

    public static SelectDrawingObjectsParseResult Success(SelectDrawingObjectsRequest request) =>
        new() { IsValid = true, Request = request };

    public static SelectDrawingObjectsParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class FilterDrawingObjectsRequest
{
    public string ObjectType { get; set; } = string.Empty;
    public string SpecificType { get; set; } = string.Empty;
}

public sealed class FilterDrawingObjectsParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public FilterDrawingObjectsRequest Request { get; private set; } = new();

    public static FilterDrawingObjectsParseResult Success(FilterDrawingObjectsRequest request) =>
        new() { IsValid = true, Request = request };

    public static FilterDrawingObjectsParseResult Fail(string error) =>
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

public sealed class GaDrawingCreationRequest
{
    public string DrawingProperties { get; set; } = "standard";
    public bool OpenDrawing { get; set; } = true;
    public string ViewName { get; set; } = string.Empty;
}

public sealed class GaDrawingCreationParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public GaDrawingCreationRequest Request { get; private set; } = new();

    public static GaDrawingCreationParseResult Success(GaDrawingCreationRequest request) =>
        new() { IsValid = true, Request = request };

    public static GaDrawingCreationParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class MoveViewRequest
{
    public int ViewId { get; set; }
    public double Dx { get; set; }
    public double Dy { get; set; }
    public bool Absolute { get; set; }
}

public sealed class MoveViewParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public MoveViewRequest Request { get; private set; } = new();

    public static MoveViewParseResult Success(MoveViewRequest request) =>
        new() { IsValid = true, Request = request };

    public static MoveViewParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class CreateDimensionRequest
{
    public int ViewId { get; set; }
    public double[] Points { get; set; } = Array.Empty<double>();
    public string Direction { get; set; } = "horizontal";
    public double Distance { get; set; } = 50.0;
    public string AttributesFile { get; set; } = string.Empty;
}

public sealed class CreateDimensionParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public CreateDimensionRequest Request { get; private set; } = new();

    public static CreateDimensionParseResult Success(CreateDimensionRequest request) =>
        new() { IsValid = true, Request = request };

    public static CreateDimensionParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class NonNegativeDoubleParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public double Value { get; private set; }

    public static NonNegativeDoubleParseResult Success(double value) =>
        new() { IsValid = true, Value = value };

    public static NonNegativeDoubleParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class SetViewScaleRequest
{
    public List<int> ViewIds { get; set; } = new();
    public double Scale { get; set; }
}

public sealed class SetViewScaleParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public SetViewScaleRequest Request { get; private set; } = new();

    public static SetViewScaleParseResult Success(SetViewScaleRequest request) =>
        new() { IsValid = true, Request = request };

    public static SetViewScaleParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class MoveDimensionRequest
{
    public int DimensionId { get; set; }
    public double Delta { get; set; }
}

public sealed class MoveDimensionParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public MoveDimensionRequest Request { get; private set; } = new();

    public static MoveDimensionParseResult Success(MoveDimensionRequest request) =>
        new() { IsValid = true, Request = request };

    public static MoveDimensionParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class FitViewsToSheetRequest
{
    public double Margin { get; set; }
    public double Gap { get; set; }
    public double TitleBlockHeight { get; set; }
}

public sealed class DeleteDimensionRequest
{
    public int DimensionId { get; set; }
}

public sealed class DeleteDimensionParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public DeleteDimensionRequest Request { get; private set; } = new();

    public static DeleteDimensionParseResult Success(DeleteDimensionRequest request) =>
        new() { IsValid = true, Request = request };

    public static DeleteDimensionParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class PartGeometryInViewRequest
{
    public int ViewId { get; set; }
    public int ModelId { get; set; }
}

public sealed class PartGeometryInViewParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public PartGeometryInViewRequest Request { get; private set; } = new();

    public static PartGeometryInViewParseResult Success(PartGeometryInViewRequest request) =>
        new() { IsValid = true, Request = request };

    public static PartGeometryInViewParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class GridAxesRequest
{
    public int ViewId { get; set; }
}

public sealed class GridAxesParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public GridAxesRequest Request { get; private set; } = new();

    public static GridAxesParseResult Success(GridAxesRequest request) =>
        new() { IsValid = true, Request = request };

    public static GridAxesParseResult Fail(string error) =>
        new() { IsValid = false, Error = error };
}

public sealed class CreatePartMarksRequest
{
    public string ContentAttributesCsv { get; set; } = string.Empty;
    public string MarkAttributesFile { get; set; } = string.Empty;
    public string FrameType { get; set; } = string.Empty;
    public string ArrowheadType { get; set; } = string.Empty;
}
