using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkLayoutFixedBlockerBuilderTests
{
    [Fact]
    public void BuildDimensionTextBoxPolygons_UsesDimensionTextBoxPolygons()
    {
        var context = new DrawingViewContext();
        var polygon = new List<double[]>
        {
            new[] { 1.0, 2.0 },
            new[] { 3.0, 2.0 },
            new[] { 3.0, 4.0 },
            new[] { 1.0, 4.0 }
        };
        context.DimensionTextBoxes.Add(new DrawingTextBox
        {
            SourceKind = DrawingTextBoxSourceKind.Dimension,
            SourceObjectId = 10,
            Text = "100",
            Polygon = polygon
        });

        var polygons = MarkLayoutFixedBlockerBuilder.BuildDimensionTextBoxPolygons(context);

        Assert.Single(polygons);
        Assert.Same(polygon, polygons[0]);
    }

    [Fact]
    public void BuildDimensionTextBoxPolygons_IgnoresDegeneratePolygons()
    {
        var context = new DrawingViewContext();
        context.DimensionTextBoxes.Add(new DrawingTextBox
        {
            SourceKind = DrawingTextBoxSourceKind.Dimension,
            SourceObjectId = 10,
            Text = "100",
            Polygon =
            [
                new[] { 1.0, 2.0 },
                new[] { 3.0, 2.0 }
            ]
        });

        var polygons = MarkLayoutFixedBlockerBuilder.BuildDimensionTextBoxPolygons(context);

        Assert.Empty(polygons);
    }
}
