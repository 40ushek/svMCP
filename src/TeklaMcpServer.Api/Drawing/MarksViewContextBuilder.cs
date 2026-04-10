using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class MarksViewContextBuilder
{
    private const double AxisEpsilon = 0.001;
    private const double GeometryEpsilon = 0.1;

    public MarksViewContext Build(View view, Model model)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var viewId = view.GetIdentifier().ID;
        var viewScale = ResolveViewScale(view);
        var context = new MarksViewContext
        {
            ViewId = viewId,
            ViewScale = viewScale,
            ViewBounds = CreateViewBounds(view.Width, view.Height),
        };

        var seenIds = new HashSet<int>();
        var markObjects = view.GetAllObjects(typeof(Mark));
        while (markObjects.MoveNext())
        {
            if (markObjects.Current is not Mark mark)
                continue;

            var markId = mark.GetIdentifier().ID;
            if (!seenIds.Add(markId))
                continue;

            try
            {
                var item = BuildMarkContext(mark, model, viewId, viewScale);
                if (item == null)
                {
                    context.Warnings.Add($"mark:{markId}:degenerate_geometry");
                    continue;
                }

                context.Marks.Add(item);
            }
            catch (Exception ex)
            {
                context.Warnings.Add($"mark:{markId}:{NormalizeWarningReason(ex)}");
            }
        }

        return context;
    }

    internal static DrawingBoundsInfo CreateViewBounds(double width, double height)
    {
        var halfWidth = width * 0.5;
        var halfHeight = height * 0.5;
        return TeklaDrawingDimensionsApi.CreateBoundsInfo(-halfWidth, -halfHeight, halfWidth, halfHeight);
    }

    internal static MarkGeometryContext CreateGeometryContext(MarkGeometryInfo geometry)
    {
        if (geometry == null)
            throw new ArgumentNullException(nameof(geometry));

        var context = new MarkGeometryContext
        {
            Bounds = TeklaDrawingDimensionsApi.CreateBoundsInfo(geometry.MinX, geometry.MinY, geometry.MaxX, geometry.MaxY),
            Center = CreatePoint(geometry.CenterX, geometry.CenterY),
            Width = geometry.Width,
            Height = geometry.Height,
            Source = geometry.Source ?? string.Empty,
            IsReliable = geometry.IsReliable,
        };

        context.Corners.AddRange(geometry.Corners.Select(static (corner, index) => new DrawingPointInfo
        {
            X = corner[0],
            Y = corner[1],
            Order = index,
        }));

        return context;
    }

    internal static DrawingPointInfo CreateAnchor(MarkGeometryContext geometry, bool hasLeaderLine, double leaderAnchorX, double leaderAnchorY)
    {
        if (geometry == null)
            throw new ArgumentNullException(nameof(geometry));

        return hasLeaderLine
            ? CreatePoint(leaderAnchorX, leaderAnchorY)
            : CreatePoint(geometry.Center?.X ?? 0.0, geometry.Center?.Y ?? 0.0);
    }

    internal static MarkAxisContext? CreateAxisContext(MarkGeometryInfo geometry, double startX, double startY, double endX, double endY)
    {
        if (geometry == null)
            throw new ArgumentNullException(nameof(geometry));

        var resolved = CreateAxisContextFromGeometry(geometry);
        return resolved ?? CreateAxisContextFromLine(startX, startY, endX, endY);
    }

    internal static MarkAxisContext? CreateAxisContextFromGeometry(MarkGeometryInfo geometry)
    {
        if (geometry == null)
            throw new ArgumentNullException(nameof(geometry));

        if (!geometry.HasAxis || !TryNormalize(geometry.AxisDx, geometry.AxisDy, out var axisDx, out var axisDy, out var angleDeg))
            return null;

        var length = Math.Abs(geometry.Width);
        var halfLength = length * 0.5;
        var start = CreatePoint(geometry.CenterX - (axisDx * halfLength), geometry.CenterY - (axisDy * halfLength), 0);
        var end = CreatePoint(geometry.CenterX + (axisDx * halfLength), geometry.CenterY + (axisDy * halfLength), 1);

        return new MarkAxisContext
        {
            Start = start,
            End = end,
            Direction = new DrawingVectorInfo { X = axisDx, Y = axisDy },
            Length = length,
            AngleDeg = angleDeg,
            IsReliable = geometry.IsReliable && length >= AxisEpsilon,
        };
    }

    internal static MarkAxisContext CreateAxisContextFromLine(double startX, double startY, double endX, double endY)
    {
        var start = CreatePoint(startX, startY, 0);
        var end = CreatePoint(endX, endY, 1);
        var dx = endX - startX;
        var dy = endY - startY;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        return new MarkAxisContext
        {
            Start = start,
            End = end,
            Direction = length >= AxisEpsilon
                ? new DrawingVectorInfo { X = dx / length, Y = dy / length }
                : null,
            Length = length,
            AngleDeg = length >= AxisEpsilon ? Math.Atan2(dy, dx) * (180.0 / Math.PI) : 0.0,
            IsReliable = length >= AxisEpsilon,
        };
    }

    internal static double ResolveViewScale(View view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        if (view.Attributes?.Scale > 0)
            return view.Attributes.Scale;

        return 1.0;
    }

    private static MarkContext? BuildMarkContext(Mark mark, Model model, int viewId, double viewScale)
    {
        var geometryInfo = MarkGeometryResolver.Build(mark, model, viewId);
        if (geometryInfo.Width < GeometryEpsilon && geometryInfo.Height < GeometryEpsilon)
            return null;

        var geometry = CreateGeometryContext(geometryInfo);
        var leaderLinePlacing = mark.Placing as LeaderLinePlacing;
        var hasLeaderLine = leaderLinePlacing != null;
        var anchor = leaderLinePlacing != null
            ? CreateAnchor(geometry, true, leaderLinePlacing.StartPoint.X, leaderLinePlacing.StartPoint.Y)
            : CreateAnchor(geometry, false, 0.0, 0.0);

        var context = new MarkContext
        {
            MarkId = mark.GetIdentifier().ID,
            ModelId = TryResolveModelId(mark),
            ViewId = viewId,
            ViewScale = viewScale,
            PlacingType = mark.Placing?.GetType().Name ?? "null",
            TextAlignment = mark.Attributes.TextAlignment.ToString(),
            RotationAngle = Math.Round(mark.Attributes.RotationAngle, 2),
            Anchor = anchor,
            CurrentCenter = geometry.Center == null ? null : CreatePoint(geometry.Center.X, geometry.Center.Y),
            Geometry = geometry,
            HasLeaderLine = hasLeaderLine,
            CanMove = true,
        };

        if (mark.Placing is BaseLinePlacing baseLinePlacing)
        {
            context.Axis = CreateAxisContext(
                geometryInfo,
                baseLinePlacing.StartPoint.X,
                baseLinePlacing.StartPoint.Y,
                baseLinePlacing.EndPoint.X,
                baseLinePlacing.EndPoint.Y);
        }

        var content = mark.Attributes.Content.GetEnumerator();
        while (content.MoveNext())
        {
            if (content.Current is not PropertyElement property)
                continue;

            context.Properties.Add(new MarkContextProperty
            {
                Name = property.Name,
                Value = property.Value,
            });
        }

        return context;
    }

    private static int? TryResolveModelId(Mark mark)
    {
        var related = mark.GetRelatedObjects();
        while (related.MoveNext())
        {
            if (related.Current is Tekla.Structures.Drawing.ModelObject drawingModelObject)
                return drawingModelObject.ModelIdentifier.ID;
        }

        return null;
    }

    private static DrawingPointInfo CreatePoint(double x, double y, int order = -1) => new()
    {
        X = x,
        Y = y,
        Order = order,
    };

    private static bool TryNormalize(double x, double y, out double normalizedX, out double normalizedY, out double angleDeg)
    {
        var length = Math.Sqrt((x * x) + (y * y));
        if (length < AxisEpsilon)
        {
            normalizedX = 0.0;
            normalizedY = 0.0;
            angleDeg = 0.0;
            return false;
        }

        normalizedX = x / length;
        normalizedY = y / length;
        angleDeg = Math.Atan2(normalizedY, normalizedX) * (180.0 / Math.PI);
        return true;
    }

    private static string NormalizeWarningReason(Exception ex)
    {
        var typeName = ex.GetType().Name;
        return string.IsNullOrWhiteSpace(typeName) ? "build_failed" : typeName;
    }
}
