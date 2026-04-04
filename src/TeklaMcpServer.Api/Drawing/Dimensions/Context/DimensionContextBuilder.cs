using System.Collections.Generic;
using System.Linq;
namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContextBuilder
{
    private const double InternalBandTolerance = 1.0;

    private readonly DimensionSourceAssociationResolver _associationResolver;

    public DimensionContextBuilder(DimensionSourceAssociationResolver associationResolver)
    {
        _associationResolver = associationResolver;
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
        var association = _associationResolver.Resolve(item.Dimension);
        var context = new DimensionContext
        {
            DimensionId = item.DimensionId,
            Item = item,
            ViewId = item.ViewId,
            ViewType = item.ViewType,
            ViewScale = item.ViewScale,
            SourceKind = item.SourceKind,
            Source = BuildSourceSummary(item, association),
            Geometry = BuildGeometry(item, association),
            Association = BuildAssociation(association)
        };

        context.Role = ClassifyRole(context);
        return context;
    }

    private static DimensionContextSourceSummary BuildSourceSummary(DimensionItem item, DimensionSourceAssociationResult association)
    {
        var source = new DimensionContextSourceSummary
        {
            SourceKind = item.SourceKind
        };

        foreach (var id in item.Dimension.SourceObjectIds
                     .Concat(association.Candidates.Where(static candidate => candidate.ModelId.HasValue).Select(static candidate => candidate.ModelId!.Value))
                     .Concat(association.Candidates.Where(static candidate => candidate.DrawingObjectId.HasValue).Select(static candidate => candidate.DrawingObjectId!.Value))
                     .Distinct()
                     .OrderBy(static id => id))
            source.SourceObjectIds.Add(id);

        return source;
    }

    private DimensionContextGeometry BuildGeometry(DimensionItem item, DimensionSourceAssociationResult association)
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
        geometry.LocalBounds = TryResolveLocalBounds(association, geometry.Warnings);
        return geometry;
    }

    private static DimensionContextSourceAssociation BuildAssociation(DimensionSourceAssociationResult association)
    {
        var result = new DimensionContextSourceAssociation();
        result.MeasuredPoints.AddRange(association.MeasuredPoints.Select(static point => new DrawingPointInfo
        {
            X = point.X,
            Y = point.Y,
            Order = point.Order
        }));
        result.RelatedSources.AddRange(association.Candidates.Select(static candidate => new DimensionContextRelatedSource
        {
            Owner = candidate.Owner,
            DrawingObjectId = candidate.DrawingObjectId,
            ModelId = candidate.ModelId,
            Type = candidate.Type,
            SourceKind = candidate.SourceKind,
            HasGeometry = candidate.HasGeometry,
            GeometryBounds = candidate.GeometryBounds == null
                ? null
                : TeklaDrawingDimensionsApi.CreateBoundsInfo(
                    candidate.GeometryBounds.MinX,
                    candidate.GeometryBounds.MinY,
                    candidate.GeometryBounds.MaxX,
                    candidate.GeometryBounds.MaxY)
        }));
        result.PointAssociations.AddRange(association.PointMappings.Select(static mapping => new DimensionContextPointAssociation
        {
            Order = mapping.Point.Order,
            Status = mapping.Status,
            MatchedOwner = mapping.MatchedCandidate?.Owner ?? string.Empty,
            MatchedDrawingObjectId = mapping.MatchedCandidate?.DrawingObjectId,
            MatchedModelId = mapping.MatchedCandidate?.ModelId,
            MatchedType = mapping.MatchedCandidate?.Type ?? string.Empty,
            MatchedSourceKind = mapping.MatchedCandidate?.SourceKind ?? string.Empty,
            DistanceToGeometry = mapping.DistanceToGeometry
        }));

        foreach (var warning in association.Warnings.Distinct())
            result.Warnings.Add(warning);

        foreach (var warning in association.Candidates
                     .SelectMany(static candidate => candidate.GeometryWarnings)
                     .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                     .Distinct())
        {
            result.Warnings.Add(warning);
        }

        return result;
    }

    private static DrawingLineInfo? CopyLine(DrawingLineInfo? line)
    {
        return line == null
            ? null
            : TeklaDrawingDimensionsApi.CreateLineInfo(line.StartX, line.StartY, line.EndX, line.EndY);
    }

    private static DrawingBoundsInfo? TryResolveLocalBounds(DimensionSourceAssociationResult association, List<string> warnings)
    {
        var candidateGroups = association.Candidates
            .Where(static candidate => candidate.HasGeometry && candidate.GeometryBounds != null)
            .GroupBy(static candidate => new
            {
                candidate.Owner,
                candidate.DrawingObjectId,
                candidate.ModelId,
                candidate.Type,
                candidate.SourceKind
            })
            .Select(static group => group.First().GeometryBounds!)
            .ToList();

        if (candidateGroups.Count == 0)
        {
            if (warnings.Count == 0)
                warnings.Add("source_geometry_unavailable");
            return null;
        }

        if (association.Candidates.Any(static candidate => !candidate.HasGeometry))
            warnings.Add("source_geometry_partial");

        return TeklaDrawingDimensionsApi.CombineBounds(candidateGroups);
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
