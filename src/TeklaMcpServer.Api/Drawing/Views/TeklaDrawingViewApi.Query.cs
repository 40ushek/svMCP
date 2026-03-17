using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingViewApi
{
    public DrawingViewsResult GetViews()
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        double sheetW = 0;
        double sheetH = 0;
        try
        {
            var ss = activeDrawing.Layout.SheetSize;
            sheetW = ss.Width;
            sheetH = ss.Height;
        }
        catch
        {
        }

        var result = new DrawingViewsResult { SheetWidth = sheetW, SheetHeight = sheetH };
        foreach (var v in EnumerateViews(activeDrawing))
            result.Views.Add(ToInfo(v));

        return result;
    }

    /// <summary>
    /// Returns debug overlay shapes for all primitives of a layout table.
    /// Lines: cyan=vertical/short, gray=spanning-horizontal.
    /// Text: yellow rectangles (approximate).
    /// Other: white crosses.
    /// </summary>
    public DrawingDebugOverlayRequest BuildTablePrimitiveOverlay(int tableId, string group = "table_primitives")
    {
        var request = new DrawingDebugOverlayRequest { Group = group, ClearGroupFirst = true };
        bool editorOpened = false;
        try
        {
            LayoutManager.OpenEditor();
            editorOpened = true;
            using var conn = new PresentationConnection();
            var segment = conn.Service.GetObjectPresentation(tableId);
            if (segment != null)
                CollectPrimitiveShapes(segment, request.Shapes);
        }
        catch { }
        finally
        {
            if (editorOpened) try { LayoutManager.CloseEditor(); } catch { }
        }
        return request;
    }

    private static void CollectPrimitiveShapes(PrimitiveBase primitive, List<DrawingDebugShape> shapes)
    {
        switch (primitive)
        {
            case Segment seg:
                foreach (var c in seg.Primitives) CollectPrimitiveShapes(c, shapes);
                return;
            case PrimitiveGroup grp:
                foreach (var c in grp.Primitives) CollectPrimitiveShapes(c, shapes);
                return;
            case LinePrimitive line:
            {
                var dx = System.Math.Abs(line.EndPoint.X - line.StartPoint.X);
                var dy = System.Math.Abs(line.EndPoint.Y - line.StartPoint.Y);
                bool spanning = dx > 20.0 && dy < 1.0;
                shapes.Add(new DrawingDebugShape
                {
                    Kind = "line",
                    X1 = line.StartPoint.X, Y1 = line.StartPoint.Y,
                    X2 = line.EndPoint.X,   Y2 = line.EndPoint.Y,
                    Color = spanning ? "blue" : "cyan"
                });
                return;
            }
            case TextPrimitive text:
            {
                // Approximate text box as rectangle
                var w = System.Math.Max(text.Height, text.Text?.Length > 0
                    ? text.Text.Length * text.Height * System.Math.Max(text.Proportion, 0.5) : text.Height);
                shapes.Add(new DrawingDebugShape
                {
                    Kind = "rectangle",
                    X1 = text.Position.X, Y1 = text.Position.Y,
                    X2 = text.Position.X + w, Y2 = text.Position.Y + text.Height,
                    Color = "yellow"
                });
                return;
            }
        }
    }

    public DrawingReservedAreasResult GetReservedAreas(double margin)
    {
        var drawing = new DrawingHandler().GetActiveDrawing()
            ?? throw new DrawingNotOpenException();

        var rawTables = DrawingReservedAreaReader.ReadLayoutTableGeometries();
        var usableMaxX = drawing.Layout.SheetSize.Width  - margin;
        var usableMaxY = drawing.Layout.SheetSize.Height - margin;
        var filteredTables = DrawingReservedAreaReader.ReadLayoutTableGeometries(usableMaxX, usableMaxY);
        var merged = DrawingReservedAreaReader.Read(drawing, margin, 0.0);

        // Read LayoutTable metadata and drawing frames
        var layoutTableInfos = new System.Collections.Generic.List<LayoutTableInfo>();
        var drawingFrames = new System.Collections.Generic.List<DrawingFrameInfo>();
        try
        {
            LayoutManager.OpenEditor();
            try
            {
                var tableIds = TableLayout.GetCurrentTables();
                if (tableIds != null)
                {
                    foreach (var id in tableIds)
                    {
                        var lt = new LayoutTable { Id = id };
                        if (lt.Select())
                            layoutTableInfos.Add(new LayoutTableInfo
                            {
                                Id = lt.Id,
                                Name = lt.Name,
                                FileName = lt.FileName,
                                Scale = lt.Scale,
                                XOffset = lt.XOffset,
                                YOffset = lt.YOffset,
                                TableCorner = lt.TableCorner,
                                RefCorner = lt.RefCorner,
                                OverlapWithViews = lt.OverlapVithViews
                            });
                    }
                }

                var frames = LayoutManager.GetDrawingFrames();
                if (frames != null)
                {
                    foreach (var f in frames)
                        drawingFrames.Add(new DrawingFrameInfo
                        {
                            Active = f.Item1,
                            X = f.Item2,
                            Y = f.Item3,
                            W = f.Item4,
                            H = f.Item5,
                            Corner = f.Item6
                        });
                }
            }
            finally
            {
                LayoutManager.CloseEditor();
            }
        }
        catch { }

        return new DrawingReservedAreasResult
        {
            SheetWidth = drawing.Layout.SheetSize.Width,
            SheetHeight = drawing.Layout.SheetSize.Height,
            Margin = margin,
            RawTables = rawTables,
            FilteredTables = filteredTables,
            MergedAreas = merged,
            LayoutTables = layoutTableInfos,
            DrawingFrames = drawingFrames
        };
    }
}
