using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingGridApi : IDrawingGridApi
{
    public GetGridAxesResult GetGridAxes(int viewId)
    {
        var dh = new DrawingHandler();
        var activeDrawing = dh.GetActiveDrawing();
        if (activeDrawing == null)
            return Fail(viewId, "No drawing is currently open.");

        View? view = null;
        var viewEnum = activeDrawing.GetSheet().GetViews();
        while (viewEnum.MoveNext())
        {
            if (viewEnum.Current is View v && v.GetIdentifier().ID == viewId)
            {
                view = v;
                break;
            }
        }
        if (view == null)
            return Fail(viewId, $"View {viewId} not found.");

        var axes = new List<GridAxisInfo>();
        var gridObjects = view.GetAllObjects(new[] { typeof(GridLine) });

        while (gridObjects.MoveNext())
        {
            if (gridObjects.Current is not GridLine gl) continue;

            var start = gl.StartLabel.GridPoint;
            var end   = gl.EndLabel.GridPoint;
            var label = gl.StartLabel.GridLabelText ?? gl.EndLabel.GridLabelText ?? "";

            var dir = new Vector(end.X - start.X, end.Y - start.Y, 0);
            dir.Normalize();

            string direction;
            double coordinate;

            // Vertical grid line (runs along Y) â†’ divides X axis
            if (Math.Abs(dir.X) < 0.01 && Math.Abs(dir.Y) > 0.99)
            {
                direction  = "X";
                coordinate = Math.Round(start.X, 1);
            }
            // Horizontal grid line (runs along X) â†’ divides Y axis
            else if (Math.Abs(dir.Y) < 0.01 && Math.Abs(dir.X) > 0.99)
            {
                direction  = "Y";
                coordinate = Math.Round(start.Y, 1);
            }
            else
            {
                direction  = "other";
                coordinate = 0;
            }

            axes.Add(new GridAxisInfo
            {
                Guid       = gl.ModelIdentifier != null && gl.ModelIdentifier.GUID != Guid.Empty
                    ? gl.ModelIdentifier.GUID.ToString()
                    : null,
                Label      = label,
                Direction  = direction,
                Coordinate = coordinate,
                StartX     = Math.Round(start.X, 1),
                StartY     = Math.Round(start.Y, 1),
                EndX       = Math.Round(end.X,   1),
                EndY       = Math.Round(end.Y,   1)
            });
        }

        // Sort: X axes by coordinate, then Y axes by coordinate
        axes = axes
            .OrderBy(a => a.Direction)
            .ThenBy(a => a.Coordinate)
            .ToList();

        return new GetGridAxesResult { Success = true, ViewId = viewId, Axes = axes };
    }

    private static GetGridAxesResult Fail(int viewId, string error) =>
        new() { Success = false, ViewId = viewId, Error = error };
}

