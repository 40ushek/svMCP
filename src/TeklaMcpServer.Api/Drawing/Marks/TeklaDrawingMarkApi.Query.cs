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
                var vid = view.GetIdentifier().ID;
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

                    contextsById.TryGetValue(markId, out var markContext);
                    var bbox = mark.GetAxisAlignedBoundingBox();
                    var obb = mark.GetObjectAlignedBoundingBox();
                    var ins = mark.InsertionPoint;
                    var leaderLinePlacing = mark.Placing as LeaderLinePlacing;
                    var info = new DrawingMarkInfo
                    {
                        Id = markId,
                        ViewId = markContext?.ViewId ?? vid,
                        InsertionX = Math.Round(ins.X, 1),
                        InsertionY = Math.Round(ins.Y, 1),
                        BboxMinX = Math.Round(bbox.MinPoint.X, 1),
                        BboxMinY = Math.Round(bbox.MinPoint.Y, 1),
                        BboxMaxX = Math.Round(bbox.MaxPoint.X, 1),
                        BboxMaxY = Math.Round(bbox.MaxPoint.Y, 1),
                        CenterX = Math.Round((bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0, 2),
                        CenterY = Math.Round((bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0, 2),
                        PlacingType = markContext?.PlacingType ?? mark.Placing?.GetType().Name ?? "null",
                        PlacingX = markContext?.HasLeaderLine == true
                            ? Math.Round(markContext.Anchor?.X ?? 0.0, 2)
                            : leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.X, 2) : 0,
                        PlacingY = markContext?.HasLeaderLine == true
                            ? Math.Round(markContext.Anchor?.Y ?? 0.0, 2)
                            : leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.Y, 2) : 0,
                        Angle = Math.Round(mark.Attributes.Angle, 2),
                        RotationAngle = markContext?.RotationAngle ?? Math.Round(mark.Attributes.RotationAngle, 2),
                        TextAlignment = markContext?.TextAlignment ?? mark.Attributes.TextAlignment.ToString(),
                        ObjectAlignedBoundingBox = CreateObjectAlignedBoundingBoxInfo(obb),
                        ResolvedGeometry = markContext?.Geometry != null
                            ? CreateResolvedGeometryInfo(markContext.Geometry)
                            : CreateResolvedGeometryInfo(MarkGeometryHelper.Build(mark, _model, vid)),
                        ArrowHead = CreateArrowHeadInfo(mark)
                    };

                    if (markContext?.Axis != null)
                    {
                        info.Axis = CreateAxisInfo(markContext.Axis);
                    }
                    else if (mark.Placing is BaseLinePlacing baseLinePlacing)
                    {
                        var axisDx = baseLinePlacing.EndPoint.X - baseLinePlacing.StartPoint.X;
                        var axisDy = baseLinePlacing.EndPoint.Y - baseLinePlacing.StartPoint.Y;
                        var axisLength = Math.Sqrt((axisDx * axisDx) + (axisDy * axisDy));
                        var normalizedDx = axisLength >= 0.001 ? axisDx / axisLength : 0.0;
                        var normalizedDy = axisLength >= 0.001 ? axisDy / axisLength : 0.0;

                        info.Axis = new MarkAxisInfo
                        {
                            StartX = Math.Round(baseLinePlacing.StartPoint.X, 2),
                            StartY = Math.Round(baseLinePlacing.StartPoint.Y, 2),
                            EndX = Math.Round(baseLinePlacing.EndPoint.X, 2),
                            EndY = Math.Round(baseLinePlacing.EndPoint.Y, 2),
                            Dx = Math.Round(normalizedDx, 4),
                            Dy = Math.Round(normalizedDy, 4),
                            Length = Math.Round(axisLength, 2),
                            AngleDeg = Math.Round(Math.Atan2(axisDy, axisDx) * (180.0 / Math.PI), 2),
                            IsReliable = axisLength >= 0.001
                        };
                    }

                    if (markContext?.ModelId != null)
                    {
                        info.ModelId = markContext.ModelId;
                    }
                    else
                    {
                        var related = mark.GetRelatedObjects();
                        while (related.MoveNext())
                        {
                            if (related.Current is Tekla.Structures.Drawing.ModelObject mo)
                            {
                                info.ModelId = mo.ModelIdentifier.ID;
                                break;
                            }
                        }
                    }

                    if (markContext?.Properties.Count > 0)
                    {
                        info.Properties.AddRange(markContext.Properties.Select(static property => new MarkPropertyValue
                        {
                            Name = property.Name,
                            Value = property.Value,
                        }));
                    }
                    else
                    {
                        var contentEnum = mark.Attributes.Content.GetEnumerator();
                        while (contentEnum.MoveNext())
                        {
                            if (contentEnum.Current is PropertyElement prop)
                                info.Properties.Add(new MarkPropertyValue { Name = prop.Name, Value = prop.Value });
                        }
                    }

                    var children = mark.GetObjects();
                    while (children.MoveNext())
                    {
                        if (children.Current is not LeaderLine leaderLine)
                            continue;

                        var leaderInfo = new MarkLeaderLineInfo
                        {
                            Type = leaderLine.LeaderLineType.ToString(),
                            StartX = Math.Round(leaderLine.StartPoint.X, 2),
                            StartY = Math.Round(leaderLine.StartPoint.Y, 2),
                            EndX = Math.Round(leaderLine.EndPoint.X, 2),
                            EndY = Math.Round(leaderLine.EndPoint.Y, 2)
                        };

                        foreach (Point elbowPoint in leaderLine.ElbowPoints)
                        {
                            leaderInfo.ElbowPoints.Add(new[]
                            {
                                Math.Round(elbowPoint.X, 2),
                                Math.Round(elbowPoint.Y, 2)
                            });
                        }

                        info.LeaderLines.Add(leaderInfo);
                    }

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
                        a.ResolvedGeometry?.MinX ?? a.BboxMinX,
                        a.ResolvedGeometry?.MinY ?? a.BboxMinY,
                        a.ResolvedGeometry?.MaxX ?? a.BboxMaxX,
                        a.ResolvedGeometry?.MaxY ?? a.BboxMaxY,
                        b.ResolvedGeometry?.MinX ?? b.BboxMinX,
                        b.ResolvedGeometry?.MinY ?? b.BboxMinY,
                        b.ResolvedGeometry?.MaxX ?? b.BboxMaxX,
                        b.ResolvedGeometry?.MaxY ?? b.BboxMaxY);

            if (overlapsDetected)
                overlaps.Add(new MarkOverlap { IdA = a.Id, IdB = b.Id });
        }

        return overlaps;
    }

    internal static bool ShouldSkipOverlapComparison(DrawingMarkInfo mark)
        => (mark.ResolvedGeometry?.Width ?? 0) < 0.1
           && (mark.ResolvedGeometry?.Height ?? 0) < 0.1;

    internal static MarkObjectAlignedBoundingBoxInfo CreateObjectAlignedBoundingBoxInfo(RectangleBoundingBox obb)
    {
        return new MarkObjectAlignedBoundingBoxInfo
        {
            Width = Math.Round(obb.Width, 2),
            Height = Math.Round(obb.Height, 2),
            AngleToAxis = Math.Round(obb.AngleToAxis, 2),
            CenterX = Math.Round((obb.MinPoint.X + obb.MaxPoint.X) / 2.0, 2),
            CenterY = Math.Round((obb.MinPoint.Y + obb.MaxPoint.Y) / 2.0, 2),
            MinX = Math.Round(obb.MinPoint.X, 2),
            MinY = Math.Round(obb.MinPoint.Y, 2),
            MaxX = Math.Round(obb.MaxPoint.X, 2),
            MaxY = Math.Round(obb.MaxPoint.Y, 2),
            Corners = new List<double[]>
            {
                new[] { Math.Round(obb.LowerLeft.X, 2), Math.Round(obb.LowerLeft.Y, 2) },
                new[] { Math.Round(obb.UpperLeft.X, 2), Math.Round(obb.UpperLeft.Y, 2) },
                new[] { Math.Round(obb.UpperRight.X, 2), Math.Round(obb.UpperRight.Y, 2) },
                new[] { Math.Round(obb.LowerRight.X, 2), Math.Round(obb.LowerRight.Y, 2) }
            }
        };
    }

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
}
