using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewBoltGeometrySnapshotTests
{
    private static readonly DepthBox Box = new(-10, -10, -1, 10, 10, 1);

    [Fact]
    public void DeduplicatesGroupsPreservesIndicesAndFreezesRawGeometry()
    {
        var group = Group();
        var reads = new List<int>();
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [20, 10, 10], partId => {
            reads.Add(partId);
            return new() { Success = true, ViewId = 7, PartId = partId, BoltGroups = [group] };
        }, Box, point => [point[0] - 100, point[1], point[2]], true);
        group.Positions[0].Point[0] = 999;
        group.OtherPartIds.Clear();
        var answer = snapshot.Answer;
        Assert.Equal(new[] { 10, 20 }, reads);
        var row = Assert.Single(answer.GetProperty("boltGroups").EnumerateArray());
        var geometry = row.GetProperty("geometry");
        Assert.Equal(100, geometry.GetProperty("positions")[0].GetProperty("point")[0].GetDouble());
        Assert.Equal(4, geometry.GetProperty("positions")[0].GetProperty("index").GetInt32());
        Assert.Equal(30, geometry.GetProperty("otherPartIds")[0].GetInt32());
        Assert.Equal("Inside", row.GetProperty("restriction")[0].GetProperty("centerState").GetString());
        Assert.Equal("Outside", row.GetProperty("restriction")[1].GetProperty("centerState").GetString());
        Assert.False(answer.GetProperty("selectionComplete").GetBoolean());
        Assert.False(answer.GetProperty("visibilityVerified").GetBoolean());
    }

    [Fact]
    public void ReadFailuresAndIncompletePartSelectionAreNotACompleteEmptyAnswer()
    {
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10, 20], partId => partId == 10
            ? new() { ViewId = 7, PartId = 10, Error = "unread" }
            : throw new InvalidOperationException("query failed"), Box, point => point, false);
        Assert.False(snapshot.Answer.GetProperty("isComplete").GetBoolean());
        Assert.Equal(2, snapshot.Answer.GetProperty("unread").GetArrayLength());
    }

    [Fact]
    public void MissingRestrictionBoxDoesNotAssumeCentersInside()
    {
        var snapshot = Capture(null);
        var row = snapshot.Answer.GetProperty("boltGroups")[0];
        Assert.All(row.GetProperty("restriction").EnumerateArray(), point =>
            Assert.Equal("Unresolved", point.GetProperty("centerState").GetString()));
    }

    [Fact]
    public void BoltsUseTheCapturedContextAndRemainSeparateFromStructuralCompleteness()
    {
        var reads = 0;
        var provider = new ViewDimensionContextProvider(() => "drawing", (view, exclusions) => {
            reads++;
            return ViewDimensionContextTests.Context(view, exclusions, bolts: Capture(Box));
        }, () => { });
        var context = provider.Get(7);
        var first = context.Query("bolts");
        Assert.True(first.GetProperty("isComplete").GetBoolean());
        Assert.False(first.GetProperty("bolts").GetProperty("selectionComplete").GetBoolean());
        Assert.Equal(first.GetRawText(), provider.Get(7).Query("bolts").GetRawText());
        Assert.Equal(1, reads);
        Assert.Equal("bolts,scale", DrawingCommandParsers.NormalizePreviewQuestions(" Bolts,scale "));
        Assert.False(context.Query("all").TryGetProperty("bolts", out _));
        provider.Get(7, refresh: true).Query("bolts");
        Assert.Equal(2, reads);
    }

    [Fact]
    public void NoBoltsIsACompleteGeometryReadButNotVerifiedSelection()
    {
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id =>
            new() { Success = true, ViewId = 7, PartId = id }, Box, point => point, true);
        Assert.True(snapshot.Answer.GetProperty("isComplete").GetBoolean());
        Assert.Empty(snapshot.Answer.GetProperty("boltGroups").EnumerateArray());
    }

    [Theory]
    [InlineData(1.00005, "Inside")]
    [InlineData(1.001, "Outside")]
    public void RestrictionBoundaryUsesTheSharedTolerance(double z, string state)
    {
        var group = Group();
        group.Positions = [new() { Index = 9, Point = [0, 0, z] }];
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, Box, point => point, true);
        Assert.Equal(state, snapshot.Answer.GetProperty("boltGroups")[0]
            .GetProperty("restriction")[0].GetProperty("centerState").GetString());
    }

    [Fact]
    public void InvalidPositionsAreReportedInsteadOfBreakingTheContext()
    {
        var group = Group();
        group.Positions[0].Point[0] = double.NaN;
        var snapshot = ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [group]
        }, Box, point => point, true);
        Assert.False(snapshot.Answer.GetProperty("isComplete").GetBoolean());
        Assert.Single(snapshot.Answer.GetProperty("unread").EnumerateArray());
        Assert.Empty(snapshot.Answer.GetProperty("boltGroups").EnumerateArray());
    }

    private static ViewBoltGeometrySnapshot Capture(DepthBox? box) =>
        ViewBoltGeometrySnapshot.Capture(7, [10], id => new() {
            Success = true, ViewId = 7, PartId = id, BoltGroups = [Group()]
        }, box, point => point, true);

    private static BoltGroupGeometry Group() => new() {
        ModelId = 42, FirstPosition = [100, 0, 0], SecondPosition = [100, 1, 0],
        PartToBeBoltedId = 10, PartToBoltToId = 20, OtherPartIds = [30],
        Positions = [new() { Index = 4, Point = [100, 0, 0] }, new() { Index = 7, Point = [100, 0, 2] }]
    };
}
