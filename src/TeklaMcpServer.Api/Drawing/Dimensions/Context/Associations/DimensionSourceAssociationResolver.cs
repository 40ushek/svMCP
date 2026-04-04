using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionSourceAssociationResult
{
    public List<DrawingPointInfo> MeasuredPoints { get; } = [];
    public List<DimensionSourceCandidateInfo> Candidates { get; } = [];
    public List<DimensionPointObjectMapping> PointMappings { get; } = [];
}

internal sealed class DimensionSourceAssociationResolver
{
    private readonly Model _model;
    private readonly IDrawingPartPointApi _partPointApi;
    private readonly DimensionPointObjectMapper _mapper = new();

    public DimensionSourceAssociationResolver(Model model, IDrawingPartPointApi partPointApi)
    {
        _model = model;
        _partPointApi = partPointApi;
    }

    public DimensionSourceAssociationResult Resolve(StraightDimensionSet dimSet, DrawingDimensionInfo dimensionInfo)
    {
        var result = new DimensionSourceAssociationResult();
        result.MeasuredPoints.AddRange(dimensionInfo.MeasuredPoints.Select(static point => new DrawingPointInfo
        {
            X = point.X,
            Y = point.Y,
            Order = point.Order
        }));

        var ownerViewId = dimensionInfo.ViewId;
        CollectDimensionSourceCandidates(result.Candidates, dimSet.GetRelatedObjects(), "dimensionSet", ownerViewId);
        var actualSegments = EnumerateSegments(dimSet);
        for (var i = 0; i < actualSegments.Count; i++)
        {
            var segmentId = i < dimensionInfo.Segments.Count ? dimensionInfo.Segments[i].Id : 0;
            var owner = segmentId > 0 ? $"segment:{segmentId}" : $"segmentIndex:{i}";
            CollectDimensionSourceCandidates(result.Candidates, actualSegments[i].GetRelatedObjects(), owner, ownerViewId);
        }

        result.PointMappings.AddRange(_mapper.Map(
            result.MeasuredPoints,
            result.Candidates,
            BuildPreferredOwnersByPointOrder(result.MeasuredPoints, dimensionInfo.Segments)));

        return result;
    }

    private void CollectDimensionSourceCandidates(
        List<DimensionSourceCandidateInfo> target,
        DrawingObjectEnumerator? relatedObjects,
        string owner,
        int? ownerViewId)
    {
        if (relatedObjects == null)
            return;

        while (relatedObjects.MoveNext())
        {
            var current = relatedObjects.Current;
            var candidate = new DimensionSourceCandidateInfo
            {
                Owner = owner,
                Type = current?.GetType().Name ?? string.Empty,
                SourceKind = ResolveRelatedObjectSourceKind(current).ToString()
            };

            if (DimensionRelatedObjectHelper.TryGetRelatedObjectId(current, out var sourceObjectId))
                candidate.DrawingObjectId = sourceObjectId;

            if (current is Tekla.Structures.Drawing.ModelObject drawingModelObject)
            {
                candidate.ModelId = drawingModelObject.ModelIdentifier.ID;
                candidate.ResolvedModelType = TrySelectRelatedModelObject(drawingModelObject)?.GetType().Name ?? string.Empty;
            }

            PopulateCandidateGeometry(candidate, ownerViewId);
            target.Add(candidate);
        }
    }

    private void PopulateCandidateGeometry(DimensionSourceCandidateInfo candidate, int? ownerViewId)
    {
        if (!ownerViewId.HasValue)
        {
            candidate.GeometryWarnings.Add("view_unavailable");
            return;
        }

        if (!candidate.ModelId.HasValue || candidate.ModelId.Value <= 0)
        {
            candidate.GeometryWarnings.Add("model_id_unavailable");
            return;
        }

        if (!string.Equals(candidate.SourceKind, DimensionSourceKind.Part.ToString(), System.StringComparison.Ordinal))
        {
            candidate.GeometryWarnings.Add("geometry_probe_not_supported_for_source_kind");
            return;
        }

        candidate.GeometrySource = "part_points";

        GetPartPointsResult partPoints;
        try
        {
            partPoints = _partPointApi.GetPartPointsInView(ownerViewId.Value, candidate.ModelId.Value);
        }
        catch
        {
            candidate.GeometryWarnings.Add("part_points_failed");
            return;
        }

        if (!partPoints.Success)
        {
            candidate.GeometryWarnings.Add(partPoints.Error ?? "part_points_unavailable");
            return;
        }

        foreach (var point in partPoints.Points.Where(static point => point.Point.Length >= 2))
        {
            candidate.GeometryPoints.Add(new DrawingPointInfo
            {
                X = System.Math.Round(point.Point[0], 3),
                Y = System.Math.Round(point.Point[1], 3),
                Order = point.Index
            });
        }

        candidate.GeometryPointCount = candidate.GeometryPoints.Count;
        if (candidate.GeometryPoints.Count == 0)
        {
            candidate.GeometryWarnings.Add("geometry_points_empty");
            return;
        }

        candidate.HasGeometry = true;
        candidate.GeometryBounds = TeklaDrawingDimensionsApi.CreateBoundsInfo(
            candidate.GeometryPoints.Min(static point => point.X),
            candidate.GeometryPoints.Min(static point => point.Y),
            candidate.GeometryPoints.Max(static point => point.X),
            candidate.GeometryPoints.Max(static point => point.Y));
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> BuildPreferredOwnersByPointOrder(
        IReadOnlyList<DrawingPointInfo> measuredPoints,
        IReadOnlyList<DimensionSegmentInfo> segments)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var point in measuredPoints)
        {
            var owners = segments
                .Where(segment => PointMatchesSegmentEndpoint(point, segment))
                .Select(static segment => $"segment:{segment.Id}")
                .Distinct()
                .OrderBy(static owner => owner)
                .ToList();
            if (owners.Count > 0)
                result[point.Order] = owners;
        }

        return result;
    }

    private static bool PointMatchesSegmentEndpoint(DrawingPointInfo point, DimensionSegmentInfo segment, double tolerance = 0.5)
    {
        return MatchesPoint(point, segment.StartX, segment.StartY, tolerance)
            || MatchesPoint(point, segment.EndX, segment.EndY, tolerance);
    }

    private static bool MatchesPoint(DrawingPointInfo point, double x, double y, double tolerance)
    {
        return System.Math.Abs(point.X - x) <= tolerance
               && System.Math.Abs(point.Y - y) <= tolerance;
    }

    private DimensionSourceKind ResolveRelatedObjectSourceKind(object? relatedObject)
    {
        if (relatedObject == null)
            return DimensionSourceKind.Unknown;

        if (relatedObject is GridLine)
            return DimensionSourceKind.Grid;

        if (relatedObject is Tekla.Structures.Drawing.Part)
            return DimensionSourceKind.Part;

        if (relatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject)
        {
            var modelObject = TrySelectRelatedModelObject(drawingModelObject);
            if (modelObject is Tekla.Structures.Model.Part)
                return DimensionSourceKind.Part;

            if (modelObject != null && modelObject.GetType().Name.IndexOf("Grid", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return DimensionSourceKind.Grid;
        }

        var typeName = relatedObject.GetType().Name;
        if (typeName.IndexOf("Grid", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return DimensionSourceKind.Grid;
        if (typeName.IndexOf("Part", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return DimensionSourceKind.Part;

        return DimensionSourceKind.Unknown;
    }

    private Tekla.Structures.Model.ModelObject? TrySelectRelatedModelObject(Tekla.Structures.Drawing.ModelObject drawingModelObject)
    {
        try
        {
            return _model.SelectModelObject(drawingModelObject.ModelIdentifier);
        }
        catch
        {
            return null;
        }
    }

    private static List<StraightDimension> EnumerateSegments(StraightDimensionSet dimSet)
    {
        var segments = new List<StraightDimension>();
        var segEnum = dimSet.GetObjects();
        while (segEnum.MoveNext())
        {
            if (segEnum.Current is StraightDimension segment)
                segments.Add(segment);
        }

        return segments;
    }
}
