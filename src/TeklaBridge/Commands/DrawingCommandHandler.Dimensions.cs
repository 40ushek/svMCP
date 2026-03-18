using System.Linq;
using System.Reflection;
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

            case "get_dimension_groups_debug":
                return HandleGetDimensionGroupsDebug(api, args);

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

    private bool HandleGetDimensionGroupsDebug(TeklaDrawingDimensionsApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var method = typeof(TeklaDrawingDimensionsApi).GetMethod("GetDimensionGroups", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            WriteError("Internal GetDimensionGroups() was not found.");
            return true;
        }

        var groups = method.Invoke(api, new object?[] { viewId }) as System.Collections.IEnumerable;
        if (groups == null)
        {
            WriteError("Internal GetDimensionGroups() returned null.");
            return true;
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

        var payload = groups.Cast<object>().Select(group => new
        {
            viewId = group.GetType().GetProperty("ViewId")?.GetValue(group),
            viewType = group.GetType().GetProperty("ViewType")?.GetValue(group),
            dimensionType = group.GetType().GetProperty("DimensionType")?.GetValue(group),
            orientation = group.GetType().GetProperty("Orientation")?.GetValue(group),
            topDirection = group.GetType().GetProperty("TopDirection")?.GetValue(group),
            direction = SerializeDirection(group.GetType().GetProperty("Direction")?.GetValue(group)),
            referenceLine = SerializeLine(group.GetType().GetProperty("ReferenceLine")?.GetValue(group) as DrawingLineInfo),
            bounds = SerializeBounds(group.GetType().GetProperty("Bounds")?.GetValue(group) as DrawingBoundsInfo),
            maximumDistance = group.GetType().GetProperty("MaximumDistance")?.GetValue(group),
            members = SerializeMembers(group.GetType().GetProperty("Members")?.GetValue(group) as System.Collections.IEnumerable)
        });

        WriteJson(new { groups = payload });
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

    private static object? SerializeDirection(object? direction)
    {
        if (direction == null)
            return null;

        var type = direction.GetType();
        var xField = type.GetField("Item1");
        var yField = type.GetField("Item2");
        var xProp = type.GetProperty("X");
        var yProp = type.GetProperty("Y");
        var x = xProp?.GetValue(direction) ?? xField?.GetValue(direction);
        var y = yProp?.GetValue(direction) ?? yField?.GetValue(direction);
        return new { x, y };
    }

    private static object SerializeMembers(System.Collections.IEnumerable? members)
    {
        if (members == null)
            return System.Array.Empty<object>();

        return members.Cast<object>().Select(member => new
        {
            dimensionId = member.GetType().GetProperty("DimensionId")?.GetValue(member),
            segmentId = member.GetType().GetProperty("SegmentId")?.GetValue(member),
            startX = member.GetType().GetProperty("StartX")?.GetValue(member),
            startY = member.GetType().GetProperty("StartY")?.GetValue(member),
            endX = member.GetType().GetProperty("EndX")?.GetValue(member),
            endY = member.GetType().GetProperty("EndY")?.GetValue(member),
            distance = member.GetType().GetProperty("Distance")?.GetValue(member),
            directionX = member.GetType().GetProperty("DirectionX")?.GetValue(member),
            directionY = member.GetType().GetProperty("DirectionY")?.GetValue(member),
            topDirection = member.GetType().GetProperty("TopDirection")?.GetValue(member),
            sortKey = member.GetType().GetProperty("SortKey")?.GetValue(member),
            bounds = SerializeBounds(member.GetType().GetProperty("Bounds")?.GetValue(member) as DrawingBoundsInfo),
            referenceLine = SerializeLine(member.GetType().GetProperty("ReferenceLine")?.GetValue(member) as DrawingLineInfo),
            leadLineMain = SerializeLine(member.GetType().GetProperty("LeadLineMain")?.GetValue(member) as DrawingLineInfo),
            leadLineSecond = SerializeLine(member.GetType().GetProperty("LeadLineSecond")?.GetValue(member) as DrawingLineInfo)
        });

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
    }
}
