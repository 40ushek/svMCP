using System;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
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

    public static PartPointsInViewParseResult ParsePartPointsInViewRequest(string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], out var viewId)
            || !int.TryParse(args[2], out var modelId))
        {
            return PartPointsInViewParseResult.Fail("Usage: get_part_points_in_view <viewId> <modelId>");
        }

        return PartPointsInViewParseResult.Success(new PartPointsInViewRequest
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
}
