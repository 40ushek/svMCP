using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionTextAttributeMapper
{
    internal static Text.TextAttributes? TryCreate(StraightDimensionSet dimSet)
    {
        try
        {
            if (dimSet.Attributes is not StraightDimensionSet.StraightDimensionSetAttributes attributes)
                return null;

            return Create(attributes);
        }
        catch
        {
            return null;
        }
    }

    internal static Text.TextAttributes Create(StraightDimensionSet.StraightDimensionSetAttributes attributes)
    {
        var textAttributes = new Text.TextAttributes
        {
            PreferredPlacing = PreferredTextPlacingTypes.AlongLinePlacingType(),
            Font = attributes.Text.Font
        };

        textAttributes.Frame.Type = MapFrameType(attributes.Text.Frame);
        textAttributes.PlacingAttributes.IsFixed = true;
        textAttributes.PlacingAttributes.PlacingQuarter.TopLeft = true;
        textAttributes.UseWordWrapping = false;

        return textAttributes;
    }

    internal static FrameTypes MapFrameType(DimensionSetBaseAttributes.FrameTypes frameType)
    {
        return frameType switch
        {
            DimensionSetBaseAttributes.FrameTypes.None => FrameTypes.None,
            DimensionSetBaseAttributes.FrameTypes.Rectangle => FrameTypes.Rectangular,
            DimensionSetBaseAttributes.FrameTypes.RoundedRectangle => FrameTypes.Round,
            DimensionSetBaseAttributes.FrameTypes.SharpenedRectangle => FrameTypes.Sharpened,
            DimensionSetBaseAttributes.FrameTypes.Underline => FrameTypes.Line,
            _ => FrameTypes.None
        };
    }
}
