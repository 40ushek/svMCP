using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionChainComposerTests
{
    [Fact]
    public void PreservesPointOrderSourcesAndProposalIdentityDeterministically()
    {
        var first = Proposal("a", [Point("datum", 20, 1), Point("end", 10, 2)]);
        var second = Proposal("b", [Point("other-datum", 20, 3), Point("other-end", 10, 4)]);
        var plan = DimensionChainComposer.Compose([first, second]);
        Assert.Same(first, plan.OriginalProposals[0]);
        Assert.Equal(new[] { "datum", "end" }, plan.OriginalProposals[0].Points.Select(p => p.Id));
        Assert.Equal(1, plan.OriginalProposals[0].Points[0].Sources[0].ModelId);
        Assert.All(plan.Decisions, d => Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, d.Kind));
        Assert.Equal("a", plan.Decisions[0].ProposalId);
        Assert.Equal(JsonSerializer.Serialize(plan.Project()),
            JsonSerializer.Serialize(DimensionChainComposer.Compose(plan.OriginalProposals).Project()));
    }

    [Fact]
    public void KeepsOriginalRefusalsAndIncompleteSupportEvidence()
    {
        var refused = Proposal("refused", [], note: "unsupported inclined member");
        var incomplete = Proposal("incomplete", [Point("a", 0, 1), Point("b", 10, 2)],
            evidence: new Dictionary<string, object> { ["incomplete"] = true, ["missingSupportModelIds"] = new[] { 7 } });
        var noEvidence = Proposal("no-evidence", [new DimensionRulePoint("a", 0, 0, []), Point("b", 10, 2)]);
        var plan = DimensionChainComposer.Compose([refused, incomplete, noEvidence]);
        Assert.All(plan.Decisions, d => Assert.Equal(DimensionCompositionDecisionKind.Blocked, d.Kind));
        Assert.Contains("inclined", plan.Decisions[0].Reason);
        Assert.Equal(new[] { 7 }, (int[])plan.OriginalProposals[1].Evidence["missingSupportModelIds"]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(plan.Project()));
        Assert.False(json.RootElement.TryGetProperty("chains", out _));
        Assert.Equal(3, json.RootElement.GetProperty("proposals").GetArrayLength());
        Assert.Equal("No selected support for model IDs: 7.", plan.Decisions[1].Reason);
        Assert.Equal(new[] { 7 }, Assert.Single(plan.Decisions[1].Issues).ModelIds);
        Assert.Equal(new[] { "a" }, Assert.Single(plan.Decisions[2].Issues).PointIds);
    }

    [Fact]
    public void RejectsAmbiguousAndCrossContextAddresses()
    {
        var valid = Proposal("a", [Point("a", 0, 1), Point("b", 10, 2)]);
        Assert.Throws<ArgumentException>(() => DimensionChainComposer.Compose([valid, valid]));
        Assert.Throws<ArgumentException>(() => DimensionChainComposer.Compose([valid,
            valid.WithProposal(new DimensionProposalIdentity("another-context", 1, "b",
                DimensionMeasurementPurpose.Location, DimensionMeasurementReference.Unknown, DimensionReferenceSupport.Unspecified, "Bottom-location"))]));
        Assert.Throws<ArgumentException>(() => DimensionChainComposer.Compose([
            new DimensionRuleResult(valid.Direction, valid.Placement, "location", valid.Points)]));
    }

    [Fact]
    public void ArbitraryDirectionDoesNotNeedFourSideProjection()
    {
        var proposal = Proposal("skew-contract", [Point("a", 0, 1), Point("b", 10, 2)]);
        var skew = new DimensionRuleResult(new DimensionDirection(1, 1), proposal.Placement, "location",
            proposal.Points, proposal: proposal.Proposal);
        var plan = DimensionChainComposer.Compose([skew]);
        Assert.Equal(Math.Sqrt(.5), plan.OriginalProposals[0].Direction.X, 10);
    }

    [Theory]
    [InlineData("overall", "Overall")]
    [InlineData("location", "Location")]
    [InlineData("internal", "Internal")]
    [InlineData("edge", "Edge")]
    [InlineData("check", "Check")]
    [InlineData("future-kind", "Unknown")]
    public void PurposeMappingDoesNotGuessUnknownKinds(string kind, string expected)
    {
        Assert.Equal(expected, DimensionProposalIdentity.PurposeFromKind(kind).ToString());
    }

    [Fact]
    public void ContentIdsSurviveProposalReorderingAndDisplayKeyChanges()
    {
        var first = Proposal("temporary", [Point("a", 0, 1), Point("b", 10, 2)]);
        var second = Proposal("temporary", [Point("a", 0, 1), Point("c", 15, 3)]);
        DimensionRuleResult Bind(DimensionRuleResult p, string key = "Bottom-location") => p.WithProposal(
            DimensionProposalIdentity.Create("snapshot", 7, "panel", key, p));
        var plan = DimensionChainComposer.Compose([Bind(first), Bind(second)]);
        var reversed = DimensionChainComposer.Compose([Bind(second), Bind(first)]);
        Assert.Equal(plan.OriginalProposals[0].Proposal!.ProposalId, reversed.OriginalProposals[1].Proposal!.ProposalId);
        Assert.Equal(plan.OriginalProposals[1].Proposal!.ProposalId, reversed.OriginalProposals[0].Proposal!.ProposalId);
        Assert.NotEqual(plan.OriginalProposals[0].Proposal!.ProposalId, plan.OriginalProposals[1].Proposal!.ProposalId);
        Assert.All(plan.OriginalProposals, p => Assert.Equal("Bottom-location", p.Proposal!.PreviewKey));
        Assert.All(plan.Decisions, d => Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, d.Kind));
        Assert.Equal(Bind(first).Proposal!.ProposalId, Bind(first, "renamed-display-key").Proposal!.ProposalId);
        Assert.Equal("renamed-display-key", Bind(first, "renamed-display-key").Proposal!.PreviewKey);
    }

    [Fact]
    public void ContentIdsIncludeSemanticsDirectionDatumAndSourceEvidence()
    {
        var first = Proposal("temporary", [Point("a", 0, 1), Point("b", 10, 2)]);
        string Id(DimensionRuleResult p, DimensionMeasurementReference reference = DimensionMeasurementReference.Unknown) =>
            DimensionProposalIdentity.Create("snapshot", 7, "panel", "Bottom-location", p, reference).ProposalId;
        var reversed = new DimensionRuleResult(first.Direction, first.Placement, first.Kind, first.Points.Reverse());
        var changedOwner = Proposal("temporary", [Point("a", 0, 99), Point("b", 10, 2)]);
        var changedPurpose = new DimensionRuleResult(first.Direction, first.Placement, "overall", first.Points);
        var changedDirection = new DimensionRuleResult(new DimensionDirection(-1, 0), first.Placement, first.Kind, first.Points);
        Assert.NotEqual(Id(first), Id(reversed));
        Assert.NotEqual(Id(first), Id(changedOwner));
        Assert.NotEqual(Id(first), Id(changedPurpose));
        Assert.NotEqual(Id(first), Id(changedDirection));
        Assert.NotEqual(Id(first), Id(first, DimensionMeasurementReference.MainPart));
    }

    [Fact]
    public void SourceEvidenceOrderingDoesNotChangeContentId()
    {
        var sources = new[] { new DimensionPointSource("part", 1, "c1"), new DimensionPointSource("part", 2, "c2") };
        var first = Proposal("temporary", [new DimensionRulePoint("a", 0, 0, sources), Point("b", 10, 2)]);
        var second = Proposal("temporary", [new DimensionRulePoint("a", 0, 0, sources.Reverse()), Point("b", 10, 2)]);
        Assert.Equal(DimensionProposalIdentity.Create("snapshot", 7, "panel", "Bottom-location", first).ProposalId,
            DimensionProposalIdentity.Create("snapshot", 7, "panel", "Bottom-location", second).ProposalId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TooFewPointsHaveConcreteDiagnostics(int count)
    {
        var plan = DimensionChainComposer.Compose([Proposal("short", new[] { Point("a", 0, 1) }.Take(count).ToArray())]);
        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(DimensionCompositionDecisionKind.Blocked, decision.Kind);
        Assert.Equal("too-few-points", Assert.Single(decision.Issues).Code);
        Assert.Contains("(" + count + ")", decision.Reason);
    }

    [Fact]
    public void ReportsAllPartCausesAndKeepsViewCausesSeparate()
    {
        var evidence = new Dictionary<string, object> {
            ["missingSupportModelIds"] = new[] { 9, 7, 9 },
            ["unlocatedModelIds"] = new[] { 11 }, ["tiltedPartIds"] = new[] { 13 }
        };
        var plan = DimensionChainComposer.Compose([
            Proposal("complete", [Point("a", 0, 1), Point("b", 10, 2)]),
            Proposal("incomplete", [Point("a", 0, 1), Point("b", 10, 2)], evidence: evidence)
        ], evidence);
        Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, plan.Decisions[0].Kind);
        Assert.Empty(plan.Decisions[0].Issues);
        Assert.Equal(3, plan.ViewIssues.Count);
        Assert.Equal(3, plan.Decisions[1].Issues.Count);
        Assert.Equal(new[] { 7, 9 }, plan.Decisions[1].Issues[0].ModelIds);
        Assert.Contains("7,9", plan.Decisions[1].Reason);
        Assert.Contains("11", plan.Decisions[1].Reason);
        Assert.Contains("13", plan.Decisions[1].Reason);
        Assert.DoesNotContain("inspect", plan.Decisions[1].Reason);
    }

    [Fact]
    public void IncompleteWithoutCauseIsReportedHonestly()
    {
        var plan = DimensionChainComposer.Compose([Proposal("incomplete", [Point("a", 0, 1), Point("b", 10, 2)],
            evidence: new Dictionary<string, object> { ["incomplete"] = true })]);
        Assert.Equal("incomplete-unspecified", Assert.Single(plan.Decisions[0].Issues).Code);
    }

    [Theory]
    [InlineData("", 7)]
    [InlineData(" ", 7)]
    [InlineData("snapshot", 0)]
    [InlineData("snapshot", -1)]
    public void IdentityRequiresExplicitContextAndPositiveView(string contextId, int viewId)
    {
        Assert.ThrowsAny<ArgumentException>(() => DimensionProposalIdentity.Create(contextId, viewId, "panel", "Bottom-location",
            Proposal("temporary", [Point("a", 0, 1), Point("b", 10, 2)])));
    }

    [Fact]
    public void ShortIdsUseSixteenHexDigitsAndCollisionsAreRejected()
    {
        var first = Proposal("temporary", [Point("a", 0, 1), Point("b", 10, 2)]);
        var identity = DimensionProposalIdentity.Create("context", 1, "panel", "Bottom-location", first);
        Assert.Matches("^panel:[0-9a-f]{16}$", identity.ProposalId);
        var second = Proposal("temporary", [Point("a", 0, 1), Point("c", 20, 3)]);
        // A synthetic collision exercises the rejection without searching for a real hash collision.
        var error = Assert.Throws<ArgumentException>(() => DimensionChainComposer.Compose([
            first.WithProposal(identity), second.WithProposal(identity)
        ]));
        Assert.Contains("shortened-ID collisions", error.Message);
    }

    private sealed class FutureLocalPlacement(double offset) : DimensionLinePlacement
    {
        public double Offset { get; } = offset;
    }
    private sealed class FutureInteriorPlacement : DimensionLinePlacement { }

    [Fact]
    public void UnsupportedPlacementGetsIdentityFromTypeAndDataButRemainsBlocked()
    {
        var first = Proposal("temporary", [Point("a", 0, 1), Point("b", 10, 2)]);
        DimensionRuleResult Bind(DimensionLinePlacement placement)
        {
            var proposal = new DimensionRuleResult(first.Direction, placement, first.Kind, first.Points);
            return proposal.WithProposal(DimensionProposalIdentity.Create("context", 1, "panel", "Bottom-location", proposal));
        }
        var local = Bind(new FutureLocalPlacement(10));
        var changedOffset = Bind(new FutureLocalPlacement(20));
        var interior = Bind(new FutureInteriorPlacement());
        Assert.NotEqual(local.Proposal!.ProposalId, changedOffset.Proposal!.ProposalId);
        Assert.NotEqual(local.Proposal.ProposalId, interior.Proposal!.ProposalId);
        Assert.Equal(local.Proposal.ProposalId, Bind(new FutureLocalPlacement(10)).Proposal!.ProposalId);
        var plan = DimensionChainComposer.Compose([local, changedOffset, interior]);
        Assert.All(plan.Decisions, d => {
            Assert.Equal(DimensionCompositionDecisionKind.Blocked, d.Kind);
            Assert.Equal("unsupported-placement", Assert.Single(d.Issues).Code);
        });
    }

    private static DimensionRulePoint Point(string id, double x, int owner) =>
        new(id, x, 0, [new DimensionPointSource("part", owner, "contour:" + owner)]);

    private static DimensionRuleResult Proposal(string id, DimensionRulePoint[] points, string? note = null,
        IReadOnlyDictionary<string, object>? evidence = null) =>
        new(new DimensionDirection(1, 0), new OutsideOutlineDimensionPlacement(new DimensionDirection(0, -1), 1),
            "location", points, note, evidence: evidence,
            proposal: new DimensionProposalIdentity("context", 1, id, DimensionMeasurementPurpose.Location,
                DimensionMeasurementReference.Unknown, DimensionReferenceSupport.Unspecified, "Bottom-location"));
}
