using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class BoltChainComponentTests
{
    [Fact]
    public void MainPartBuilderReusesFullBoltCoverageInsteadOfCreatingAnOrphanCrossChain()
    {
        var main = Grid(41, 10);
        var plate = Grid(44, 11);
        var contours = Contours();
        var settings = new BoltChainSettings();
        // Build the alternatives separately, before coincidence links are applied.
        var result = BoltChainCandidates.Build(Snapshot(main).Answer, [10], [10], contours, (_, _, _) => true,
            new BoltChainSettings(positionBoltsTo: BoltPositionTo.None), lineGap: 80).ToList();
        result.AddRange(BoltChainCandidates.Build(Snapshot(plate).Answer, [11], [10], contours, (_, _, _) => true,
            new BoltChainSettings(positionBoltsTo: BoltPositionTo.None), lineGap: 80));
        var covering = Assert.Single(result, chain => chain.MainPart && chain.Kind == BoltChainCandidate.PartKind && chain.Axis == 1);
        Assert.Equal(4, covering.SourcePositions.Count);
        Assert.Contains(plate.Positions, source => !covering.Points.Any(point =>
            point.X == source.Point[0] && point.Y == source.Point[1]));
        var groups = Groups(Snapshot(main, plate), contours, settings);
        var position = BoltMainPartChainBuilder.BuildPosition(groups, [10], contours, [0, 0, 0], [900, 0, 0],
            settings, groups.ToDictionary(group => group.Id, group => group.Geometry!), 80, 0.01)!;

        BoltMainPartChainBuilder.AddAcross(result, position, groups, contours,
            groups.ToDictionary(group => group.Id, group => group.Geometry!), 80, 0.01);

        Assert.DoesNotContain(result, chain => chain.MainPart && chain.Kind == BoltChainCandidate.PartKind
            && chain.Axis == 1 && chain.GroupIds.Contains(44));
        var own = Assert.Single(result, chain => !chain.MainPart && chain.Kind == BoltChainCandidate.PartKind && chain.Axis == 1);
        Assert.Equal(covering.Key, own.OverMainPartChain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoincidentGridsProduceOneCrossDimensionOrKeepThePlateWhenMainDimensionsAreDisabled(bool disableMain)
    {
        var beam = DimensionChainPreviewTests.Solid(10, 0, 900, -100, 100);
        beam.StartPoint = [0, 0, 0];
        beam.EndPoint = [900, 0, 0];
        var context = DimensionChainPreviewTests.Framed("FrontView", Snapshot(Grid(41, 10), Grid(44, 11)), null, beam,
            DimensionChainPreviewTests.Solid(11, 480, 560, -95, 95));
        var chains = context.BoltChains(boltSettings: new BoltChainSettings(mainPartInternal:
            disableMain ? BoltInternalDimensions.None : BoltInternalDimensions.All));
        var cross = Assert.Single(chains, chain => chain.Create && chain.Decision.Candidate.Kind == BoltChainCandidate.PartKind
            && chain.Decision.Candidate.Axis == 1);
        Assert.Equal(disableMain ? 11 : 10, cross.Decision.Candidate.PartId);
        Assert.DoesNotContain(chains, chain => chain.Create && chain.Decision.Candidate.MainPart
            && chain.Decision.Candidate.Kind == BoltChainCandidate.PartKind && chain.Decision.Candidate.GroupIds.Contains(44));
    }

    [Fact]
    public void MergePlannerDeduplicatesTwinsAndKeepsFarCrossChainsSeparate()
    {
        var first = Grid(41, 10);
        var twin = Grid(42, 10);
        var far = Grid(43, 10);
        foreach (var position in far.Positions) position.Point[0] += 300;
        far.FirstPosition[0] += 300;
        far.SecondPosition[0] += 300;
        var settings = new BoltChainSettings();
        var groups = Groups(Snapshot(first, twin, far), Contours(), settings);

        var (twins, units) = BoltGroupMergePlanner.Build(groups, BoltGroupMerge.SameHole, 0.01, 80);

        Assert.Equal(41, twins[42]);
        Assert.Equal(units[0][41], units[0][43]);
        Assert.NotEqual(units[1][41], units[1][43]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void PlacementClearsAProtrudingPartEvenWhenItOverlapsTheMainPart(int sign)
    {
        var contours = Contours();
        contours[11] = [new OutlineTreeNodeResult {
            Polygon = [[800, sign * -50d], [850, sign * -50d], [850, sign * 300d], [800, sign * 300d]]
        }];
        var points = new[] { new[] { 0d, sign * 100d }, new[] { 900d, sign * 100d } };
        var placement = BoltChainPlacement.Placement(contours, 10, 0, 0, 80, 0.01, sign, skipUnder: false);
        Assert.Null(placement.Refusal);
        Assert.Equal(380, sign * points[0][1] + placement.Offset(points) + 80, 6);
    }

    [Fact]
    public void RepresentativesClusterFromTheFirstCoordinateAndRespectTraversalAndLineSide()
    {
        var bolts = new[] { new[] { 0d, -10d }, new[] { 0.009, 10d }, new[] { 0.018, 0d } };
        var forward = BoltChainGeometry.Representatives(bolts, 0, 1, 0.01);
        Assert.Equal(new[] { bolts[1], bolts[2] }, forward);
        var reversed = BoltChainGeometry.Representatives(bolts, 0, -1, 0.01, forward: -1);
        Assert.Equal(new[] { bolts[2], bolts[0] }, reversed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedSourcePrecisionDoesNotDependOnWhetherTheInputIsAnArrayOrAList(bool useList)
    {
        const double x = 20.123456789;
        var position = new BoltPartChainPreview.ProjectedPosition([x, 30.654321, 0], [new(42, 3)]);
        IEnumerable<BoltPartChainPreview.ProjectedPosition> positions = useList
            ? new List<BoltPartChainPreview.ProjectedPosition> { position } : new[] { position };
        var answer = ViewDimensionContext.Freeze(new { sources = BoltChainSources.Captured(positions).Project() }, display: true);
        var source = answer.GetProperty("sources")[0];
        Assert.Equal(x, source.GetProperty("point")[0].GetDouble());
        Assert.Equal(42, source.GetProperty("sources")[0].GetProperty("boltGroupId").GetInt32());
        Assert.Equal(3, source.GetProperty("sources")[0].GetProperty("index").GetInt32());
    }

    [Fact]
    public void PointAndSpacingSourcesKeepTheirDifferentPreviewShapes()
    {
        var points = JsonSerializer.SerializeToElement(BoltChainSources.Points([new[] { 12.5, 30d }]).Project());
        Assert.Equal(12.5, points[0].GetProperty("point")[0].GetDouble());
        var indices = JsonSerializer.SerializeToElement(BoltChainSources.Indices([new[] { 1, 3 }]).Project());
        Assert.Equal(new[] { 1, 3 }, indices[0].EnumerateArray().Select(index => index.GetInt32()).ToArray());
        Assert.Empty(JsonSerializer.SerializeToElement(BoltChainSources.Empty.Project()).EnumerateArray());
    }

    private static BoltGroupGeometry Grid(int id, int partId) => new() {
        ModelId = id, Shape = "BoltArray", Axis = [0, 0, 1], PartToBeBoltedId = partId,
        FirstPosition = [500, -40, 0], SecondPosition = [540, -40, 0],
        Positions = [new() { Index = 0, Point = [500, -40, 0] }, new() { Index = 1, Point = [540, -40, 0] },
            new() { Index = 2, Point = [500, 40, 0] }, new() { Index = 3, Point = [540, 40, 0] }]
    };

    private static ViewBoltGeometrySnapshot Snapshot(params BoltGroupGeometry[] groups) =>
        ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = groups.ToList()
        }, new DepthBox(-1000, -1000, -1000, 1000, 1000, 1000), point => point, true);

    private static BoltChainGroup[] Groups(ViewBoltGeometrySnapshot snapshot,
        IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>> contours, BoltChainSettings settings) =>
        BoltDimensionChainPreview.BuildGroups(snapshot.Answer, [10, 11],
            [DimensionChainSide.Top, DimensionChainSide.Bottom, DimensionChainSide.Left, DimensionChainSide.Right],
            contours, 0.01, settings).Select(group => BoltChainGroup.Read(group, [10])).ToArray();

    private static Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>> Contours() => new() {
        [10] = [new OutlineTreeNodeResult { Polygon = [[0, -100], [900, -100], [900, 100], [0, 100]] }],
        [11] = [new OutlineTreeNodeResult { Polygon = [[480, -95], [560, -95], [560, 95], [480, 95]] }]
    };
}
