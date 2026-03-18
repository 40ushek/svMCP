using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDimensionCommands(string command, string[] args)
    {
        var api = new TeklaDrawingDimensionsApi();

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

            case "place_control_diagonals":
                return HandlePlaceControlDiagonals(api, args);

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

    private bool HandlePlaceControlDiagonals(TeklaDrawingDimensionsApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParsePlaceControlDiagonalsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.PlaceControlDiagonals(
            parseResult.Request.ViewId,
            parseResult.Request.Distance,
            parseResult.Request.AttributesFile);

        WriteJson(new
        {
            created = result.Created,
            createdCount = result.CreatedCount,
            viewId = result.ViewId,
            viewType = result.ViewType,
            rectangleLike = result.RectangleLike,
            requestedDiagonalCount = result.RequestedDiagonalCount,
            partsScanned = result.PartsScanned,
            sourceDimensionsScanned = result.SourceDimensionsScanned,
            candidatePoints = result.CandidatePoints,
            dimensionId = result.DimensionId,
            dimensionIds = result.DimensionIds,
            startPoint = result.StartPoint,
            endPoint = result.EndPoint,
            farthestDistance = result.FarthestDistance,
            selectViewMs = result.SelectViewMs,
            readGeometryMs = result.ReadGeometryMs,
            findExtremesMs = result.FindExtremesMs,
            createMs = result.CreateMs,
            commitMs = result.CommitMs,
            totalMs = result.TotalMs,
            error = result.Error
        });
        return true;
    }

    private void WriteGetDimensionsResult(GetDimensionsResult result)
    {
        static object? SerializeBounds(DrawingBoundsInfo? bounds)
        {
            if (bounds == null)
                return null;

            return new
            {
                minX = bounds.MinX,
                minY = bounds.MinY,
                maxX = bounds.MaxX,
                maxY = bounds.MaxY,
                width = bounds.Width,
                height = bounds.Height
            };
        }

        static object? SerializeLine(DrawingLineInfo? line)
        {
            if (line == null)
                return null;

            return new
            {
                startX = line.StartX,
                startY = line.StartY,
                endX = line.EndX,
                endY = line.EndY,
                length = line.Length
            };
        }

        WriteJson(new
        {
            total = result.Total,
            dimensions = result.Dimensions.Select(d => new
            {
                id = d.Id,
                type = d.Type,
                dimensionType = d.DimensionType,
                viewId = d.ViewId,
                viewType = d.ViewType,
                orientation = d.Orientation,
                distance = d.Distance,
                directionX = d.DirectionX,
                directionY = d.DirectionY,
                topDirection = d.TopDirection,
                bounds = SerializeBounds(d.Bounds),
                referenceLine = SerializeLine(d.ReferenceLine),
                segments = d.Segments.Select(s => new
                {
                    id = s.Id,
                    startX = s.StartX,
                    startY = s.StartY,
                    endX = s.EndX,
                    endY = s.EndY,
                    distance = s.Distance,
                    directionX = s.DirectionX,
                    directionY = s.DirectionY,
                    topDirection = s.TopDirection,
                    bounds = SerializeBounds(s.Bounds),
                    textBounds = SerializeBounds(s.TextBounds),
                    dimensionLine = SerializeLine(s.DimensionLine),
                    leadLineMain = SerializeLine(s.LeadLineMain),
                    leadLineSecond = SerializeLine(s.LeadLineSecond)
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
