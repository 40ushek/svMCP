using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Choosing the parts an overall is measured over, and saying what the choice did not know.
/// The silence is the danger here: a short overall looks exactly like a correct one.
/// </summary>
public sealed class DefiningPartsTests
{
    private static PartInView Part(int id, string mark, PartRole role, bool classified = true) => new()
    {
        ModelId = id,
        PartPos = mark,
        BboxMin = [0, 0, 0],
        BboxMax = [100, 100, 0],
        Role = classified
            ? new PartRoleResult(role, "rule", "reason")
            : PartRoleResult.Unclassified
    };

    [Fact]
    public void OnlyDefiningPartsAreMeasuredOver()
    {
        var selection = DefiningParts.From(
        [
            Part(1, "T-1", PartRole.Defining),
            Part(2, "R-1", PartRole.Attached),
            Part(3, "M-1", PartRole.Ignored)
        ]);

        Assert.Equal([1], selection.Parts.Select(part => part.ModelId));
        Assert.True(selection.IsComplete);
        Assert.Null(selection.Reservation());
    }

    [Fact]
    public void APartNoRuleCoveredIsNamedAndTheResultStopsBeingComplete()
    {
        var selection = DefiningParts.From(
        [
            Part(1, "T-1", PartRole.Defining),
            Part(2, "Z-9", PartRole.Unknown)
        ]);

        // Kept out of the extent, because including it would put an unclassified overhang
        // back in - but said out loud, because leaving it out silently shortens the wall.
        Assert.Equal([1], selection.Parts.Select(part => part.ModelId));
        Assert.False(selection.IsComplete);
        Assert.Contains("Z-9", selection.Reservation());
        Assert.Contains("matched no role rule", selection.Reservation());
    }

    [Fact]
    public void APartNobodyClassifiedIsReportedSeparately()
    {
        // The two want different fixes: one needs a rule, the other needs reading properly.
        var selection = DefiningParts.From(
        [
            Part(1, "T-1", PartRole.Defining),
            Part(2, "B-7", PartRole.Unknown, classified: false)
        ]);

        Assert.Empty(selection.Unknown);
        Assert.Equal([2], selection.Unclassified.Select(part => part.ModelId));
        Assert.Contains("never classified", selection.Reservation());
    }

    [Fact]
    public void WithNothingDefiningItFallsBackToEveryPartAndSaysSo()
    {
        // An extent of zero would switch the overall test off without a word, which is the
        // failure this area keeps producing. Falling back is not the same as knowing.
        var selection = DefiningParts.From(
        [
            Part(1, "R-1", PartRole.Attached),
            Part(2, "M-1", PartRole.Ignored)
        ]);

        Assert.Equal(2, selection.Parts.Count);
        Assert.True(selection.FellBackToEverything);
        Assert.False(selection.IsComplete);
        Assert.Contains("no part is classified as defining", selection.Reservation());
    }

    [Fact]
    public void MaterialTypeDoesNotEnterIntoIt()
    {
        // Insulation reports 5, the same as timber. That is how it got into the extent
        // before, and nothing here looks at it.
        var insulation = Part(2, "R-1", PartRole.Attached);
        insulation.MaterialType = 5;

        var selection = DefiningParts.From([Part(1, "T-1", PartRole.Defining), insulation]);

        Assert.Equal([1], selection.Parts.Select(part => part.ModelId));
    }

    [Fact]
    public void ManyUnknownsAreSummarisedRatherThanListedInFull()
    {
        var parts = new List<PartInView> { Part(1, "T-1", PartRole.Defining) };
        for (var i = 0; i < 8; i++)
            parts.Add(Part(100 + i, $"Z-{i}", PartRole.Unknown));

        var reservation = DefiningParts.From(parts).Reservation();

        Assert.Contains("8 part(s) matched no role rule", reservation);
        Assert.Contains("...", reservation);
    }
}
