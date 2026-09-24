using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.Dimensions;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class CreateDimensionPlacementTests
{
    [Fact]
    public void CreateDimensionParserUsesAutomaticPlacementWhenDistanceOmitted()
    {
        var result = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "17", "[0,10,0,100,20,0]", "horizontal", "", "standard", ""]);

        Assert.True(result.IsValid);
        Assert.Null(result.Request.Distance);
        Assert.Null(result.Request.PaperGapMm);
        Assert.Equal("standard", result.Request.AttributesFile);
    }

    [Fact]
    public void CreateDimensionParserKeepsManualDistanceAndRejectsSupplyingBothModes()
    {
        var manual = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "17", "[0,10,0,100,20,0]", "horizontal", "42", "standard", ""]);
        Assert.True(manual.IsValid);
        Assert.Equal(42, manual.Request.Distance);

        var gap = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "17", "[0,10,0,100,20,0]", "horizontal", "", "standard", "9"]);
        Assert.True(gap.IsValid);
        Assert.Null(gap.Request.Distance);
        Assert.Equal(9, gap.Request.PaperGapMm);

        var both = DrawingCommandParsers.ParseCreateDimensionRequest(
            ["create_dimension", "17", "[0,10,0,100,20,0]", "horizontal", "42", "standard", "9"]);
        Assert.False(both.IsValid);
        Assert.Contains("either distance or paperGapMm", both.Error);
    }

    [Theory]
    [InlineData(DimensionChainSide.Top, 86.8, 435.05, 348.25)]
    [InlineData(DimensionChainSide.Bottom, 86.8, -48, 134.8)]
    [InlineData(DimensionChainSide.Left, 0, -48, 48)]
    [InlineData(DimensionChainSide.Right, 0, 308, 308)]
    public void CalculatorOffsetsFromAssemblyExtentAndTeklasFirstPoint(
        DimensionChainSide side, double expectedBase, double expectedLine, double expectedDistance)
    {
        var shape = new GeometryGroupShape("outline",
            RegionFlattener.Flatten(new[] { new Vec3(-40, -40, 0), new Vec3(300, -40, 0),
                new Vec3(300, 427.05, 0), new Vec3(-40, 427.05, 0) }.ToList()));
        var group = new GeometryGroup("assembly", [shape]);
        var points = side is DimensionChainSide.Top or DimensionChainSide.Bottom
            ? new[] { 0d, 86.8, 0d, 100d, 427.05, 0d }
            : new[] { 0d, 0d, 0d, 100d, 100d, 0d };

        var result = DimensionPlacementCalculator.Calculate(side, points, group.Extent!, 1);

        Assert.Equal(8, result.PaperGapMm);
        Assert.Equal(8, result.GapViewUnits);
        Assert.Equal(expectedBase, result.BaseCoordinate, 6);
        Assert.Equal(expectedLine, result.TargetLineCoordinate, 6);
        Assert.Equal(expectedDistance, result.Distance, 6);
    }

    [Fact]
    public void CalculatorRejectsAmbiguousFirstPointAndPointsPastTheTargetSide()
    {
        var shape = new GeometryGroupShape("outline",
            RegionFlattener.Flatten(new[] { new Vec3(-10, -10, 0), new Vec3(10, -10, 0),
                new Vec3(10, 10, 0), new Vec3(-10, 10, 0) }.ToList()));
        var extent = new GeometryGroup("assembly", [shape]).Extent!;

        Assert.Throws<System.ArgumentException>(() => DimensionPlacementCalculator.Calculate(
            DimensionChainSide.Top, [0, 1, 0, 0, 2, 0], extent, 1));
        Assert.Throws<System.ArgumentException>(() => DimensionPlacementCalculator.Calculate(
            DimensionChainSide.Top, [0, 100, 0, 10, 100, 0], extent, 1));
    }
}
