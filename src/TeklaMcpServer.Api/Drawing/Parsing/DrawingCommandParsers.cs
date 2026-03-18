using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
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

    private static NonNegativeDoubleParseResult ParseOptionalNonNegativeDoubleArg(
        string[] args,
        int index,
        double defaultValue,
        string argumentName)
    {
        var value = defaultValue;
        if (args.Length > index)
        {
            if (!double.TryParse(args[index], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
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

public sealed class PlaceControlDiagonalsRequest
{
    public int? ViewId { get; set; }
    public double Distance { get; set; } = 60.0;
    public string AttributesFile { get; set; } = "standard";
}

public sealed class PlaceControlDiagonalsParseResult
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public PlaceControlDiagonalsRequest Request { get; private set; } = new();

    public static PlaceControlDiagonalsParseResult Success(PlaceControlDiagonalsRequest request) =>
        new() { IsValid = true, Request = request };

    public static PlaceControlDiagonalsParseResult Fail(string error) =>
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
    public double? Margin { get; set; }  // null = auto-read from drawing layout
    public double Gap { get; set; }
    public double TitleBlockHeight { get; set; }
    public bool KeepScale { get; set; }
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
