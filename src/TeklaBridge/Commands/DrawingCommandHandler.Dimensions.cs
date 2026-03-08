using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDimensionCommands(string command, string[] args)
    {
        var api = new TeklaDrawingDimensionsApi(_model);

        switch (command)
        {
            case "get_drawing_dimensions":
                return HandleGetDrawingDimensions(api, args);

            case "move_dimension":
                return HandleMoveDimension(api, args);

            case "create_dimension":
                return HandleCreateDimension(api, args);

            case "delete_dimension":
                return HandleDeleteDimension(api, args);

            default:
                return false;
        }
    }

    private bool HandleGetDrawingDimensions(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var result = api.GetDimensions(viewId);
        WriteGetDimensionsResult(result);
        return true;
    }

    private bool HandleMoveDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseMoveDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.MoveDimension(parseResult.Request.DimensionId, parseResult.Request.Delta);
        WriteMoveDimensionResult(result);
        return true;
    }

    private bool HandleCreateDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseCreateDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.CreateDimension(
            parseResult.Request.ViewId,
            parseResult.Request.Points,
            parseResult.Request.Direction,
            parseResult.Request.Distance,
            parseResult.Request.AttributesFile);
        WriteCreateDimensionResult(result);
        return true;
    }

    private bool HandleDeleteDimension(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseDeleteDimensionRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.DeleteDimension(parseResult.Request.DimensionId);
        if (!result.HasActiveDrawing)
        {
            WriteRawJson(NoActiveDrawingErrorJson);
            return true;
        }

        WriteDeleteDimensionResult(result);
        return true;
    }

    private void WriteGetDimensionsResult(GetDimensionsResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            dimensions = result.Dimensions.Select(d => new
            {
                id = d.Id,
                type = d.Type,
                distance = d.Distance,
                segments = d.Segments.Select(s => new
                {
                    id = s.Id,
                    startX = s.StartX,
                    startY = s.StartY,
                    endX = s.EndX,
                    endY = s.EndY
                })
            })
        });
    }

    private void WriteMoveDimensionResult(MoveDimensionResult result)
    {
        WriteJson(new
        {
            moved = result.Moved,
            dimensionId = result.DimensionId,
            newDistance = result.NewDistance
        });
    }

    private void WriteCreateDimensionResult(CreateDimensionResult result)
    {
        WriteJson(new
        {
            created = result.Created,
            dimensionId = result.DimensionId,
            viewId = result.ViewId,
            pointCount = result.PointCount,
            error = result.Error
        });
    }

    private void WriteDeleteDimensionResult(DeleteDimensionResult result)
    {
        WriteJson(new
        {
            deleted = result.Deleted,
            dimensionId = result.DimensionId
        });
    }
}
