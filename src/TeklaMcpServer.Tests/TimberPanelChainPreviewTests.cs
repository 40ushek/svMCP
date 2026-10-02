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
        var top = Read(rows.Single(r => r.side == "Top").chains[0]);
        Assert.Empty(top.RootElement.GetProperty("segments").EnumerateArray());
        Assert.Contains("covered by Bottom", top.RootElement.GetProperty("note").GetString());
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
        var width = Read(rows.Single(r => r.side == "Bottom").chains[1]).RootElement;
        Assert.Equal(3077.5, width.GetProperty("segments")[0].GetDouble());
        var height = Read(rows.Single(r => r.side == "Right").chains[1]).RootElement;
        Assert.Equal("overall", height.GetProperty("kind").GetString());
        Assert.False(height.TryGetProperty("row", out _));
        Assert.Equal(2732d, height.GetProperty("segments")[0].GetDouble());
        var heightPoints = height.GetProperty("pointIds").EnumerateArray()
            .Select(id => catalog.AllPoints.Single(p => p.Id == id.GetString())).ToArray();
        Assert.Equal(-1278.5, heightPoints[0].Y);
        Assert.Equal(1453.5, heightPoints[1].Y);

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
        Assert.Contains("No selected support for model IDs:",
            bottom.RootElement.GetProperty("incompleteReason").GetString());
        Assert.NotEmpty(bottom.RootElement.GetProperty("missingSupportModelIds").EnumerateArray());
        Assert.NotEmpty(preview.UnlocatedModelIds);
    }

    [Fact]
    public void CoincidentHorizontalMemberLevelsShareOneChainMarkWithoutBeingIncomplete()
    {
        var parts = new[]
        {
            Part(1, 0, 60, 0, 500),
            Part(2, 140, 200, 0, 500),
            Part(3, 60, 100, 200, 220),
            Part(4, 100, 140, 200, 220)
        };
        var boundary = Rectangle("boundary", 0, 200, 0, 500);
        var group = new GeometryGroup("shared-levels", [boundary], parts);
        CalcDimensionChains.Apply(group);
        var catalog = DimensionPointCatalog.Build(group.DimensionChains!);

        var preview = TimberPanelChainPreview.Build(catalog, group, [1, 2, 3, 4], 3, () => null);
        var left = Read(preview.Rows.Single(r => r.side == "Left").chains[0]).RootElement;

        Assert.False(left.GetProperty("incomplete").GetBoolean());
        Assert.Equal(new[] { 200d, 300d }, left.GetProperty("segments").EnumerateArray().Select(x => x.GetDouble()));
        Assert.DoesNotContain(3, preview.UnlocatedModelIds);
        Assert.DoesNotContain(4, preview.UnlocatedModelIds);
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
        Assert.Empty(Read(preview.Single(r => r.side == "Top").chains[0]).RootElement
            .GetProperty("segments").EnumerateArray());
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

    [Fact]
    public void OverallDirectionsCanBeDisabledAndSidesSelected()
    {
        var shape = Part(1, 0, 200, 0, 500);
        var group = new GeometryGroup("rectangle", [shape], [shape]);
        CalcDimensionChains.Apply(group);
        var catalog = DimensionPointCatalog.Build(group.DimensionChains!);
        var context = DimensionRuleContext.FromCatalog(catalog, group.Extent!);
        var settings = new OverallDimensionSettings(horizontalSide: null, verticalSide: DimensionChainSide.Left);
        var result = Assert.Single(new DimensionRuleSet(new OverallDimensionRule(settings)).Calculate(context).Results);
        Assert.Equal(DimensionChainSide.Left, AxisAlignedDimensionRulePreviewAdapter.GetSide(result));
        Assert.Equal(500d, Assert.Single(result.Segments));
        Assert.Equal(0d, result.Points[0].Y);
        Assert.Equal(500d, result.Points[1].Y);
        Assert.Empty(new OverallDimensionRule(new OverallDimensionSettings(null, null)).Calculate(context).Results);

        var top = Assert.Single(new OverallDimensionRule(
            new OverallDimensionSettings(DimensionChainSide.Top, null)).Calculate(context).Results);
        Assert.Equal(DimensionChainSide.Top, AxisAlignedDimensionRulePreviewAdapter.GetSide(top));
        Assert.Equal(200d, Assert.Single(top.Segments));

        var preview = TimberPanelChainPreview.Build(catalog, group, [1], 3, () => null,
            new OverallDimensionSettings(null, null));
        Assert.All(preview.Rows, row => Assert.Single(row.chains));
    }

    [Fact]
    public void OverallHeightRequiresFullPanelExtremesOnTheSelectedSide()
    {
        var boundary = Polygon("boundary", (0, 0), (200, 0), (200, 600), (0, 400));
        var group = new GeometryGroup("sloped", [boundary], [Part(1, 0, 60, 0, 400), Part(2, 140, 200, 0, 600)]);
        CalcDimensionChains.Apply(group);
        var context = DimensionRuleContext.FromCatalog(DimensionPointCatalog.Build(group.DimensionChains!), group.Extent!);
        var right = Assert.Single(new OverallDimensionRule(new OverallDimensionSettings(null, DimensionChainSide.Right)).Calculate(context).Results);
        Assert.Null(right.Note);
        Assert.Equal(600d, Assert.Single(right.Segments));
        var left = Assert.Single(new OverallDimensionRule(new OverallDimensionSettings(null, DimensionChainSide.Left)).Calculate(context).Results);
        // This catalog also exposes the high corner on Left; it must still use full height.
        Assert.Null(left.Note);
        Assert.Equal(600d, Assert.Single(left.Segments));
        var shortShape = Part(3, 0, 200, 0, 400);
        var shortGroup = new GeometryGroup("missing-top", [shortShape], [shortShape]);
        CalcDimensionChains.Apply(shortGroup);
        var incompleteContext = DimensionRuleContext.FromCatalog(
            DimensionPointCatalog.Build(shortGroup.DimensionChains!), group.Extent!);
        var missing = Assert.Single(new OverallDimensionRule(
            new OverallDimensionSettings(null, DimensionChainSide.Right)).Calculate(incompleteContext).Results);
        Assert.NotNull(missing.Note);
        Assert.Empty(missing.Points);
        Assert.Empty(missing.Segments);
    }

    [Fact]
    public void OverallSettingsRejectSidesForTheWrongAxisAndInvalidTolerance()
    {
        Assert.Throws<ArgumentException>(() => new OverallDimensionSettings(DimensionChainSide.Left));
        Assert.Throws<ArgumentException>(() => new OverallDimensionSettings(null, DimensionChainSide.Top));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverallDimensionSettings(positionTolerance: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverallDimensionSettings(positionTolerance: -1));
    }

    [Fact]
    public void RuleResultSupportsInclinedDirectionAndPlacementIndependentOfCardinalSides()
    {
        var direction = new DimensionDirection(3, 4);
        var placement = new OutsideOutlineDimensionPlacement(new DimensionDirection(-4, 3), 1);
        var result = new DimensionRuleResult(direction, placement, "future-inclined",
            [new DimensionRulePoint("p1", 10, 20, [])], segments: [25]);

        Assert.Equal(.6, result.Direction.X, 12);
        Assert.Equal(.8, result.Direction.Y, 12);
        Assert.Same(placement, result.Placement);
        Assert.Equal(1, placement.Row);
        Assert.Throws<NotSupportedException>(() => AxisAlignedDimensionRulePreviewAdapter.GetSide(result));
    }

    private sealed class FutureInteriorPlacement : DimensionLinePlacement { }

    [Fact]
    public void PreviewAdapterRejectsUnsupportedPlacementWithAnExplicitReason()
    {
        var result = new DimensionRuleResult(new DimensionDirection(1, 0),
            new FutureInteriorPlacement(), "future-interior", []);
        var error = Assert.Throws<NotSupportedException>(() => AxisAlignedDimensionRulePreviewAdapter.GetRow(result));
        Assert.Contains("outside-outline", error.Message);
        Assert.Throws<NotSupportedException>(() => AxisAlignedDimensionRulePreviewAdapter.ToPreview(result));
        Assert.Throws<ArgumentNullException>(() => new DimensionRuleResult(new DimensionDirection(1, 0), null!, "invalid", []));
    }

    [Fact]
    public void RulePointSourceAcceptsFutureObjectKindsWithoutChangingTheRuleContract()
    {
        var sources = new[] {
            new DimensionPointSource("bolt", 10, "bolt:10", featureKind: "center"),
            new DimensionPointSource("bolt-group", 11, "bolt-group:11"),
            new DimensionPointSource("rebar", 12, "rebar:12", pointIndex: 1),
            new DimensionPointSource("rebar-group", 13, "rebar-group:13")
        };
        var point = new DimensionRulePoint("future", 1, 2, sources);
        var context = new DimensionRuleContext([point]);

        Assert.Same(point, context.GetPoint("future"));
        Assert.Equal(new[] { "bolt", "bolt-group", "rebar", "rebar-group" },
            point.Sources.Select(source => source.ObjectKind));
        Assert.Null(context.AxisAlignedGeometry);
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
