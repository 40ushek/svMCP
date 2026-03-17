using System;
using System.Globalization;

namespace TeklaMcpServer.Api.Drawing;

public static partial class DrawingCommandParsers
{
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

    public static FitViewsToSheetRequest ParseFitViewsToSheetRequest(string[] args)
    {
        var request = new FitViewsToSheetRequest { Margin = null, Gap = 8.0, TitleBlockHeight = 0.0 };

        if (args.Length > 1 &&
            double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var margin))
            request.Margin = margin == 0 ? (double?)null : margin;  // 0 from MCP = auto (null); negative passes through to API validation

        if (args.Length > 2 &&
            double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var gap))
            request.Gap = gap;

        if (args.Length > 3 &&
            double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var titleBlockHeight))
            request.TitleBlockHeight = titleBlockHeight;

        return request;
    }
}
