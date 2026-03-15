using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

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

            foreach (var view in viewsToQuery)
            {
                var vid = view.GetIdentifier().ID;
                var markObjects = view.GetAllObjects(typeof(Mark));

                while (markObjects.MoveNext())
                {
                    if (markObjects.Current is not Mark mark)
                        continue;

                    var markId = mark.GetIdentifier().ID;
                    if (!seenIds.Add(markId))
                        continue;

                    var bbox = mark.GetAxisAlignedBoundingBox();
                    var obb = mark.GetObjectAlignedBoundingBox();
                    var geometry = MarkGeometryHelper.Build(mark, _model, vid);
                    var ins = mark.InsertionPoint;
                    var leaderLinePlacing = mark.Placing as LeaderLinePlacing;
                    var info = new DrawingMarkInfo
                    {
                        Id = markId,
                        ViewId = vid,
                        InsertionX = Math.Round(ins.X, 1),
                        InsertionY = Math.Round(ins.Y, 1),
                        BboxMinX = Math.Round(bbox.MinPoint.X, 1),
                        BboxMinY = Math.Round(bbox.MinPoint.Y, 1),
                        BboxMaxX = Math.Round(bbox.MaxPoint.X, 1),
                        BboxMaxY = Math.Round(bbox.MaxPoint.Y, 1),
                        CenterX = Math.Round((bbox.MinPoint.X + bbox.MaxPoint.X) / 2.0, 2),
                        CenterY = Math.Round((bbox.MinPoint.Y + bbox.MaxPoint.Y) / 2.0, 2),
                        PlacingType = mark.Placing?.GetType().Name ?? "null",
                        PlacingX = leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.X, 2) : 0,
                        PlacingY = leaderLinePlacing != null ? Math.Round(leaderLinePlacing.StartPoint.Y, 2) : 0,
                        Angle = Math.Round(mark.Attributes.Angle, 2),
                        RotationAngle = Math.Round(mark.Attributes.RotationAngle, 2),
                        TextAlignment = mark.Attributes.TextAlignment.ToString(),
                        ObjectAlignedBoundingBox = new MarkObjectAlignedBoundingBoxInfo
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
                        },
                        ResolvedGeometry = new MarkResolvedGeometryInfo
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
                        },
                        ArrowHead = new MarkArrowheadInfo
                        {
                            Type = mark.Attributes.ArrowHead.Head.ToString(),
                            Position = mark.Attributes.ArrowHead.ArrowPosition.ToString(),
                            Height = Math.Round(mark.Attributes.ArrowHead.Height, 2),
                            Width = Math.Round(mark.Attributes.ArrowHead.Width, 2)
                        }
                    };

                    if (mark.Placing is BaseLinePlacing baseLinePlacing)
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

                    var related = mark.GetRelatedObjects();
                    while (related.MoveNext())
                    {
                        if (related.Current is Tekla.Structures.Drawing.ModelObject mo)
                        {
                            info.ModelId = mo.ModelIdentifier.ID;
                            break;
                        }
                    }

                    var contentEnum = mark.Attributes.Content.GetEnumerator();
                    while (contentEnum.MoveNext())
                    {
                        if (contentEnum.Current is PropertyElement prop)
                            info.Properties.Add(new MarkPropertyValue { Name = prop.Name, Value = prop.Value });
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

            var overlaps = new List<MarkOverlap>();
            for (int i = 0; i < marks.Count; i++)
            for (int j = i + 1; j < marks.Count; j++)
            {
                var a = marks[i];
                var b = marks[j];
                var overlapsDetected =
                    a.ResolvedGeometry?.Corners is { Count: >= 3 } aCorners &&
                    b.ResolvedGeometry?.Corners is { Count: >= 3 } bCorners
                        ? MarkGeometryHelper.PolygonsIntersect(aCorners, bCorners)
                        : MarkGeometryHelper.RectanglesOverlap(
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

            return new GetMarksResult { Total = marks.Count, Marks = marks, Overlaps = overlaps };
        }
        finally
        {
            DrawingEnumeratorBase.AutoFetch = previousAutoFetch;
        }
    }
}
