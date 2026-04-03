using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public class DimensionTextSupportHelperTests
{
    [Theory]
    [InlineData(DimensionSetBaseAttributes.FrameTypes.None, FrameTypes.None)]
    [InlineData(DimensionSetBaseAttributes.FrameTypes.Rectangle, FrameTypes.Rectangular)]
    [InlineData(DimensionSetBaseAttributes.FrameTypes.RoundedRectangle, FrameTypes.Round)]
    [InlineData(DimensionSetBaseAttributes.FrameTypes.SharpenedRectangle, FrameTypes.Sharpened)]
    [InlineData(DimensionSetBaseAttributes.FrameTypes.Underline, FrameTypes.Line)]
    public void MapFrameType_UsesExpectedMapping(
        DimensionSetBaseAttributes.FrameTypes source,
        FrameTypes expected)
    {
        var actual = DimensionTextAttributeMapper.MapFrameType(source);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateTextAttributes_CopiesFontAndUsesFixedPlacementDefaults()
    {
#pragma warning disable CS0618
        var dimensionAttributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
        dimensionAttributes.Text.Frame = DimensionSetBaseAttributes.FrameTypes.Rectangle;
        dimensionAttributes.Text.Font.Height = 3.5;

        var actual = DimensionTextAttributeMapper.Create(dimensionAttributes);

        Assert.Equal(FrameTypes.Rectangular, actual.Frame.Type);
        Assert.Equal(3.5, actual.Font.Height, 6);
        Assert.NotNull(actual.PreferredPlacing);
        Assert.True(actual.PlacingAttributes.IsFixed);
        Assert.True(actual.PlacingAttributes.PlacingQuarter.TopLeft);
        Assert.False(actual.UseWordWrapping);
    }
}
