using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal readonly struct DimensionTextPlacementContext
{
    public DimensionTextPlacementContext(
        string textPlacing,
        int sideSign,
        double leftTagLineOffset,
        double rightTagLineOffset)
    {
        TextPlacing = textPlacing;
        SideSign = sideSign;
        LeftTagLineOffset = leftTagLineOffset;
        RightTagLineOffset = rightTagLineOffset;
    }

    public string TextPlacing { get; }
    public int SideSign { get; }
    public double LeftTagLineOffset { get; }
    public double RightTagLineOffset { get; }
}

internal static class DimensionTextPlacementHelper
{
    internal static DimensionTextPlacementContext CreateContext(
        StraightDimensionSet dimSet,
        int? topDirection)
    {
        var sideSign = 1;
        if (topDirection.HasValue)
            sideSign = ResolveTextSideSign(topDirection.Value, GetPlacingDirectionSign(dimSet));

        return new DimensionTextPlacementContext(
            GetTextPlacing(dimSet),
            sideSign == 0 ? 1 : sideSign,
            GetTagLineOffset(dimSet, left: true),
            GetTagLineOffset(dimSet, left: false));
    }

    internal static string GetTextPlacing(StraightDimensionSet dimSet)
    {
        try
        {
            if (dimSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
                return attributes.Text.TextPlacing.ToString();

            var attributesProperty = dimSet.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(static property => property.Name == "Attributes")
                .OrderByDescending(static property => property.PropertyType == typeof(StraightDimensionSet.StraightDimensionSetAttributes))
                .FirstOrDefault();
            var reflectedAttributes = attributesProperty?.GetValue(dimSet, null);
            var textProperty = reflectedAttributes?.GetType().GetProperty("Text");
            var textAttributes = textProperty?.GetValue(reflectedAttributes, null);
            var textPlacingProperty = textAttributes?.GetType().GetProperty("TextPlacing");
            return textPlacingProperty?.GetValue(textAttributes, null)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static int ResolveTextSideSign(int topDirection, int placingDirectionSign)
    {
        var normalizedTopDirection = topDirection == 0 ? 1 : System.Math.Sign(topDirection);
        var normalizedPlacingDirection = placingDirectionSign == 0 ? 1 : System.Math.Sign(placingDirectionSign);
        return normalizedTopDirection * normalizedPlacingDirection;
    }

    internal static int GetPlacingDirectionSign(StraightDimensionSet dimSet)
    {
        try
        {
            if (dimSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
            {
                var positive = attributes.Placing.Direction.Positive;
                var negative = attributes.Placing.Direction.Negative;
                return negative && !positive ? -1 : 1;
            }

            var attributesProperty = dimSet.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(static property => property.Name == "Attributes")
                .OrderByDescending(static property => property.PropertyType == typeof(StraightDimensionSet.StraightDimensionSetAttributes))
                .FirstOrDefault();
            var reflectedAttributes = attributesProperty?.GetValue(dimSet, null);
            var placingProperty = reflectedAttributes?.GetType().GetProperty("Placing");
            var placingAttributes = placingProperty?.GetValue(reflectedAttributes, null);
            var directionProperty = placingAttributes?.GetType().GetProperty("Direction");
            var directionAttributes = directionProperty?.GetValue(placingAttributes, null);
            var positiveProperty = directionAttributes?.GetType().GetProperty("Positive");
            var negativeProperty = directionAttributes?.GetType().GetProperty("Negative");
            var positiveReflected = positiveProperty?.GetValue(directionAttributes, null) as bool?;
            var negativeReflected = negativeProperty?.GetValue(directionAttributes, null) as bool?;
            return negativeReflected == true && positiveReflected != true ? -1 : 1;
        }
        catch
        {
            return 1;
        }
    }

    internal static double GetTagLineOffset(StraightDimensionSet dimSet, bool left)
    {
        try
        {
            var offset = left ? dimSet.LeftTagLineOffset : dimSet.RightTagLineOffset;
            return offset > 1e-6 ? offset : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    internal static DrawingLineInfo ApplyLineOffsets(
        DrawingLineInfo dimensionLine,
        double startOffset,
        double endOffset)
    {
        if (startOffset <= 1e-6 && endOffset <= 1e-6)
            return TeklaDrawingDimensionsApi.CreateLineInfo(dimensionLine.StartX, dimensionLine.StartY, dimensionLine.EndX, dimensionLine.EndY);

        if (!TeklaDrawingDimensionsApi.TryNormalizeDirection(
                dimensionLine.EndX - dimensionLine.StartX,
                dimensionLine.EndY - dimensionLine.StartY,
                out var axis))
        {
            return TeklaDrawingDimensionsApi.CreateLineInfo(dimensionLine.StartX, dimensionLine.StartY, dimensionLine.EndX, dimensionLine.EndY);
        }

        var length = System.Math.Sqrt(
            System.Math.Pow(dimensionLine.EndX - dimensionLine.StartX, 2) +
            System.Math.Pow(dimensionLine.EndY - dimensionLine.StartY, 2));
        var clampedStartOffset = System.Math.Max(0.0, startOffset);
        var clampedEndOffset = System.Math.Max(0.0, endOffset);
        if ((clampedStartOffset + clampedEndOffset) >= length - 1e-6)
            return TeklaDrawingDimensionsApi.CreateLineInfo(dimensionLine.StartX, dimensionLine.StartY, dimensionLine.EndX, dimensionLine.EndY);

        return TeklaDrawingDimensionsApi.CreateLineInfo(
            dimensionLine.StartX + (axis.X * clampedStartOffset),
            dimensionLine.StartY + (axis.Y * clampedStartOffset),
            dimensionLine.EndX - (axis.X * clampedEndOffset),
            dimensionLine.EndY - (axis.Y * clampedEndOffset));
    }
}
