using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Geometry;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingDimensionsApi : IDrawingDimensionsApi
{
    private const double AxisSummaryToleranceRatio = 0.10;
    private static readonly IReadOnlyDictionary<FrameTypes, (double WidthFactor, double HeightFactor)> FrameSizeCoefficientsBasedOnHeightAndWidth =
        new Dictionary<FrameTypes, (double WidthFactor, double HeightFactor)>
        {
            [FrameTypes.Circle] = (1.15, 1.15),
            [FrameTypes.Hexagon] = (1.28, 1.25),
            [FrameTypes.Diamond] = (1.55, 1.55),
            [FrameTypes.Triangle] = (2.28, 1.47)
        };

    private static readonly IReadOnlyDictionary<FrameTypes, (double WidthFactor, double HeightFactor)> FrameSizeCoefficientsBasedOnWidth =
        new Dictionary<FrameTypes, (double WidthFactor, double HeightFactor)>
        {
            [FrameTypes.Line] = (0.6, 1.4),
            [FrameTypes.Rectangular] = (0.9, 1.55),
            [FrameTypes.Round] = (1.75, 1.55),
            [FrameTypes.Sharpened] = (1.75, 1.55)
        };

    private readonly struct DimensionLineContext
    {
        public DimensionLineContext(
            (double X, double Y) upDirection,
            (double X, double Y) direction,
            int topDirection,
            DrawingLineInfo referenceLine)
        {
            UpDirection = upDirection;
            Direction = direction;
            TopDirection = topDirection;
            ReferenceLine = referenceLine;
        }

        public (double X, double Y) UpDirection { get; }
        public (double X, double Y) Direction { get; }
        public int TopDirection { get; }
        public DrawingLineInfo ReferenceLine { get; }
    }

    private readonly Model _model;

    public TeklaDrawingDimensionsApi()
        : this(new Model())
    {
    }

    internal TeklaDrawingDimensionsApi(Model model)
    {
        _model = model ?? new Model();
    }

    internal static DrawingBoundsInfo CreateBoundsInfo(double minX, double minY, double maxX, double maxY) => new()
    {
        MinX = System.Math.Round(minX, 3),
        MinY = System.Math.Round(minY, 3),
        MaxX = System.Math.Round(maxX, 3),
        MaxY = System.Math.Round(maxY, 3)
    };

    internal static DrawingLineInfo CreateLineInfo(double startX, double startY, double endX, double endY) => new()
    {
        StartX = System.Math.Round(startX, 3),
        StartY = System.Math.Round(startY, 3),
        EndX = System.Math.Round(endX, 3),
        EndY = System.Math.Round(endY, 3)
    };

    internal static DrawingBoundsInfo? CombineBounds(IEnumerable<DrawingBoundsInfo?> bounds)
    {
        var present = bounds.Where(static b => b != null).Cast<DrawingBoundsInfo>().ToList();
        if (present.Count == 0)
            return null;

        return CreateBoundsInfo(
            present.Min(static b => b.MinX),
            present.Min(static b => b.MinY),
            present.Max(static b => b.MaxX),
            present.Max(static b => b.MaxY));
    }

    internal static string DetermineDimensionOrientation(
        double directionX,
        double directionY,
        DrawingLineInfo? referenceLine,
        IReadOnlyList<DimensionSegmentInfo> segments)
    {
        if (referenceLine != null)
            return DetermineLineOrientation(referenceLine.StartX, referenceLine.StartY, referenceLine.EndX, referenceLine.EndY);

        if (TryNormalizeDirection(directionX, directionY, out var normalizedDirection))
            return DetermineDirectionOrientation(normalizedDirection.X, normalizedDirection.Y);

        return DetermineDimensionOrientation(segments);
    }

    internal static string DetermineDimensionOrientation(IReadOnlyList<DimensionSegmentInfo> segments)
    {
        var hasHorizontal = false;
        var hasVertical = false;
        var hasAngled = false;

        foreach (var segment in segments)
        {
            var dx = System.Math.Abs(segment.EndX - segment.StartX);
            var dy = System.Math.Abs(segment.EndY - segment.StartY);
            if (dx <= 1e-6 && dy <= 1e-6)
                continue;

            if (dy <= dx * AxisSummaryToleranceRatio)
            {
                hasHorizontal = true;
                continue;
            }

            if (dx <= dy * AxisSummaryToleranceRatio)
            {
                hasVertical = true;
                continue;
            }

            hasAngled = true;
        }

        if (hasAngled || (hasHorizontal && hasVertical))
            return "angled";

        if (hasHorizontal)
            return "horizontal";

        if (hasVertical)
            return "vertical";

        return string.Empty;
    }

    private static string DetermineLineOrientation(double startX, double startY, double endX, double endY)
    {
        if (!TryNormalizeDirection(endX - startX, endY - startY, out var direction))
            return string.Empty;

        return DetermineDirectionOrientation(direction.X, direction.Y);
    }

    private static string DetermineDirectionOrientation(double directionX, double directionY)
    {
        var dx = System.Math.Abs(directionX);
        var dy = System.Math.Abs(directionY);

        if (dy <= dx * AxisSummaryToleranceRatio)
            return "horizontal";

        if (dx <= dy * AxisSummaryToleranceRatio)
            return "vertical";

        return "angled";
    }

    private static DrawingBoundsInfo? TryGetBounds(DrawingObject drawingObject)
    {
        if (drawingObject is not IAxisAlignedBoundingBox bounded)
            return null;

        var box = bounded.GetAxisAlignedBoundingBox();
        if (box == null)
            return null;

        return CreateBoundsInfo(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
    }

    private static DrawingBoundsInfo CreateBoundsFromSegmentPoints(Point start, Point end) => CreateBoundsInfo(
        System.Math.Min(start.X, end.X),
        System.Math.Min(start.Y, end.Y),
        System.Math.Max(start.X, end.X),
        System.Math.Max(start.Y, end.Y));

    internal static DrawingBoundsInfo CreateBoundsFromLine(DrawingLineInfo line) => CreateBoundsInfo(
        System.Math.Min(line.StartX, line.EndX),
        System.Math.Min(line.StartY, line.EndY),
        System.Math.Max(line.StartX, line.EndX),
        System.Math.Max(line.StartY, line.EndY));

    private static DrawingBoundsInfo? TryGetTextBounds(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        DrawingLineInfo? dimensionLine)
    {
        var polygon = TryCreateTextPolygon(segment, dimSet, dimensionLine);
        if (polygon == null || polygon.Count == 0)
            return null;

        return CreateBoundsFromPolygon(polygon);
    }

    internal static string? TryGetMeasuredValueText(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        View view)
    {
        var formattedValue = DimensionTextValueFormatter.TryFormatMeasuredValue(segment, dimSet, view);
        if (!string.IsNullOrWhiteSpace(formattedValue))
            return formattedValue;

        try
        {
            return segment.Value?.GetUnformattedString();
        }
        catch
        {
            return null;
        }
    }

    private static (int? ViewId, string ViewType, double ViewScale) GetOwnerViewInfo(DrawingObject drawingObject)
    {
        var ownerView = drawingObject.GetView();
        if (ownerView == null)
            return (null, string.Empty, 1.0);

        return ownerView is View view
            ? (view.GetIdentifier().ID, view.ViewType.ToString(), view.Attributes?.Scale > 0 ? view.Attributes.Scale : 1.0)
            : (ownerView.GetIdentifier().ID, ownerView.GetType().Name, 1.0);
    }

    private static string TryGetDimensionType(StraightDimensionSet dimSet)
    {
        try
        {
            if (dimSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes straightAttributes)
                return straightAttributes.DimensionType.ToString();

            var attributesProperty = dimSet.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(static property => property.Name == "Attributes")
                .OrderByDescending(static property => property.PropertyType == typeof(StraightDimensionSet.StraightDimensionSetAttributes))
                .FirstOrDefault();
            var attributes = attributesProperty?.GetValue(dimSet, null);
            var dimensionTypeProperty = attributes?.GetType().GetProperty("DimensionType");
            var value = dimensionTypeProperty?.GetValue(attributes, null);
            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string NormalizeTemporaryDimensionValue(
        string? rawValue,
        DimensionSetBaseAttributes.DimensionValueUnits dimensionUnits,
        bool negative) => DimensionTextValueFormatter.NormalizeTemporaryValue(rawValue, dimensionUnits, negative);

    private static string TryGetDimensionTextPlacing(StraightDimensionSet dimSet) => DimensionTextPlacementHelper.GetTextPlacing(dimSet);

    internal static List<double[]>? TryCreateTextPolygon(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        DrawingLineInfo? dimensionLine)
    {
        if (dimensionLine == null)
            return null;

        if (segment.GetView() is not View view)
            return null;

        var textValue = TryGetMeasuredValueText(segment, dimSet, view);
        if (string.IsNullOrWhiteSpace(textValue))
            return null;

        var textAttributes = DimensionTextAttributeMapper.TryCreate(dimSet);
        if (textAttributes == null)
            return null;

        var upDirection = TryGetUpDirection(segment, out var resolvedUpDirection)
            ? resolvedUpDirection
            : ((double X, double Y)?)null;
        var placementContext = CreateDimensionTextPlacementContext(segment, dimSet);

        var runtimePolygon = TryCreateTextPolygonFromRuntimeTextObjects(segment, dimSet, textValue!, dimensionLine, textAttributes.Frame.Type);
        if (runtimePolygon != null)
            return runtimePolygon;

        var effectiveDimensionLine = ApplyDimensionTextLineOffsets(
            dimensionLine,
            placementContext.LeftTagLineOffset,
            placementContext.RightTagLineOffset);

        var size = TryMeasureTextSizeForFallback(view, textValue!, textAttributes);
        if (!size.HasValue)
            return null;

        var correctedSize = ApplyFrameSizeCorrection(size.Value.Width, size.Value.Height, textAttributes.Frame.Type);
        return DimensionTextPolygonPlacementHelper.CreateFallbackPolygon(
            effectiveDimensionLine,
            correctedSize.Width,
            correctedSize.Height,
            TryGetViewScale(view),
            placementContext,
            upDirection);
    }

    private static DrawingBoundsInfo CreateBoundsFromPolygon(IReadOnlyList<double[]> polygon)
    {
        return CreateBoundsInfo(
            polygon.Min(static point => point[0]),
            polygon.Min(static point => point[1]),
            polygon.Max(static point => point[0]),
            polygon.Max(static point => point[1]));
    }

    internal static List<double[]>? CreateDimStyleTextPolygon(
        DrawingLineInfo dimensionLine,
        double widthAlongLine,
        double heightPerpendicularToLine,
        double viewScale) => DimensionTextPolygonPlacementHelper.CreateDimStyleTextPolygon(dimensionLine, widthAlongLine, heightPerpendicularToLine, viewScale);

    internal static bool TryGetDimStyleLineVector(
        DrawingLineInfo dimensionLine,
        out (double X, double Y) lineVector) => DimensionPlacementHeuristics.TryGetDimStyleLineVector(dimensionLine, out lineVector);

    internal static bool ComparePointsLeftToRight((double X, double Y) left, (double X, double Y) right)
    {
        if (!(left.X >= right.X && left.Y >= right.Y))
            return left.X > right.X && left.Y < right.Y;

        return true;
    }

    private static double TryGetViewScale(View view)
    {
        try
        {
            return view.Attributes?.Scale > 0 ? view.Attributes.Scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static DimensionTextPlacementContext CreateDimensionTextPlacementContext(
        StraightDimension segment,
        StraightDimensionSet dimSet)
    {
        int? topDirection = null;
        if (TryGetUpDirection(segment, out var upDirection))
            topDirection = GetTopDirection(upDirection.X, upDirection.Y);

        return DimensionTextPlacementHelper.CreateContext(dimSet, topDirection);
    }

    internal static int ResolveDimensionTextSideSign(int topDirection, int placingDirectionSign) => DimensionTextPlacementHelper.ResolveTextSideSign(topDirection, placingDirectionSign);

    private static int TryGetDimensionPlacingDirectionSign(StraightDimensionSet dimSet) => DimensionTextPlacementHelper.GetPlacingDirectionSign(dimSet);

    private static double TryGetTagLineOffset(StraightDimensionSet dimSet, bool left) => DimensionTextPlacementHelper.GetTagLineOffset(dimSet, left);

    internal static DrawingLineInfo ApplyDimensionTextLineOffsets(
        DrawingLineInfo dimensionLine,
        double startOffset,
        double endOffset) => DimensionTextPlacementHelper.ApplyLineOffsets(dimensionLine, startOffset, endOffset);

    private static List<double[]>? TryCreateTextPolygonFromRuntimeTextObjects(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        string expectedText,
        DrawingLineInfo dimensionLine,
        FrameTypes frameType)
    {
        var polygon = TrySelectTextPolygon(segment.GetRelatedObjects(), expectedText, dimensionLine, frameType);
        if (polygon != null)
            return polygon;

        polygon = TrySelectTextPolygon(dimSet.GetRelatedObjects(), expectedText, dimensionLine, frameType);
        if (polygon != null)
            return polygon;

        polygon = TrySelectTextPolygon(segment, expectedText, dimensionLine, frameType);
        if (polygon != null)
            return polygon;

        return TrySelectTextPolygon(dimSet, expectedText, dimensionLine, frameType);
    }

    private static List<double[]>? TrySelectTextPolygon(
        DrawingObjectEnumerator relatedObjects,
        string expectedText,
        DrawingLineInfo dimensionLine,
        FrameTypes frameType)
    {
        var exactMatches = new List<(double Score, List<double[]> Polygon)>();
        var fallbackMatches = new List<(double Score, List<double[]> Polygon)>();
        while (relatedObjects.MoveNext())
        {
            if (!TryCreateRelatedTextCandidate(relatedObjects.Current, frameType, out var candidateText, out var polygon))
                continue;
            var score = ScoreTextPolygonAgainstDimensionLine(polygon, dimensionLine);
            if (MatchesDimensionText(candidateText, expectedText))
            {
                exactMatches.Add((score, polygon));
                continue;
            }

            fallbackMatches.Add((score, polygon));
        }

        if (exactMatches.Count > 0)
            return exactMatches.OrderBy(static candidate => candidate.Score).First().Polygon;

        return fallbackMatches.Count == 1
            ? fallbackMatches[0].Polygon
            : null;
    }

    private static List<double[]>? TrySelectTextPolygon(
        DrawingObject owner,
        string expectedText,
        DrawingLineInfo dimensionLine,
        FrameTypes frameType)
    {
        var exactMatches = new List<(double Score, List<double[]> Polygon)>();
        var fallbackMatches = new List<(double Score, List<double[]> Polygon)>();

        foreach (var candidate in EnumerateNestedDrawingObjects(owner))
        {
            if (!TryCreateRelatedTextCandidate(candidate, frameType, out var candidateText, out var polygon))
                continue;

            var score = ScoreTextPolygonAgainstDimensionLine(polygon, dimensionLine);
            if (MatchesDimensionText(candidateText, expectedText))
            {
                exactMatches.Add((score, polygon));
                continue;
            }

            fallbackMatches.Add((score, polygon));
        }

        if (exactMatches.Count > 0)
            return exactMatches.OrderBy(static candidate => candidate.Score).First().Polygon;

        return fallbackMatches.Count == 1
            ? fallbackMatches[0].Polygon
            : null;
    }

    private static IEnumerable<object?> EnumerateNestedDrawingObjects(object owner)
    {
        if (!TryGetChildObjects(owner, out var children))
        {
            yield break;
        }

        while (children.MoveNext())
        {
            var child = children.Current;
            yield return child;

            if (child != null)
            {
                foreach (var nestedChild in EnumerateNestedDrawingObjects(child))
                    yield return nestedChild;
            }
        }
    }

    private static bool TryGetChildObjects(object owner, out DrawingObjectEnumerator children)
    {
        children = null!;

        try
        {
            var getObjectsMethod = owner.GetType().GetMethod(
                "GetObjects",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                binder: null,
                types: System.Type.EmptyTypes,
                modifiers: null);
            if (getObjectsMethod?.Invoke(owner, null) is not DrawingObjectEnumerator enumerator)
                return false;

            children = enumerator;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateRelatedTextCandidate(
        object? relatedObject,
        FrameTypes frameType,
        out string? textValue,
        out List<double[]> polygon)
    {
        textValue = null;
        polygon = [];
        if (relatedObject == null)
            return false;

        try
        {
            if (relatedObject is Text text)
            {
                textValue = text.TextString;
                polygon = CreatePolygonFromObjectAlignedBox(text.GetObjectAlignedBoundingBox(), frameType);
                return true;
            }

            var type = relatedObject.GetType();
            var textProperty = type.GetProperty(
                "TextString",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var looksLikeText = textProperty != null
                || type.Name.IndexOf("Text", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeText)
                return false;

            var objectAlignedMethod = type.GetMethod(
                "GetObjectAlignedBoundingBox",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (objectAlignedMethod?.Invoke(relatedObject, null) is not RectangleBoundingBox objectAlignedBoundingBox)
                return false;

            textValue = textProperty?.GetValue(relatedObject, null)?.ToString();
            polygon = CreatePolygonFromObjectAlignedBox(objectAlignedBoundingBox, frameType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesDimensionText(string? candidateText, string expectedText)
    {
        if (string.IsNullOrWhiteSpace(candidateText))
            return false;

        static string NormalizeComparableText(string value) =>
            value.Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace(" ", string.Empty)
                .Trim();

        return string.Equals(
            NormalizeComparableText(candidateText!),
            NormalizeComparableText(expectedText),
            System.StringComparison.Ordinal);
    }

    private static double ScoreTextPolygonAgainstDimensionLine(
        IReadOnlyList<double[]> polygon,
        DrawingLineInfo dimensionLine)
    {
        if (!TryNormalizeDirection(
                dimensionLine.EndX - dimensionLine.StartX,
                dimensionLine.EndY - dimensionLine.StartY,
                out var axis))
        {
            return double.MaxValue;
        }

        var center = GetPolygonCenter(polygon);
        var lineCenterX = (dimensionLine.StartX + dimensionLine.EndX) / 2.0;
        var lineCenterY = (dimensionLine.StartY + dimensionLine.EndY) / 2.0;
        var normalX = -axis.Y;
        var normalY = axis.X;
        var alongDelta = System.Math.Abs(((center.X - lineCenterX) * axis.X) + ((center.Y - lineCenterY) * axis.Y));
        var normalDelta = System.Math.Abs(((center.X - lineCenterX) * normalX) + ((center.Y - lineCenterY) * normalY));
        return (normalDelta * 1000.0) + alongDelta;
    }

    private static (double X, double Y) GetPolygonCenter(IReadOnlyList<double[]> polygon)
    {
        if (polygon.Count == 0)
            return (0.0, 0.0);

        return (
            polygon.Average(static point => point[0]),
            polygon.Average(static point => point[1]));
    }

    private static double GetPolygonSpanAlongDirection(
        IReadOnlyList<double[]> polygon,
        double axisX,
        double axisY)
    {
        if (!TryNormalizeDirection(axisX, axisY, out var normalizedAxis))
            return 0.0;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in polygon)
        {
            var projection = (point[0] * normalizedAxis.X) + (point[1] * normalizedAxis.Y);
            min = System.Math.Min(min, projection);
            max = System.Math.Max(max, projection);
        }

        return max > min ? max - min : 0.0;
    }

    private static List<double[]> OffsetPolygon(
        IReadOnlyList<double[]> polygon,
        double offsetX,
        double offsetY)
    {
        return polygon
            .Select(point => new[]
            {
                System.Math.Round(point[0] + offsetX, 3),
                System.Math.Round(point[1] + offsetY, 3)
            })
            .ToList();
    }

    private static (double Width, double Height)? TryMeasureTextSizeForFallback(
        View view,
        string textValue,
        Text.TextAttributes textAttributes)
    {
        Text? text = null;
        try
        {
            var placing = new AlongLinePlacing(new Point(0.0, 0.0, 0.0), new Point(1000.0, 0.0, 0.0));
            text = new Text(view, new Point(0.0, 0.0, 0.0), textValue, placing, textAttributes);
            if (!text.Insert())
                return null;

            var objectAlignedBoundingBox = text.GetObjectAlignedBoundingBox();
            return (objectAlignedBoundingBox.Width, objectAlignedBoundingBox.Height);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (text != null)
            {
                try
                {
                    text.Delete();
                }
                catch
                {
                }
            }
        }
    }

    internal static (double Width, double Height) ApplyFrameSizeCorrection(
        double width,
        double height,
        FrameTypes frameType)
    {
        if (width <= 1e-6 || height <= 1e-6)
            return (width, height);

        if (FrameSizeCoefficientsBasedOnWidth.TryGetValue(frameType, out var widthBased))
            return (width + (height * widthBased.WidthFactor), height * widthBased.HeightFactor);

        if (FrameSizeCoefficientsBasedOnHeightAndWidth.TryGetValue(frameType, out var diagonalBased))
        {
            var diagonal = System.Math.Sqrt((width * width) + (height * height));
            return (diagonal * diagonalBased.WidthFactor, diagonal * diagonalBased.HeightFactor);
        }

        return (width, height);
    }

    internal static List<double[]> ApplyFrameSizeCorrectionToPolygon(
        IReadOnlyList<double[]> polygon,
        FrameTypes frameType)
    {
        if (polygon.Count != 4)
            return polygon.Select(static point => new[] { point[0], point[1] }).ToList();

        var lowerLeft = polygon[0];
        var upperLeft = polygon[1];
        var upperRight = polygon[2];
        var lowerRight = polygon[3];

        var width = System.Math.Sqrt(
            System.Math.Pow(lowerRight[0] - lowerLeft[0], 2) +
            System.Math.Pow(lowerRight[1] - lowerLeft[1], 2));
        var height = System.Math.Sqrt(
            System.Math.Pow(upperLeft[0] - lowerLeft[0], 2) +
            System.Math.Pow(upperLeft[1] - lowerLeft[1], 2));

        var correctedSize = ApplyFrameSizeCorrection(width, height, frameType);
        if (System.Math.Abs(correctedSize.Width - width) <= 1e-6
            && System.Math.Abs(correctedSize.Height - height) <= 1e-6)
        {
            return polygon
                .Select(static point => new[] { System.Math.Round(point[0], 3), System.Math.Round(point[1], 3) })
                .ToList();
        }

        if (!TryNormalizeDirection(lowerRight[0] - lowerLeft[0], lowerRight[1] - lowerLeft[1], out var widthAxis)
            || !TryNormalizeDirection(upperLeft[0] - lowerLeft[0], upperLeft[1] - lowerLeft[1], out var heightAxis))
        {
            return polygon
                .Select(static point => new[] { System.Math.Round(point[0], 3), System.Math.Round(point[1], 3) })
                .ToList();
        }

        var centerX = (lowerLeft[0] + upperLeft[0] + upperRight[0] + lowerRight[0]) / 4.0;
        var centerY = (lowerLeft[1] + upperLeft[1] + upperRight[1] + lowerRight[1]) / 4.0;
        var halfWidth = correctedSize.Width / 2.0;
        var halfHeight = correctedSize.Height / 2.0;

        var correctedPolygon = new[]
        {
            new[] { centerX - (widthAxis.X * halfWidth) - (heightAxis.X * halfHeight), centerY - (widthAxis.Y * halfWidth) - (heightAxis.Y * halfHeight) },
            new[] { centerX - (widthAxis.X * halfWidth) + (heightAxis.X * halfHeight), centerY - (widthAxis.Y * halfWidth) + (heightAxis.Y * halfHeight) },
            new[] { centerX + (widthAxis.X * halfWidth) + (heightAxis.X * halfHeight), centerY + (widthAxis.Y * halfWidth) + (heightAxis.Y * halfHeight) },
            new[] { centerX + (widthAxis.X * halfWidth) - (heightAxis.X * halfHeight), centerY + (widthAxis.Y * halfWidth) - (heightAxis.Y * halfHeight) }
        };

        return correctedPolygon
            .Select(static point => new[] { System.Math.Round(point[0], 3), System.Math.Round(point[1], 3) })
            .ToList();
    }

    private static List<double[]> CreatePolygonFromObjectAlignedBox(
        RectangleBoundingBox objectAlignedBoundingBox,
        FrameTypes frameType)
    {
        return ApplyFrameSizeCorrectionToPolygon(
        [
            [System.Math.Round(objectAlignedBoundingBox.LowerLeft.X, 3), System.Math.Round(objectAlignedBoundingBox.LowerLeft.Y, 3)],
            [System.Math.Round(objectAlignedBoundingBox.UpperLeft.X, 3), System.Math.Round(objectAlignedBoundingBox.UpperLeft.Y, 3)],
            [System.Math.Round(objectAlignedBoundingBox.UpperRight.X, 3), System.Math.Round(objectAlignedBoundingBox.UpperRight.Y, 3)],
            [System.Math.Round(objectAlignedBoundingBox.LowerRight.X, 3), System.Math.Round(objectAlignedBoundingBox.LowerRight.Y, 3)]
        ], frameType);
    }

    private static bool TryGetUpDirection(StraightDimension segment, out (double X, double Y) direction)
    {
        direction = default;
        try
        {
            var upDirection = segment.UpDirection;
            if (TryNormalizeDirection(upDirection.X, upDirection.Y, out direction))
                return true;
        }
        catch
        {
        }

        try
        {
            var property = segment.GetType().GetProperty("UpDirection");
            var value = property?.GetValue(segment, null);
            if (value == null)
                return false;

            var xProperty = value.GetType().GetProperty("X");
            var yProperty = value.GetType().GetProperty("Y");
            if (xProperty?.GetValue(value, null) is not double x || yProperty?.GetValue(value, null) is not double y)
                return false;

            return TryNormalizeDirection(x, y, out direction);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryNormalizeDirection(double x, double y, out (double X, double Y) direction)
    {
        direction = default;
        var length = System.Math.Sqrt((x * x) + (y * y));
        if (length <= 1e-6)
            return false;

        direction = (System.Math.Round(x / length, 6), System.Math.Round(y / length, 6));
        return true;
    }

    internal static (double X, double Y) CanonicalizeDirection(double x, double y)
    {
        if (x < -1e-6 || (System.Math.Abs(x) <= 1e-6 && y < -1e-6))
            return (-x, -y);

        return (x, y);
    }

    internal static int GetTopDirection(double upX, double upY)
    {
        if (upY < -1e-6 || (System.Math.Abs(upY) <= 1e-6 && upX > 1e-6))
            return -1;

        return 1;
    }

    internal static DrawingLineInfo? TryCreateCommonReferenceLine(
        IReadOnlyList<(double X, double Y)> points,
        (double X, double Y) upDirection,
        double distance,
        out (double X, double Y) direction) => DimensionProjectionHelper.TryCreateCommonReferenceLine(points, upDirection, distance, out direction);

    internal static DrawingLineInfo? TryCreateReferenceLine(DimensionSegmentInfo segment)
    {
        return segment.DimensionLine == null
            ? null
            : CreateLineInfo(
                segment.DimensionLine.StartX,
                segment.DimensionLine.StartY,
                segment.DimensionLine.EndX,
                segment.DimensionLine.EndY);
    }

    internal static (double X, double Y) CreateReferencePoint(
        double pointX,
        double pointY,
        (double X, double Y) upDirection,
        double distance) => DimensionProjectionHelper.CreateReferencePoint(pointX, pointY, upDirection, distance);

    internal static (double X, double Y) ProjectPointToReferenceLine(
        double pointX,
        double pointY,
        double lineOriginX,
        double lineOriginY,
        double lineDirectionX,
        double lineDirectionY) => DimensionProjectionHelper.ProjectPointToReferenceLine(pointX, pointY, lineOriginX, lineOriginY, lineDirectionX, lineDirectionY);

    internal static (double X, double Y) CreatePointOnDimensionLine(
        double alongProjection,
        double offsetProjection,
        (double X, double Y) direction,
        (double X, double Y) upDirection) => DimensionProjectionHelper.CreatePointOnDimensionLine(alongProjection, offsetProjection, direction, upDirection);

    internal static List<DrawingPointInfo> BuildMeasuredPointList(
        IReadOnlyList<DimensionSegmentInfo> segments,
        double directionX,
        double directionY)
    {
        var nodes = new Dictionary<(double X, double Y), HashSet<(double X, double Y)>>();

        foreach (var segment in segments)
        {
            var start = (System.Math.Round(segment.StartX, 3), System.Math.Round(segment.StartY, 3));
            var end = (System.Math.Round(segment.EndX, 3), System.Math.Round(segment.EndY, 3));

            if (!nodes.TryGetValue(start, out var startNeighbours))
            {
                startNeighbours = [];
                nodes[start] = startNeighbours;
            }

            if (!nodes.TryGetValue(end, out var endNeighbours))
            {
                endNeighbours = [];
                nodes[end] = endNeighbours;
            }

            startNeighbours.Add(end);
            endNeighbours.Add(start);
        }

        if (nodes.Count == 0)
            return [];

        var orderedKeys = OrderMeasuredPointKeys(nodes, directionX, directionY);
        return orderedKeys
            .Select((point, index) => new DrawingPointInfo
            {
                X = point.X,
                Y = point.Y,
                Order = index
            })
            .ToList();
    }

    internal static int FindMeasuredPointOrder(
        IReadOnlyList<DrawingPointInfo> points,
        double x,
        double y,
        double tolerance = 0.5)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (System.Math.Abs(points[i].X - x) <= tolerance &&
                System.Math.Abs(points[i].Y - y) <= tolerance)
            {
                return points[i].Order;
            }
        }

        return -1;
    }

    private static List<(double X, double Y)> OrderMeasuredPointKeys(
        Dictionary<(double X, double Y), HashSet<(double X, double Y)>> nodes,
        double directionX,
        double directionY)
    {
        var result = new List<(double X, double Y)>();
        var visitedEdges = new HashSet<((double X, double Y) A, (double X, double Y) B)>();
        var remaining = new HashSet<(double X, double Y)>(nodes.Keys);
        var hasDirection = TryNormalizeDirection(directionX, directionY, out var direction);
        var directionValue = hasDirection ? CanonicalizeDirection(direction.X, direction.Y) : (1d, 0d);

        while (remaining.Count > 0)
        {
            var start = SelectMeasuredChainStart(remaining, nodes, directionValue);
            TraverseMeasuredChain(start, nodes, directionValue, visitedEdges, result, remaining);
        }

        return result;
    }

    private static (double X, double Y) SelectMeasuredChainStart(
        IEnumerable<(double X, double Y)> candidates,
        IReadOnlyDictionary<(double X, double Y), HashSet<(double X, double Y)>> nodes,
        (double X, double Y) direction)
    {
        var endpoints = candidates
            .Where(point => nodes.TryGetValue(point, out var neighbours) && neighbours.Count <= 1)
            .ToList();

        var source = endpoints.Count > 0 ? endpoints : candidates.ToList();
        return source
            .OrderByDescending(point => Project(point.X, point.Y, direction.X, direction.Y))
            .ThenBy(point => point.X)
            .ThenBy(point => point.Y)
            .First();
    }

    private static void TraverseMeasuredChain(
        (double X, double Y) start,
        IReadOnlyDictionary<(double X, double Y), HashSet<(double X, double Y)>> nodes,
        (double X, double Y) direction,
        HashSet<((double X, double Y) A, (double X, double Y) B)> visitedEdges,
        List<(double X, double Y)> ordered,
        HashSet<(double X, double Y)> remaining)
    {
        var current = start;
        (double X, double Y)? previous = null;

        while (true)
        {
            if (remaining.Remove(current))
                ordered.Add(current);

            if (!nodes.TryGetValue(current, out var neighbours) || neighbours.Count == 0)
                break;

            var nextCandidates = neighbours
                .Where(candidate => !visitedEdges.Contains(NormalizeEdge(current, candidate)))
                .OrderByDescending(candidate => Project(candidate.X, candidate.Y, direction.X, direction.Y))
                .ThenBy(candidate => previous.HasValue && candidate.Equals(previous.Value) ? 1 : 0)
                .ThenBy(candidate => candidate.X)
                .ThenBy(candidate => candidate.Y)
                .ToList();

            if (nextCandidates.Count == 0)
                break;

            var next = nextCandidates[0];
            var edge = NormalizeEdge(current, next);
            if (visitedEdges.Contains(edge))
                break;

            visitedEdges.Add(edge);
            previous = current;
            current = next;
        }
    }

    private static ((double X, double Y) A, (double X, double Y) B) NormalizeEdge(
        (double X, double Y) first,
        (double X, double Y) second)
    {
        return first.X < second.X || (System.Math.Abs(first.X - second.X) <= 1e-6 && first.Y <= second.Y)
            ? (first, second)
            : (second, first);
    }

    private static double Project(double x, double y, double axisX, double axisY) => (x * axisX) + (y * axisY);

    private static Vector? TryParseVector(string? s) => DimensionCreatePlacementHelper.TryParseVector(s);

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }

    private static View ResolveTargetView(Tekla.Structures.Drawing.Drawing drawing, int? viewId)
    {
        var views = EnumerateViews(drawing).ToList();
        if (views.Count == 0)
            throw new System.InvalidOperationException("No views found in active drawing.");

        if (viewId.HasValue)
            return views.FirstOrDefault(v => v.GetIdentifier().ID == viewId.Value)
                ?? throw new ViewNotFoundException(viewId.Value);

        var frontView = views.FirstOrDefault(v =>
            string.Equals(v.ViewType.ToString(), "FrontView", System.StringComparison.OrdinalIgnoreCase));
        if (frontView != null)
            return frontView;

        return views
            .OrderByDescending(v => v.Width * v.Height)
            .First();
    }

    internal static Vector BuildDiagonalOffsetDirection(Point start, Point end) => DimensionDiagonalPlacementHelper.BuildOffsetDirection(start, end);

    /// <summary>
    /// Removes hull vertices that are nearly collinear with their neighbors
    /// (perpendicular deviation below threshold). Eliminates spurious snap points
    /// that are genuine hull vertices but barely outside the true shape boundary.
    /// </summary>
    internal static List<Point> SimplifyHull(List<Point> hull, double maxDeviationMm = 1.0)
    {
        if (hull.Count <= 3)
            return hull;

        var simplified = new List<Point>(hull);
        var changed = true;
        while (changed && simplified.Count > 3)
        {
            changed = false;
            for (var i = 0; i < simplified.Count; i++)
            {
                var prev = simplified[(i - 1 + simplified.Count) % simplified.Count];
                var curr = simplified[i];
                var next = simplified[(i + 1) % simplified.Count];

                var edgeDx = next.X - prev.X;
                var edgeDy = next.Y - prev.Y;
                var edgeLen = System.Math.Sqrt((edgeDx * edgeDx) + (edgeDy * edgeDy));
                if (edgeLen < 1e-6)
                    continue;

                // Perpendicular distance from curr to the line prev→next
                var dist = System.Math.Abs((edgeDx * (prev.Y - curr.Y)) - (edgeDy * (prev.X - curr.X))) / edgeLen;
                if (dist < maxDeviationMm)
                {
                    simplified.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        return simplified;
    }

    /// <summary>
    /// Removes statistical outlier points using MAD (Median Absolute Deviation).
    /// Protects convex hull from spurious snap points near the model origin.
    /// </summary>
    internal static List<Point> FilterOutlierPoints(List<Point> points)
    {
        if (points.Count <= 4)
            return points;

        var xs = points.Select(p => p.X).OrderBy(x => x).ToList();
        var ys = points.Select(p => p.Y).OrderBy(y => y).ToList();
        var medX = xs[xs.Count / 2];
        var medY = ys[ys.Count / 2];

        var madXs = points.Select(p => System.Math.Abs(p.X - medX)).OrderBy(v => v).ToList();
        var madYs = points.Select(p => System.Math.Abs(p.Y - medY)).OrderBy(v => v).ToList();
        var madX = madXs[madXs.Count / 2];
        var madY = madYs[madYs.Count / 2];

        // Use a generous threshold (6×MAD ≈ ~4σ for normal distribution).
        // Fall back to a minimum scale so near-zero MAD doesn't discard valid points.
        const double ThresholdFactor = 6.0;
        const double MinScale = 10.0; // mm
        var threshX = System.Math.Max(madX * ThresholdFactor, MinScale);
        var threshY = System.Math.Max(madY * ThresholdFactor, MinScale);

        var filtered = points.Where(p =>
            System.Math.Abs(p.X - medX) <= threshX &&
            System.Math.Abs(p.Y - medY) <= threshY).ToList();

        // Never return fewer than 2 points — fall back to original if over-filtered.
        return filtered.Count >= 2 ? filtered : points;
    }

    private static List<Point> CollectDimensionSegmentPoints(View targetView, out int dimensionsScanned)
    {
        var points = new List<Point>();
        dimensionsScanned = 0;

        var segmentObjects = targetView.GetAllObjects(typeof(StraightDimension));
        while (segmentObjects.MoveNext())
        {
            if (segmentObjects.Current is not StraightDimension segment)
                continue;

            dimensionsScanned++;
            points.Add(new Point(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z));
            points.Add(new Point(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z));
        }

        if (points.Count >= 2)
            return points;

        points.Clear();
        dimensionsScanned = 0;

        var dimObjects = targetView.GetAllObjects(typeof(StraightDimensionSet));
        while (dimObjects.MoveNext())
        {
            if (dimObjects.Current is not StraightDimensionSet dimSet)
                continue;

            dimensionsScanned++;
            var segEnum = dimSet.GetObjects();
            while (segEnum.MoveNext())
            {
                if (segEnum.Current is not StraightDimension segment)
                    continue;

                points.Add(new Point(segment.StartPoint.X, segment.StartPoint.Y, segment.StartPoint.Z));
                points.Add(new Point(segment.EndPoint.X, segment.EndPoint.Y, segment.EndPoint.Z));
            }
        }

        return points;
    }

    private static bool TryFindSecondaryDiagonal(
        IReadOnlyList<Point> hull,
        Point firstA,
        Point firstB,
        out (Point Start, Point End) pair)
    {
        if (TryFindSecondaryDiagonalInternal(hull, firstA, firstB, requireDistinctEndpoints: true, out pair))
            return true;

        return TryFindSecondaryDiagonalInternal(hull, firstA, firstB, requireDistinctEndpoints: false, out pair);
    }

    private static bool TryFindSecondaryDiagonalInternal(
        IReadOnlyList<Point> hull,
        Point firstA,
        Point firstB,
        bool requireDistinctEndpoints,
        out (Point Start, Point End) pair)
    {
        pair = default;
        var firstLen = System.Math.Sqrt(ConvexHull.DistanceSquared(firstA, firstB));
        if (firstLen < 1e-9)
            return false;

        var hasCandidate = false;
        var bestScore = double.MinValue;
        for (var i = 0; i < hull.Count; i++)
        {
            for (var j = i + 1; j < hull.Count; j++)
            {
                var a = hull[i];
                var b = hull[j];

                if (IsSamePair(a, b, firstA, firstB))
                    continue;

                if (requireDistinctEndpoints
                    && (SamePointXY(a, firstA) || SamePointXY(a, firstB) || SamePointXY(b, firstA) || SamePointXY(b, firstB)))
                {
                    continue;
                }

                var lenSquared = ConvexHull.DistanceSquared(a, b);
                if (lenSquared < 1e-9)
                    continue;

                var len = System.Math.Sqrt(lenSquared);
                var dotAbs = System.Math.Abs(((firstB.X - firstA.X) * (b.X - a.X) + (firstB.Y - firstA.Y) * (b.Y - a.Y)) / (firstLen * len));
                if (dotAbs > 0.98)
                    continue;

                var crossingBonus = SegmentsProperlyIntersect(firstA, firstB, a, b) ? 1_000_000_000d : 0d;
                var score = crossingBonus + lenSquared;
                if (!hasCandidate || score > bestScore)
                {
                    hasCandidate = true;
                    bestScore = score;
                    pair = (a, b);
                }
            }
        }

        return hasCandidate;
    }

    private static bool IsRectangleLikeHull(IReadOnlyList<Point> hull)
    {
        if (hull.Count != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            var prev = hull[(i + 3) % 4];
            var curr = hull[i];
            var next = hull[(i + 1) % 4];

            var vx1 = curr.X - prev.X;
            var vy1 = curr.Y - prev.Y;
            var vx2 = next.X - curr.X;
            var vy2 = next.Y - curr.Y;

            var len1 = System.Math.Sqrt(vx1 * vx1 + vy1 * vy1);
            var len2 = System.Math.Sqrt(vx2 * vx2 + vy2 * vy2);
            if (len1 < 1e-9 || len2 < 1e-9)
                return false;

            var cos = System.Math.Abs((vx1 * vx2 + vy1 * vy2) / (len1 * len2));
            if (cos > 0.2)
                return false;
        }

        return true;
    }

    private static bool SegmentsProperlyIntersect(Point a1, Point a2, Point b1, Point b2)
    {
        var o1 = GeometricOrientation(a1, a2, b1);
        var o2 = GeometricOrientation(a1, a2, b2);
        var o3 = GeometricOrientation(b1, b2, a1);
        var o4 = GeometricOrientation(b1, b2, a2);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    private static double GeometricOrientation(Point a, Point b, Point c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool IsSamePair(Point a1, Point a2, Point b1, Point b2)
    {
        return (SamePointXY(a1, b1) && SamePointXY(a2, b2))
            || (SamePointXY(a1, b2) && SamePointXY(a2, b1));
    }

    private static bool SamePointXY(Point left, Point right)
    {
        return System.Math.Abs(left.X - right.X) <= 1.0
            && System.Math.Abs(left.Y - right.Y) <= 1.0;
    }
}
