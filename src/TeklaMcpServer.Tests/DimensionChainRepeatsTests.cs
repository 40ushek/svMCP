using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;
using static TeklaMcpServer.Api.Drawing.DimensionChainRepeats;

namespace TeklaMcpServer.Tests;

public sealed class DimensionChainRepeatsTests
{
    private static Placed OnSheet(int id, int view, string type, params double[] segments) => new(id, view, type, segments, horizontal: true);
    private static Placed Vertical(int id, int view, string type, params double[] segments) => new(id, view, type, segments, horizontal: false);

    [Fact]
    public void TopRepeatsBottomAndRightRepeatsLeftOfTheSameKind()
    {
        var chains = new[] {
            new Chain("Top-overall", "Top", "overall", [2962.69]),
            new Chain("Bottom-overall", "Bottom", "overall", [2962.699]),
            new Chain("Top-location", "Top", "location", [100, 200]),
            new Chain("Bottom-location", "Bottom", "location", [200, 100]),
            new Chain("Right-chain", "Right", "chain", [70, 160]),
            new Chain("Left-chain", "Left", "chain", [70, 160.4])
        };

        var repeats = Find(7, "SectionView",chains, []);

        Assert.Equal(new[] { ("Top-overall", "Bottom-overall"), ("Right-chain", "Left-chain") },
            repeats.Select(r => (r.Key, r.Of!)).ToArray());
        Assert.All(repeats, r => Assert.Equal("sameView", r.Reason));
    }

    [Fact]
    public void AnOverallRepeatsAOneSegmentLocationChainOpposite()
    {
        // M.160 bottom view: the left location chain is the 180 itself, the right overall repeats it.
        var chains = new[] {
            new Chain("Left-location", "Left", "location", [180]),
            new Chain("Right-location", "Right", "location", [5, 175]),
            new Chain("Right-overall", "Right", "overall", [180])
        };

        var repeat = Assert.Single(Find(7, "SectionView", chains, []));

        Assert.Equal(("Right-overall", "sameView", "Left-location"), (repeat.Key, repeat.Reason, repeat.Of!));
    }

    [Fact]
    public void LocalAndInclinedChainsAreNeverComparedInsideOneView()
    {
        var chains = new[] {
            new Chain("Local-1", "Local", "local", [199.3]),
            new Chain("Local-2", "Local", "local", [199.3]),
            new Chain("Inclined-3", "Inclined", "inclined", [25, 339])
        };

        Assert.Empty(Find(7, "FrontView",chains, []));
    }

    [Fact]
    public void OverallRepeatsAOneSegmentDimensionOnAnotherView()
    {
        // M.52: the top view's overall 9047.6 is already on the sheet from the front view.
        var chains = new[] { new Chain("Top-overall", "Top", "overall", [9047.6]) };
        var sheet = new[] { OnSheet(4104, 3936, "FrontView", 9047.6), OnSheet(9761, 2711, "TopView", 9047.6) };

        var repeat = Assert.Single(Find(2711, "TopView",chains, sheet));

        Assert.Equal(("overallOnSheet", 4104, 3936), (repeat.Reason, repeat.DimensionId!.Value, repeat.ViewId!.Value));
    }

    [Fact]
    public void OverallRepeatsOnlyTheSameMeasure()
    {
        // A 200x200 tube: the front view's height is not the top view's width, nor any 200 elsewhere.
        var chains = new[] { new Chain("Left-overall", "Left", "overall", [200]) };
        var notTheHeight = new[] { Vertical(1, 2711, "TopView", 200), OnSheet(2, 2711, "TopView", 200),
            Vertical(3, 500, "SectionView", 200), new Placed(4, 600, "BackView", [200]) };

        Assert.Empty(Find(3936, "FrontView",chains, notTheHeight));

        var repeat = Assert.Single(Find(3936, "FrontView",chains, [.. notTheHeight, Vertical(5, 600, "BackView", 200)]));
        Assert.Equal(("overallOnSheet", 5), (repeat.Reason, repeat.DimensionId!.Value));
    }

    [Fact]
    public void SectionKeepsItsOverallAndDoesNotReadTheSheet()
    {
        var reads = 0;
        IReadOnlyList<Placed> Sheet() { reads++; return [OnSheet(1, 2, "FrontView", 300)]; }

        Assert.Empty(Find(9, "SectionView",[new Chain("Top-overall", "Top", "overall", [300])], Sheet));
        Assert.Empty(Find(9, "EndView",[new Chain("Left-overall", "Left", "overall", [300])], Sheet));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void BottomViewChainMirrorsTheTopViewInEitherOrder()
    {
        var chains = new[] {
            new Chain("Right-location", "Right", "location", [200, 106]),
            new Chain("Local-5", "Local", "local", [30]),
            new Chain("Top-location", "Top", "location", [40, 50])
        };
        var sheet = new[] { OnSheet(11, 2711, "TopView", 106, 200), OnSheet(12, 2711, "TopView", 30),
            OnSheet(13, 3936, "FrontView", 40, 50) };

        var repeats = Find(1949, "BottomView",chains, sheet);

        Assert.Equal(new[] { "Right-location", "Local-5" }, repeats.Select(r => r.Key).ToArray());
        Assert.All(repeats, r => Assert.Equal("mirror", r.Reason));
    }

    [Fact]
    public void SectionKeepsAChainMatchingAnotherView()
    {
        var chains = new[] { new Chain("Top-chain", "Top", "chain", [80, 140, 80]) };
        var sheet = new[] { OnSheet(1, 2, "FrontView", 80, 140, 80) };

        Assert.Empty(Find(9, "SectionView",chains, sheet));
    }

    [Fact]
    public void SheetIsReadOnlyWhenAChainCouldRepeatIt()
    {
        var reads = 0;
        IReadOnlyList<Placed> Sheet() { reads++; return []; }
        var section = new[] { new Chain("Top-chain", "Top", "chain", [80, 140, 80]) };
        var overallRepeatedOnItsView = new[] {
            new Chain("Top-overall", "Top", "overall", [500]), new Chain("Bottom-overall", "Bottom", "overall", [500]) };

        Find(9, "SectionView",section, Sheet);
        Assert.Equal(0, reads);
        Find(9, "FrontView",[overallRepeatedOnItsView[0]], Sheet);
        Assert.Equal(1, reads);
        Find(9, "BottomView",section, Sheet);
        Assert.Equal(2, reads);
        Find(9, "FrontView",overallRepeatedOnItsView, Sheet);
        Assert.Equal(3, reads); // Bottom-overall itself may repeat the sheet
    }

    [Fact]
    public void ItsOwnViewIsNotTheSheet()
    {
        var chains = new[] { new Chain("Bottom-overall", "Bottom", "overall", [9047.6]) };

        Assert.Empty(Find(3936, "FrontView",chains, [OnSheet(4104, 3936, "FrontView", 9047.6)]));
    }

    [Fact]
    public void PreviewGivesOnlyCompleteChainsWithTheirRowSide()
    {
        using var doc = JsonDocument.Parse("""
            {"chainPreview":[
              {"side":"Top","chains":[{"key":"Top-location","kind":"location","segments":[1,2],"incomplete":false},
                                      {"key":"Top-overall","kind":"overall","segments":[],"incomplete":true}]},
              {"side":"Local","chains":[{"key":"Local-7","kind":"local","segments":[30],"incomplete":false}]}]}
            """);

        var chains = FromPreview(doc.RootElement);

        Assert.Equal(new[] { ("Top-location", "Top"), ("Local-7", "Local") }, chains.Select(c => (c.Key, c.Side)).ToArray());
    }

    [Fact]
    public void SheetSegmentsAreStepsAlongTheMeasuringAxis()
    {
        var group = new DimensionGroupInfo { ViewId = 5, ViewType = "TopView", DimensionType = "Horizontal" };
        group.Items.Add(new DimensionItemInfo { Id = 3, PointList = [
            new DrawingPointInfo { X = 300, Y = 9 }, new DrawingPointInfo { X = 0, Y = 7 }, new DrawingPointInfo { X = 100, Y = 8 } ] });

        var placed = Assert.Single(FromSheet(new GetDimensionsResult { Groups = [group] }));

        Assert.Equal((3, 5, "TopView"), (placed.DimensionId, placed.ViewId, placed.ViewType));
        Assert.Equal(new[] { 100d, 200d }, placed.Segments);
    }

    [Theory]
    [InlineData("Unknown", 1, 0, true)]   // a hand-placed horizontal dimension of unresolved type
    [InlineData("Free", 0, 1, false)]
    public void SheetSegmentsFollowTheDimensionLineWhateverTheType(string type, double dx, double dy, bool horizontal)
    {
        var group = new DimensionGroupInfo { ViewId = 5, ViewType = "TopView", DimensionType = type,
            Direction = new DrawingVectorInfo { X = dx, Y = dy } };
        group.Items.Add(new DimensionItemInfo { Id = 3, PointList = [
            new DrawingPointInfo { X = 300 * dx + 9 * dy, Y = 300 * dy + 9 * dx }, new DrawingPointInfo { X = 7 * dy, Y = 7 * dx },
            new DrawingPointInfo { X = 100 * dx + 8 * dy, Y = 100 * dy + 8 * dx } ] });

        var placed = Assert.Single(FromSheet(new GetDimensionsResult { Groups = [group] }));

        Assert.Equal(new[] { 100d, 200d }, placed.Segments);
        Assert.Equal(horizontal, placed.Horizontal);
    }

    [Fact]
    public void InclinedSheetDimensionMeasuresAlongItsLineAndIsNoOverall()
    {
        var group = new DimensionGroupInfo { ViewId = 5, ViewType = "TopView", DimensionType = "Free",
            Direction = new DrawingVectorInfo { X = 3, Y = 4 } };
        group.Items.Add(new DimensionItemInfo { Id = 3, PointList = [
            new DrawingPointInfo { X = 0, Y = 0 }, new DrawingPointInfo { X = 30, Y = 40 } ] });

        var placed = Assert.Single(FromSheet(new GetDimensionsResult { Groups = [group] }));

        Assert.Equal(new[] { 50d }, placed.Segments);
        Assert.Null(placed.Horizontal);
    }

    [Fact]
    public void RepeatsAreAddedToTheAnswerWithoutTouchingTheChains()
    {
        using var doc = JsonDocument.Parse("""{"viewId":7,"chainPreview":[{"side":"Top","chains":[]}]}""");

        var answer = ViewDimensionContextProvider.WithRepeats(doc.RootElement,
            [new Repeat("Top-overall", "sameView", "Bottom-overall"), new Repeat("Local-5", "mirror", dimensionId: 12, viewId: 2711)]);

        Assert.Equal(7, answer.GetProperty("viewId").GetInt32());
        Assert.Equal(1, answer.GetProperty("chainPreview").GetArrayLength());
        var rows = answer.GetProperty("repeats").EnumerateArray().ToArray();
        Assert.Equal("Bottom-overall", rows[0].GetProperty("of").GetString());
        Assert.False(rows[0].TryGetProperty("dimensionId", out _));
        Assert.Equal(12, rows[1].GetProperty("dimensionId").GetInt32());
    }
}
