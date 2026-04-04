using System.Collections.Generic;
using System.Linq;
namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContextBuilder
{
    private const double InternalBandTolerance = 1.0;

    private readonly IDrawingPartPointApi _partPointApi;

    public DimensionContextBuilder(IDrawingPartPointApi partPointApi)
    {
        _partPointApi = partPointApi;
    }

    public DimensionContextBuildResult Build(IEnumerable<DimensionItem> items)
    {
        var result = new DimensionContextBuildResult();
        foreach (var item in items.Where(static item => item != null).Distinct())
        {
            result.Contexts.Add(Build(item));
        }

        return result;
    }

    public DimensionContext Build(DimensionItem item)
    {
        var context = new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item,
            ViewId = item.ViewId,
            ViewType = item.ViewType,
            ViewScale = item.ViewScale,
            SourceKind = item.SourceKind,
            Source = BuildSourceSummary(item),
            Geometry = BuildGeometry(item)
        };

        context.Role = ClassifyRole(context);
        return context;
    }

    private static DimensionContextSourceSummary BuildSourceSummary(DimensionItem item)
    {
        var source = new DimensionContextSourceSummary
        {
            SourceKind = item.SourceKind
        };

        foreach (var id in item.Dimension.SourceObjectIds.Distinct())
            source.SourceObjectIds.Add(id);

        return source;
    }

    private DimensionContextGeometry BuildGeometry(DimensionItem item)
    {
        var geometry = new DimensionContextGeometry
        {
            ReferenceLine = CopyLine(item.ReferenceLine),
            LeadLineMain = CopyLine(item.LeadLineMain),
            LeadLineSecond = CopyLine(item.LeadLineSecond),
            Distance = item.Distance
        };

        geometry.PointList.AddRange(item.PointList.Select(static point => new DrawingPointInfo
        {
            X = point.X,
            Y = point.Y,
            Order = point.Order
        }));
        geometry.LengthList.AddRange(item.LengthList);
        geometry.RealLengthList.AddRange(item.RealLengthList);
        geometry.LocalBounds = TryResolveLocalBounds(item, geometry.Warnings);
        return geometry;
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        return line == null
            ? null
            : TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private DrawingBoundsInfo? TryResolveLocalBounds(DimensionItem item, List<string> warnings)
    {
        if (item.SourceKind != DimensionSourceKind.Part)
            return null;

        if (!item.ViewId.HasValue)
        {
            warnings.Add("view_unavailable");
            return null;
        }

        if (item.Dimension.SourceObjectIds.Count == 0)
        {
            warnings.Add("source_geometry_unavailable");
            return null;
        }

        var boundsList = new List<DrawingBoundsInfo>();
        foreach (var modelId in item.Dimension.SourceObjectIds.Distinct())
        {
            GetPartPointsResult partPoints;
            try
            {
                partPoints = _partPointApi.GetPartPointsInView(item.ViewId.Value, modelId);
            }
            catch
            {
                warnings.Add($"part_points_failed:{modelId}");
                continue;
            }

            if (!partPoints.Success || partPoints.Points.Count == 0)
            {
                warnings.Add($"part_points_unavailable:{modelId}");
                continue;
            }

            var bounds = TryCreateBounds(partPoints.Points.Select(static point => point.Point));
            if (bounds == null)
            {
                warnings.Add($"part_points_empty:{modelId}");
                continue;
            }

            boundsList.Add(bounds);
        }

        if (boundsList.Count == 0)
        {
            if (warnings.Count == 0)
                warnings.Add("source_geometry_unavailable");
            return null;
        }

        return TeklaDrawingDimensionsApi.CombineBounds(boundsList);
    }

    private static DrawingBoundsInfo? TryCreateBounds(IEnumerable<double[]> points)
    {
        var projected = points
            .Where(static point => point.Length >= 2)
            .Select(static point => (X: point[0], Y: point[1]))
            .ToList();
        if (projected.Count == 0)
            return null;

        return TeklaDrawingDimensionsApi.CreateBoundsInfo(
            projected.Min(static point => point.X),
            projected.Min(static point => point.Y),
            projected.Max(static point => point.X),
            projected.Max(static point => point.Y));
    }

    private static DimensionContextRole ClassifyRole(DimensionContext context)
    {
        if (context.Item.DomainDimensionType == DimensionType.Free ||
            context.Item.GeometryKind == DimensionGeometryKind.Free)
        {
            return DimensionContextRole.Control;
        }

        if (context.SourceKind == DimensionSourceKind.Grid)
            return DimensionContextRole.Grid;

        if (context.SourceKind == DimensionSourceKind.Part && !context.HasSourceGeometry)
            return DimensionContextRole.NoSourceGeometry;

        if (context.SourceKind != DimensionSourceKind.Part ||
            context.ReferenceLine == null ||
            context.LocalBounds == null ||
            !context.Item.Direction.HasValue ||
            context.Item.TopDirection == 0)
        {
            return DimensionContextRole.Unknown;
        }

        var direction = context.Item.Direction.Value;
        var sideNormalX = -direction.Y * context.Item.TopDirection;
        var sideNormalY = direction.X * context.Item.TopDirection;
        var referenceOffset = Project(context.ReferenceLine.StartX, context.ReferenceLine.StartY, sideNormalX, sideNormalY);
        var boundsExtents = ProjectBounds(context.LocalBounds, sideNormalX, sideNormalY);

        if (referenceOffset < boundsExtents.Min - InternalBandTolerance ||
            referenceOffset > boundsExtents.Max + InternalBandTolerance)
        {
            return DimensionContextRole.External;
        }

        return DimensionContextRole.Internal;
    }

    private static (double Min, double Max) ProjectBounds(DrawingBoundsInfo bounds, double axisX, double axisY)
    {
        var values = new[]
        {
            Project(bounds.MinX, bounds.MinY, axisX, axisY),
            Project(bounds.MinX, bounds.MaxY, axisX, axisY),
            Project(bounds.MaxX, bounds.MinY, axisX, axisY),
            Project(bounds.MaxX, bounds.MaxY, axisX, axisY)
        };

        return (values.Min(), values.Max());
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);
}
