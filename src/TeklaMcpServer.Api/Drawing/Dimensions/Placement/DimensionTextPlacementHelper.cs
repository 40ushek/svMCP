using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionTextPlacementHelper
{
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
}
