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

        var rows = TimberPanelChainPreview.Build(catalog, group, parts.Select(p => p.ModelId!.Value).ToArray(), 3, () => null);
        var bottom = Read(rows.Single(r => r.side == "Bottom").chains[0]);
        Assert.Equal(new[] { 120d, 427.5, 625, 625, 625, 535, 120 }, bottom.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Empty(bottom.RootElement.GetProperty("unlocatedModelIds").EnumerateArray());

        var left = Read(rows.Single(r => r.side == "Left").chains[0]);
        var right = Read(rows.Single(r => r.side == "Right").chains[0]);
        Assert.Equal(new[] { 45d, 60, 2227, 180 }, left.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new[] { 45d, 60, 2227, 180, 220 }, right.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
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

        var rows = TimberPanelChainPreview.Build(catalog, group, [1, 2], 3, () => null);
        var bottom = Read(rows.Single(r => r.side == "Bottom").chains[0]);
        Assert.True(bottom.RootElement.GetProperty("incomplete").GetBoolean());
        Assert.NotEmpty(bottom.RootElement.GetProperty("unlocatedModelIds").EnumerateArray());
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

        var row = TimberPanelChainPreview.Build(catalog, group, [1, 2], 3, () => completeNoContact)
            .Single(r => r.side == "Bottom");
        var chain = Read(row.chains[0]);

        Assert.Equal(new[] { 120d }, chain.RootElement.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal("complete-contact-check-geometry-fallback", chain.RootElement.GetProperty("contactStatus").GetString());
        Assert.Contains(chain.RootElement.GetProperty("contactFallbackPairs").EnumerateArray(), pair =>
            pair.EnumerateArray().Select(id => id.GetInt32()).SequenceEqual(new[] { 1, 2 }));
    }

    private static GeometryGroupShape Part(int id, double minX, double maxX, double minY, double maxY) =>
        Rectangle($"part:{id}", minX, maxX, minY, maxY, id);

    private static GeometryGroupShape Rectangle(string id, double minX, double maxX, double minY, double maxY, int? modelId = null) =>
        new(id, RegionFlattener.Flatten(new[] { new Vec3(minX, minY, 0), new Vec3(maxX, minY, 0),
            new Vec3(maxX, maxY, 0), new Vec3(minX, maxY, 0) }.ToList()), modelId: modelId);

    private static JsonDocument Read(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value));
}
