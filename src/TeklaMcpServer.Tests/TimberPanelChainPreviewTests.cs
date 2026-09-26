using System.Linq;
using System.Text.Json;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class TimberPanelChainPreviewTests
{
    [Fact]
    public void Iw11WallFixtureMatchesAcceptedHorizontalAndVerticalChains()
    {
        var parts = new[]
        {
            Part(3759265, .04, 60.04, -1278.5, 1053.5),
            Part(3759385, 60.04, 120.04, -1278.5, 1053.5),
            Part(4085631, 547.54, 607.54, -1173.5, 1053.5),
            Part(3759355, 1172.54, 1232.54, -1173.5, 1053.5),
            Part(3759325, 1797.54, 1857.54, -1173.5, 1053.5),
            Part(3759295, 2422.54, 2482.54, -1173.5, 1053.5),
            Part(6987430, 2957.54, 3017.54, -1278.5, 1053.5),
            Part(3759235, 3017.54, 3077.54, -1278.5, 1453.5),
            Part(3759205, .04, 3017.54, 1053.5, 1233.5),
            Part(3759175, 120.04, 2957.54, -1233.5, -1173.5)
        };
        var boundary = Rectangle("boundary", .04, 3077.54, -1278.5, 1453.5);
        var group = new GeometryGroup("iw11-wall", [boundary], parts);
        CalcDimensionChains.Apply(group);
        var catalog = DimensionPointCatalog.Build(group.DimensionChains!);

        var preview = TimberPanelChainPreview.Build(catalog, group, parts.Select(p => p.ModelId!.Value).ToArray(), 3, () => null);
        var rows = preview.Rows;
        var bottom = Read(rows.Single(r => r.side == "Bottom").chains[0]);
        Assert.Equal(new[] { 120d, 427.5, 625, 625, 625, 535, 120 }, bottom.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Empty(preview.UnlocatedModelIds);

        var left = Read(rows.Single(r => r.side == "Left").chains[0]);
        var right = Read(rows.Single(r => r.side == "Right").chains[0]);
        // rule 1: the 60 mm bottom plate keeps one face, so its own thickness is not a position
        // the outline of this fixture is a rectangle, so its top-left vertex (1453.5) is a Left position too:
        // Left and Right then carry the same positions and rule 7 prints Left and drops Right
        Assert.Equal(new[] { 45d, 2467, 220 }, left.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Empty(right.RootElement.GetProperty("segments").EnumerateArray());
        Assert.Contains("covered by Left", right.RootElement.GetProperty("note").GetString());
        Assert.Equal("overall", Read(rows.Single(r => r.side == "Bottom").chains[1]).RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void PanelPreviewNeverInventsPointIdsWhenARequiredSupportIsMissing()
    {
        var left = Part(1, 0, 60, 0, 500);
        var right = Part(2, 100, 160, 0, 500);
        var boundary = Rectangle("boundary", 0, 160, 0, 500);
        var group = new GeometryGroup("gapped-wall", [boundary], [left, right]);
        CalcDimensionChains.Apply(group);
        var unrelated = new GeometryGroup("unrelated", [Rectangle("outline", 0, 160, 0, 500)],
            [Part(99, 0, 160, 0, 500)]);
        CalcDimensionChains.Apply(unrelated);
        var catalog = DimensionPointCatalog.Build(unrelated.DimensionChains!);

        var preview = TimberPanelChainPreview.Build(catalog, group, [1, 2], 3, () => null);
        var rows = preview.Rows;
        var bottom = Read(rows.Single(r => r.side == "Bottom").chains[0]);
        Assert.True(bottom.RootElement.GetProperty("incomplete").GetBoolean());
        Assert.NotEmpty(preview.UnlocatedModelIds);
    }

    [Fact]
    public void CompleteContactSearchFallsBackToGeometryForAnUnconfirmedDoublePost()
    {
        var left = Part(1, 0, 60, 0, 500);
        var right = Part(2, 60, 120, 0, 500);
        var boundary = Rectangle("boundary", 0, 120, 0, 500);
        var group = new GeometryGroup("double-post", [boundary], [left, right]);
        CalcDimensionChains.Apply(group);
        var catalog = DimensionPointCatalog.Build(group.DimensionChains!);
        var completeNoContact = new ViewContactCandidatePointsResult(7, [], [], [], [], searchComplete: true);

        var preview = TimberPanelChainPreview.Build(catalog, group, [1, 2], 3, () => completeNoContact);
        var row = preview.Single(r => r.side == "Bottom");
        var chain = Read(row.chains[0]);

        Assert.Equal(new[] { 120d }, chain.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal("complete-contact-check-geometry-fallback", preview.ContactStatus);
        Assert.Contains(preview.ContactFallbackPairs, pair => pair.SequenceEqual(new[] { 1, 2 }));
    }

    [Fact]
    public void EveryVertexOfAScaleneTrapezoidPanelIsAPositionOfAChain()
    {
        // bottom 0..2847 at y -2234.6, left top (0; 976.3), right top (2847; 2243.9): a scalene trapezoid
        var boundary = Polygon("boundary", (0, -2234.6), (2847, -2234.6), (2847, 2243.9), (0, 976.3));
        var studs = new[] {
            Part(1, 0, 60, -2234.6, 976.3), Part(2, 600, 660, -2234.6, 1300), Part(3, 1800, 1860, -2234.6, 1900),
            Part(4, 2787, 2847, -2234.6, 2243.9), Part(5, 60, 2787, -2234.6, -2174.6)
        };
        var group = new GeometryGroup("trapezoid", [boundary], studs);
        CalcDimensionChains.Apply(group);
        var catalog = DimensionPointCatalog.Build(group.DimensionChains!);

        var preview = TimberPanelChainPreview.Build(catalog, group, studs.Select(p => p.ModelId!.Value).ToArray(), 3, () => null);
        double[] Ys(string side) => Read(preview.Rows.Single(r => r.side == side).chains[0]).RootElement
            .GetProperty("points").EnumerateArray().Select(p => p.GetProperty("pointId").GetString()!)
            .Select(id => Math.Round(catalog.AllPoints.Single(q => q.Id == id).Y, 1)).ToArray();
        Assert.Contains(976.3, Ys("Left"));
        Assert.Contains(2243.9, Ys("Right"));
        Assert.Contains(-2234.6, Ys("Left"));
        Assert.Contains(-2234.6, Ys("Right"));
    }

    private static GeometryGroupShape Polygon(string id, params (double X, double Y)[] points) =>
        new(id, RegionFlattener.Flatten(points.Select(p => new Vec3(p.X, p.Y, 0)).ToList()));

    private static GeometryGroupShape Part(int id, double minX, double maxX, double minY, double maxY) =>
        Rectangle($"part:{id}", minX, maxX, minY, maxY, id);

    private static GeometryGroupShape Rectangle(string id, double minX, double maxX, double minY, double maxY, int? modelId = null) =>
        new(id, RegionFlattener.Flatten(new[] { new Vec3(minX, minY, 0), new Vec3(maxX, minY, 0),
            new Vec3(maxX, maxY, 0), new Vec3(minX, maxY, 0) }.ToList()), modelId: modelId);

    private static JsonDocument Read(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value));
}
