using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MainPartMaterialTests
{
    [Theory]
    [InlineData(1, "steel")]
    [InlineData(2, "concrete")]
    [InlineData(5, "timber")]
    [InlineData(6, "misc")]
    [InlineData(-1, "unknown")]
    [InlineData(3, "unknown")]
    public void MaterialTypeMapsToTheSkillRoute(int materialType, string route) =>
        Assert.Equal(route, TeklaDrawingInteractionApi.MaterialClass(materialType));

    [Theory]
    [InlineData("S355J2", "steel")]
    [InlineData("C24", "timber")]
    [InlineData("GL24h", "timber")]
    [InlineData("C30/37", "concrete")]
    public void AnUnreadTypeFallsBackToTheMaterialName(string material, string route) =>
        Assert.Equal(route, TeklaDrawingInteractionApi.MaterialClass(TeklaDrawingPartGeometryApi.InferMaterialType(material)));
}
