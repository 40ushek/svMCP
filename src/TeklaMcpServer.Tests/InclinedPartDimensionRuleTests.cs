using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// A column (10) cut on a slope at its top, with a head plate (11) lying on the cut: the plate
/// overhangs the column on the left and stops 8 short of its corner on the right. Modelled on
/// M.81, where the user dimensioned the plate along itself as 238 / 8.
/// </summary>
public sealed class InclinedPartDimensionRuleTests
{
    private const double Thickness = 15;
    private static readonly double CutLength = Math.Sqrt(160 * 160 + 16.8 * 16.8);
    private static readonly (double X, double Y) Along = (160 / CutLength, -16.8 / CutLength);
    private static readonly (double X, double Y) Up = (-Along.Y, Along.X);

    private static (double, double)[] Column => [(-80, 0), (80, 0), (80, 983.2), (-80, 1000)];

    // The plate from `start` to `end`, both measured along the cut from the column's upper left corner.
    private static (double, double)[] Plate(double start, double end)
    {
        (double, double) At(double along, double up) =>
            (-80 + along * Along.X + up * Up.X, 1000 + along * Along.Y + up * Up.Y);
        return [At(start, 0), At(end, 0), At(end, Thickness), At(start, Thickness)];
    }

    [Fact]
    public void HeadPlateOnASlopedCutIsDimensionedAlongItself()
    {
        var result = Assert.Single(Calculate(Plate(-83, CutLength - 8)));

        Assert.Equal(new[] { Math.Round(CutLength + 75, 3), 8d }, result.Segments.ToArray());
        Assert.Equal(Along.X, result.Direction.X, 6);
        Assert.Equal(Along.Y, result.Direction.Y, 6);
        var normal = ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal;
        Assert.True(normal.Y > 0, "the line goes above the plate, away from the column");
        // The plate's ends are taken on its outer face; the last point is the column corner.
        Assert.Equal(new int?[] { 11, 11, 10 }, result.Points.Select(point => point.Sources.Single().ModelId).ToArray());
        Assert.Equal(80, result.Points.Last().X, 6);
        Assert.Equal(983.2, result.Points.Last().Y, 6);
        Assert.Equal(Thickness, (double)result.Evidence["acrossSpan"], 6);
    }

    [Fact]
    public void PlateOverhangingBothColumnCornersIsLocatedByTheColumnLimitsInsideIt()
    {
        var result = Assert.Single(Calculate(Plate(-50, CutLength + 30)));
        Assert.Equal(new[] { 50d, Math.Round(CutLength, 3), 30d }, result.Segments.ToArray());
    }

    [Fact]
    public void TwoEqualPlatesAtOnePlaceKeepOneChain()
    {
        // A head plate on each side of the web: the same contour twice. Each carries the other.
        var contours = new Dictionary<int, IReadOnlyList<(double X, double Y)>> {
            [10] = Column.Select(point => (point.Item1, point.Item2)).ToArray(),
            [11] = Plate(-83, CutLength - 8).Select(point => (point.Item1, point.Item2)).ToArray(),
            [12] = Plate(-83, CutLength - 8).Select(point => (point.Item1, point.Item2)).ToArray()
        };
        var result = Assert.Single(new InclinedPartDimensionRule(new InclinedPartDimensionSettings())
            .Calculate(new DimensionRuleContext([], null, new InclinedPartDimensionInput(contours, 10))).Results);

        Assert.Equal(11, (int)result.Evidence["partId"]);
        Assert.Equal(new[] { 12 }, (int[])result.Evidence["attachedPartIds"]);
        Assert.Equal(new[] { Math.Round(CutLength + 75, 3), 8d }, result.Segments.ToArray());
    }

    [Fact]
    public void SmallPartWeldedOntoTheInclinedPlateIsLocatedAlongIt()
    {
        // Two equal small plates at one place on the head plate (one each side of the web),
        // 20 to 100 along it: the chain reads the place once, between the plate's ends.
        (double, double)[] Small() => Plate(20, 100).Select(point => (point.Item1, point.Item2)).ToArray();
        var contours = new Dictionary<int, IReadOnlyList<(double X, double Y)>> {
            [10] = Column.Select(point => (point.Item1, point.Item2)).ToArray(),
            [11] = Plate(-83, CutLength - 8).Select(point => (point.Item1, point.Item2)).ToArray(),
            [12] = Small(), [13] = Small()
        };
        var result = Assert.Single(new InclinedPartDimensionRule(new InclinedPartDimensionSettings())
            .Calculate(new DimensionRuleContext([], null, new InclinedPartDimensionInput(contours, 10))).Results);

        Assert.Equal(new[] { 103d, 80d, Math.Round(CutLength - 8 - 100, 3), 8d }, result.Segments.ToArray());
        Assert.Equal(new[] { 12, 13 }, (int[])result.Evidence["attachedPartIds"]);
    }

    [Fact]
    public void PlateStandingOnTheInclinedPlateWithItsEndIsCarriedButOneMeetingItAtAnAngleIsNot()
    {
        IReadOnlyList<DimensionRuleResult> Run((double X, double Y)[] other) =>
            new InclinedPartDimensionRule(new InclinedPartDimensionSettings()).Calculate(new DimensionRuleContext([], null,
                new InclinedPartDimensionInput(new Dictionary<int, IReadOnlyList<(double X, double Y)>> {
                    [10] = Column.Select(point => (point.Item1, point.Item2)).ToArray(),
                    [11] = Plate(-83, CutLength - 8).Select(point => (point.Item1, point.Item2)).ToArray(),
                    [12] = other
                }, 10))).Results;
        (double X, double Y) At(double along, double up) =>
            (-80 + along * Along.X + up * Up.X, 1000 + along * Along.Y + up * Up.Y);

        // A 12 thick plate standing square on the head plate's upper face, 60 to 72 along it, 200 tall.
        var standing = Assert.Single(Run([At(60, Thickness), At(72, Thickness), At(72, Thickness + 200), At(60, Thickness + 200)]));
        Assert.Equal(new[] { 143d, 12d, Math.Round(CutLength - 8 - 72, 3), 8d }, standing.Segments.ToArray());

        // A level plate ending at the same place: along the head plate it shows only a projection.
        var level = Assert.Single(Run([(-20, 1030), (-8, 1030), (-8, 1230), (-20, 1230)]));
        Assert.Empty((int[])level.Evidence["attachedPartIds"]);
    }

    [Fact]
    public void PlateSquareToTheViewAxesGetsNoInclinedChain()
    {
        (double, double)[] column = [(-80, 0), (80, 0), (80, 1000), (-80, 1000)];
        (double, double)[] plate = [(-130, 1000), (130, 1000), (130, 1015), (-130, 1015)];
        Assert.Empty(Calculate(plate, column));
    }

    [Fact]
    public void TiltedPartThatIsNotAStraightBarGetsNoInclinedChain()
    {
        // A gusset: one long tilted edge, the others neither along it nor square to it.
        (double, double)[] gusset = [(-80, 1000), (80, 983.2), (60, 1100), (-40, 1050)];
        Assert.Empty(Calculate(gusset));
    }

    [Fact]
    public void PreviewOffersTheInclinedChainAndLeavesThePlateOutOfTheChainAcrossTheColumn()
    {
        var context = Context();
        var rows = context.Query("chain").GetProperty("chainPreview").EnumerateArray().ToArray();

        var inclined = Assert.Single(rows.Single(row => row.GetProperty("side").GetString() == "Inclined")
            .GetProperty("chains").EnumerateArray());
        Assert.Equal("Inclined-11", inclined.GetProperty("key").GetString());
        var segments = inclined.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.Equal(CutLength + 75, segments[0], 1);
        Assert.Equal(8, segments[1], 1);
        Assert.Equal(8 * 10 + Thickness, inclined.GetProperty("distance").GetDouble(), 1);

        var top = rows.Single(row => row.GetProperty("side").GetString() == "Top").GetProperty("chains")
            .EnumerateArray().Single(chain => chain.GetProperty("kind").GetString() == "location");
        Assert.Empty(top.GetProperty("pointIds").EnumerateArray());
        Assert.Contains("tilted", top.GetProperty("note").GetString());

        // A one-side question keeps its one row.
        Assert.Single(context.Query("chain", "Top").GetProperty("chainPreview").EnumerateArray());
    }

    [Fact]
    public void BatchWritesTheInclinedChainWithItsOwnPointsDirectionAndDistance()
    {
        var context = Context();
        var writes = new List<(CreateDimensionRequest Request, double Distance)>();
        var provider = new ViewDimensionContextProvider(() => "drawing-a", (_, _) => context, () => { },
            (request, distance) => { writes.Add((request, distance)); return new() { Created = true, DimensionId = 101 }; },
            readDimensions: _ => new GetDimensionsResult(), validateAttributes: _ => { });
        provider.Get(7);

        var result = provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new BatchDimensionChain { Preview = "Inclined-11" }]
        });

        var write = Assert.Single(writes);
        Assert.Equal(9, write.Request.Points.Length);
        Assert.Equal(80, write.Request.Points[6], 1);
        Assert.Equal(983.2, write.Request.Points[7], 1);
        Assert.Equal(8 * 10 + Thickness, write.Distance, 1);
        var vector = write.Request.Direction.Split(',').Select(value =>
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(Up.X, vector[0], 3);
        Assert.Equal(Up.Y, vector[1], 3);
        Assert.Equal("Inclined-11", Assert.Single(result.Chains).Key);

        Assert.Throws<ArgumentException>(() => provider.CreateBatch(new CreateDimensionsBatchRequest {
            ViewId = 7, ContextId = context.ContextId, RuleSet = "steel",
            Chains = [new BatchDimensionChain { Preview = "Inclined-11", PaperGapMm = 8 }]
        }));
    }

    private static IReadOnlyList<DimensionRuleResult> Calculate((double, double)[] plate, (double, double)[]? column = null)
    {
        var contours = new Dictionary<int, IReadOnlyList<(double X, double Y)>> {
            [10] = (column ?? Column).Select(point => (point.Item1, point.Item2)).ToArray(),
            [11] = plate.Select(point => (point.Item1, point.Item2)).ToArray()
        };
        return new InclinedPartDimensionRule(new InclinedPartDimensionSettings())
            .Calculate(new DimensionRuleContext([], null, new InclinedPartDimensionInput(contours, 10))).Results;
    }

    private static ViewDimensionContext Context()
    {
        var parts = new Dictionary<int, PartSolidGeometryInViewResult> {
            [10] = DimensionChainPreviewTests.Poly(10, Column),
            [11] = DimensionChainPreviewTests.Poly(11, Plate(-83, CutLength - 8))
        };
        var outline = TeklaDrawingAssemblyOutlineApi.Build(7, parts.Keys, new Solids(parts));
        var role = new PartRoleResult(PartRole.Included, "included", "test");
        var structural = new StructuralOutline(outline,
            [new PartRoleInView(10, "P10", "P", role, true), new PartRoleInView(11, "P11", "P", role, false)],
            [], [], [], [], parts.Keys.ToArray());
        return new ViewDimensionContext(7, 10, structural, [], new { drawingGuid = "test" }, new { viewType = "FrontView" });
    }

    private sealed class Solids(Dictionary<int, PartSolidGeometryInViewResult> parts) : IDrawingPartSolidGeometryApi
    {
        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => parts[modelId];
    }
}
