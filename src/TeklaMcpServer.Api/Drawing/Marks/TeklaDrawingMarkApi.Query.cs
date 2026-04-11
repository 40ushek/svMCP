using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Algorithms.Geometry;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingMarkApi
{
    public GetMarksResult GetMarks(int? viewId)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var previousAutoFetch = DrawingEnumeratorBase.AutoFetch;
        DrawingEnumeratorBase.AutoFetch = false;
        try
        {
            var viewsToQuery = viewId.HasValue
                ? new[] { EnumerateViews(activeDrawing).FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                    ?? throw new ViewNotFoundException(viewId.Value) }
                : EnumerateViews(activeDrawing).ToArray();

            var marks = new List<DrawingMarkInfo>();
            var seenIds = new HashSet<int>();
            var contextBuilder = new MarksViewContextBuilder();

            foreach (var view in viewsToQuery)
            {
                var viewContext = contextBuilder.Build(view, _model);
                var contextsById = viewContext.Marks.ToDictionary(item => item.MarkId);
                var markObjects = view.GetAllObjects(typeof(Mark));

                while (markObjects.MoveNext())
                {
                    if (markObjects.Current is not Mark mark)
                        continue;

                    var markId = mark.GetIdentifier().ID;
                    if (!seenIds.Add(markId))
                        continue;

                    var ins = mark.InsertionPoint;
                    if (!contextsById.TryGetValue(markId, out var markContext))
                        continue;

                    var info = CreateDrawingMarkInfo(
                        markId,
                        ins.X,
                        ins.Y,
                        mark.Attributes.Angle,
                        CreateArrowHeadInfo(mark),
                        CreateLeaderLineInfos(mark),
                        markContext);

                    marks.Add(info);
                }
            }

            return new GetMarksResult { Total = marks.Count, Marks = marks, Overlaps = BuildOverlaps(marks) };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }

    internal static List<MarkOverlap> BuildOverlaps(IReadOnlyList<DrawingMarkInfo> marks)
    {
        var overlaps = new List<MarkOverlap>();
        for (int i = 0; i < marks.Count; i++)
        for (int j = i + 1; j < marks.Count; j++)
        {
            var a = marks[i];
            var b = marks[j];
            // Skip zero-size marks — they have degenerate geometry and produce false overlaps.
            if (ShouldSkipOverlapComparison(a) || ShouldSkipOverlapComparison(b))
                continue;

            var overlapsDetected =
                a.ResolvedGeometry?.Corners is { Count: >= 3 } aCorners &&
                b.ResolvedGeometry?.Corners is { Count: >= 3 } bCorners
                    ? PolygonGeometry.Intersects(aCorners, bCorners)
                    : PolygonGeometry.RectanglesOverlap(
                        a.BboxMinX,
                        a.BboxMinY,
                        a.BboxMaxX,
                        a.BboxMaxY,
                        b.BboxMinX,
                        b.BboxMinY,
                        b.BboxMaxX,
                        b.BboxMaxY);

            if (overlapsDetected)
                overlaps.Add(new MarkOverlap { IdA = a.Id, IdB = b.Id });
        }

        return overlaps;
    }

    internal static bool ShouldSkipOverlapComparison(DrawingMarkInfo mark)
        => (mark.ResolvedGeometry?.Width ?? 0) < 0.1
           && (mark.ResolvedGeometry?.Height ?? 0) < 0.1;

    internal static MarkResolvedGeometryInfo CreateResolvedGeometryInfo(MarkGeometryInfo geometry)
    {
        return new MarkResolvedGeometryInfo
        {
            Source = geometry.Source,
            IsReliable = geometry.IsReliable,
            Width = Math.Round(geometry.Width, 2),
            Height = Math.Round(geometry.Height, 2),
            CenterX = Math.Round(geometry.CenterX, 2),
            CenterY = Math.Round(geometry.CenterY, 2),
            MinX = Math.Round(geometry.MinX, 2),
            MinY = Math.Round(geometry.MinY, 2),
            MaxX = Math.Round(geometry.MaxX, 2),
            MaxY = Math.Round(geometry.MaxY, 2),
            AngleDeg = Math.Round(geometry.AngleDeg, 2),
            AxisDx = Math.Round(geometry.AxisDx, 4),
            AxisDy = Math.Round(geometry.AxisDy, 4),
            Corners = geometry.Corners
                .Select(c => new[] { Math.Round(c[0], 2), Math.Round(c[1], 2) })
                .ToList()
        };
    }

    internal static MarkResolvedGeometryInfo CreateResolvedGeometryInfo(MarkGeometryContext geometry)
    {
        var axisDx = 0.0;
        var axisDy = 0.0;
        var angleDeg = 0.0;
        if (geometry.Corners.Count >= 2)
        {
            var dx = geometry.Corners[1].X - geometry.Corners[0].X;
            var dy = geometry.Corners[1].Y - geometry.Corners[0].Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length >= 0.001)
            {
                axisDx = dx / length;
                axisDy = dy / length;
                angleDeg = Math.Atan2(axisDy, axisDx) * (180.0 / Math.PI);
            }
        }

        return new MarkResolvedGeometryInfo
        {
            Source = geometry.Source,
            IsReliable = geometry.IsReliable,
            Width = Math.Round(geometry.Width, 2),
            Height = Math.Round(geometry.Height, 2),
            CenterX = Math.Round(geometry.Center?.X ?? 0.0, 2),
            CenterY = Math.Round(geometry.Center?.Y ?? 0.0, 2),
            MinX = Math.Round(geometry.Bounds?.MinX ?? 0.0, 2),
            MinY = Math.Round(geometry.Bounds?.MinY ?? 0.0, 2),
            MaxX = Math.Round(geometry.Bounds?.MaxX ?? 0.0, 2),
            MaxY = Math.Round(geometry.Bounds?.MaxY ?? 0.0, 2),
            AngleDeg = Math.Round(angleDeg, 2),
            AxisDx = Math.Round(axisDx, 4),
            AxisDy = Math.Round(axisDy, 4),
            Corners = geometry.Corners
                .Select(c => new[] { Math.Round(c.X, 2), Math.Round(c.Y, 2) })
                .ToList()
        };
    }

    internal static MarkAxisInfo CreateAxisInfo(MarkAxisContext axis)
    {
        return new MarkAxisInfo
        {
            StartX = Math.Round(axis.Start?.X ?? 0.0, 2),
            StartY = Math.Round(axis.Start?.Y ?? 0.0, 2),
            EndX = Math.Round(axis.End?.X ?? 0.0, 2),
            EndY = Math.Round(axis.End?.Y ?? 0.0, 2),
            Dx = Math.Round(axis.Direction?.X ?? 0.0, 4),
            Dy = Math.Round(axis.Direction?.Y ?? 0.0, 4),
            Length = Math.Round(axis.Length, 2),
            AngleDeg = Math.Round(axis.AngleDeg, 2),
            IsReliable = axis.IsReliable
        };
    }

    internal static DrawingMarkInfo CreateDrawingMarkInfo(
        int markId,
        double insertionX,
        double insertionY,
        double angle,
        MarkArrowheadInfo arrowHead,
        IReadOnlyList<MarkLeaderLineInfo> leaderLines,
        MarkContext markContext)
    {
        var resolvedGeometry = CreateResolvedGeometryInfo(markContext.Geometry);
        var info = new DrawingMarkInfo
        {
            Id = markId,
            ViewId = markContext.ViewId ?? 0,
            ModelId = markContext.ModelId,
            InsertionX = Math.Round(insertionX, 1),
            InsertionY = Math.Round(insertionY, 1),
            BboxMinX = resolvedGeometry.MinX,
            BboxMinY = resolvedGeometry.MinY,
            BboxMaxX = resolvedGeometry.MaxX,
            BboxMaxY = resolvedGeometry.MaxY,
            CenterX = resolvedGeometry.CenterX,
            CenterY = resolvedGeometry.CenterY,
            PlacingType = markContext.PlacingType,
            PlacingX = markContext.HasLeaderLine ? Math.Round(markContext.Anchor?.X ?? 0.0, 2) : 0,
            PlacingY = markContext.HasLeaderLine ? Math.Round(markContext.Anchor?.Y ?? 0.0, 2) : 0,
            Angle = Math.Round(angle, 2),
            RotationAngle = markContext.RotationAngle,
            TextAlignment = markContext.TextAlignment,
            ResolvedGeometry = resolvedGeometry,
            ArrowHead = arrowHead
        };

        if (markContext.Axis != null)
            info.Axis = CreateAxisInfo(markContext.Axis);

        info.Properties.AddRange(markContext.Properties.Select(static property => new MarkPropertyValue
        {
            Name = property.Name,
            Value = property.Value,
        }));
        info.LeaderLines.AddRange(leaderLines);
        return info;
    }

    internal static MarkArrowheadInfo CreateArrowHeadInfo(Mark mark)
    {
        return new MarkArrowheadInfo
        {
            Type = mark.Attributes.ArrowHead.Head.ToString(),
            Position = mark.Attributes.ArrowHead.ArrowPosition.ToString(),
            Height = Math.Round(mark.Attributes.ArrowHead.Height, 2),
            Width = Math.Round(mark.Attributes.ArrowHead.Width, 2)
        };
    }

    internal static List<MarkLeaderLineInfo> CreateLeaderLineInfos(Mark mark)
        => MarkLeaderLineReader.CreateInfos(mark);
}
