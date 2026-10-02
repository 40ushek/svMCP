using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionChainUnionTests
{
    [Fact]
    public void ExplicitCompatiblePairProducesAUnionWithoutReplacingOriginals()
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var b = Known([Point("datum", 0, 1), Point("b", 20, 3)]);
        var plan = DimensionChainComposer.Compose([a, b]);
        var chain = Assert.Single(plan.Chains);
        Assert.Equal(new[] { "datum", "a", "b" }, chain.Points.Select(p => p.Id));
        Assert.Equal(new[] { 10d, 10d }, chain.Segments);
        Assert.Equal(new[] { 1, 2, 3 }, chain.Points.Select(p => p.Sources[0].ModelId!.Value));
        Assert.Same(a, plan.OriginalProposals[0]);
        Assert.Same(b, plan.OriginalProposals[1]);
        Assert.All(plan.Decisions, decision => {
            Assert.Equal(DimensionCompositionDecisionKind.Combined, decision.Kind);
            Assert.Equal(chain.ChainId, decision.ChainId);
            Assert.Empty(decision.Issues);
        });
        Assert.Matches("^composed:[0-9a-f]{16}$", chain.ChainId);
        var projected = ViewDimensionContext.Freeze(plan.Project()).GetProperty("chains")[0];
        Assert.Equal("composition-preview", projected.GetProperty("stage").GetString());
        Assert.False(projected.TryGetProperty("points", out _));
        Assert.False(projected.TryGetProperty("legacyRowHint", out _));
    }

    [Theory]
    [InlineData("category", "categories")]
    [InlineData("units", "Units")]
    [InlineData("reference-id", "Reference")]
    [InlineData("scope", "scopes")]
    [InlineData("type", "types")]
    [InlineData("direction", "Directions")]
    [InlineData("normal", "Placement")]
    [InlineData("purpose", "purposes")]
    [InlineData("reference", "Reference")]
    [InlineData("reference-support", "Reference")]
    [InlineData("datum", "datum")]
    [InlineData("datum-owner", "datum")]
    [InlineData("same-coordinate-other-owner", "support evidence")]
    [InlineData("same-id-other-coordinate", "support evidence")]
    [InlineData("opposite-span", "opposite sides")]
    public void IncompatibleEvidenceKeepsBothProposalsSeparate(string change, string reason)
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var points = change switch {
            "datum" => new[] { Point("other", 0, 1), Point("b", 20, 3) },
            "datum-owner" => new[] { Point("datum", 0, 99), Point("b", 20, 3) },
            "same-coordinate-other-owner" => new[] { Point("datum", 0, 1), Point("other", 10, 99) },
            "same-id-other-coordinate" => new[] { Point("datum", 0, 1), Point("a", 20, 2) },
            "opposite-span" => new[] { Point("datum", 0, 1), Point("b", -20, 3) },
            _ => new[] { Point("datum", 0, 1), Point("b", 20, 3) }
        };
        var intent = new DimensionCompositionIntent(change == "category" ? DimensionSubjectCategory.Bolt : DimensionSubjectCategory.Part,
            change == "units" ? "in" : "mm", change == "reference-id" ? "other-frame" : "frame",
            change == "scope" ? "other-layer" : "structural", change == "type" ? DimensionChainMeasurementType.Absolute : DimensionChainMeasurementType.Relative);
        var b = Known(points, intent, direction: change == "direction" ? new(-1, 0) : null,
            placement: change == "normal" ? new(new DimensionDirection(0, 1), 1) : null,
            kind: change == "purpose" ? "location" : "internal",
            reference: change == "reference" ? DimensionMeasurementReference.Assembly : DimensionMeasurementReference.MainPart,
            support: change == "reference-support" ? DimensionReferenceSupport.Midpoint : DimensionReferenceSupport.NearestEdge);
        var plan = DimensionChainComposer.Compose([a, b]);
        Assert.Empty(plan.Chains);
        Assert.All(plan.Decisions, decision => {
            Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, decision.Kind);
            Assert.Contains(reason, decision.Reason);
        });
    }

    [Theory]
    [InlineData("unknown-reference", "unresolved")]
    [InlineData("unknown-purpose", "unresolved")]
    [InlineData("closed", "Closed-chain")]
    [InlineData("overall", "Overall")]
    [InlineData("check", "check")]
    [InlineData("nonmonotonic", "monotonic")]
    public void UnresolvedOrUnsupportedIntentNeverClaimsCombination(string change, string reason)
    {
        var points = change == "nonmonotonic"
            ? new[] { Point("datum", 0, 1), Point("a", 20, 2), Point("b", 10, 3) }
            : new[] { Point("datum", 0, 1), Point("a", 10, 2) };
        var proposal = Known(points, new(DimensionSubjectCategory.Part, "mm", "frame", "structural",
            DimensionChainMeasurementType.Relative, change == "closed" ? DimensionChainClosure.Closed : DimensionChainClosure.Open),
            kind: change == "unknown-purpose" ? "future" : change is "overall" or "check" ? change : "internal",
            reference: change == "unknown-reference" ? DimensionMeasurementReference.Unknown : DimensionMeasurementReference.MainPart);
        var decision = Assert.Single(DimensionChainComposer.Compose([proposal]).Decisions);
        Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, decision.Kind);
        Assert.Contains(reason, decision.Reason);
    }

    [Fact]
    public void BlockedProposalCannotEnterAnOtherwiseCompatibleUnion()
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var b = Known([Point("datum", 0, 1), Point("b", 20, 3)]);
        var blocked = new DimensionRuleResult(b.Direction, b.Placement, b.Kind, b.Points,
            evidence: new Dictionary<string, object> { ["missingSupportModelIds"] = new[] { 99 } },
            proposal: b.Proposal, compositionIntent: b.CompositionIntent);
        var plan = DimensionChainComposer.Compose([a, blocked]);
        Assert.Empty(plan.Chains);
        Assert.Equal(DimensionCompositionDecisionKind.Blocked, plan.Decisions[1].Kind);
        Assert.Equal(new[] { 99 }, Assert.Single(plan.Decisions[1].Issues).ModelIds);
    }

    [Fact]
    public void UnionIsDeterministicForReorderedInputsAndRetainsEveryOriginalSource()
    {
        var sources = new[] { new DimensionPointSource("part", 1, "edge"), new DimensionPointSource("geometry", null, "frame-edge") };
        var a = Known([new("datum", 0, 0, sources), Point("a", 10, 2)]);
        var b = Known([new("datum", 0, 0, sources.Reverse()), Point("b", 20, 3)]);
        var c = Known([new("datum", 0, 0, sources), Point("c", 30, 4)]);
        var first = DimensionChainComposer.Compose([a, b, c]);
        var reversed = DimensionChainComposer.Compose([c, b, a]);
        Assert.Equal(JsonSerializer.Serialize(first.Chains.Select(chain => chain.Project())),
            JsonSerializer.Serialize(reversed.Chains.Select(chain => chain.Project())));
        Assert.Equal(2, Assert.Single(first.Chains).Points[0].Sources.Count);
        Assert.Equal(JsonSerializer.Serialize(first.Project()),
            JsonSerializer.Serialize(DimensionChainComposer.Compose(first.OriginalProposals).Project()));
    }

    [Theory]
    [InlineData(1, 1, 10, 10, 20, 20)]
    [InlineData(1, 0, -10, 0, -20, 0)]
    public void UnionPreservesSkewOrBackwardDatumOrder(double dx, double dy, double ax, double ay, double bx, double by)
    {
        var direction = new DimensionDirection(dx, dy);
        var a = Known([Point("datum", 0, 1), new("a", ax, ay, [new("part", 2, "edge:2")])], direction: direction);
        var b = Known([Point("datum", 0, 1), new("b", bx, by, [new("part", 3, "edge:3")])], direction: direction);
        var chain = Assert.Single(DimensionChainComposer.Compose([a, b]).Chains);
        Assert.Equal(new[] { "datum", "a", "b" }, chain.Points.Select(point => point.Id));
        Assert.All(chain.Segments, segment => Assert.Equal(Math.Sqrt(ax * ax + ay * ay), segment, 10));
    }

    [Fact]
    public void CompositionSemanticsParticipateInContentIdentity()
    {
        var points = new[] { Point("datum", 0, 1), Point("a", 10, 2) };
        var a = Known(points);
        var b = Known(points, new(DimensionSubjectCategory.Part, "mm", "frame", "another-scope", DimensionChainMeasurementType.Relative));
        Assert.NotEqual(a.Proposal!.ProposalId, b.Proposal!.ProposalId);
        Assert.Same(a.CompositionIntent, a.WithProposal(a.Proposal).CompositionIntent);
    }

    [Fact]
    public void PanelDiagnosticAdapterExposesUnionAndPreservesLegacyRows()
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var b = Known([Point("datum", 0, 1), Point("b", 20, 3)]);
        var diagnostics = new Dictionary<string, object> {
            ["contactFallbackPairs"] = Array.Empty<int[]>(), ["unlocatedModelIds"] = Array.Empty<int>(),
            ["missingSupportModelIds"] = Array.Empty<int>(), ["tiltedPartIds"] = Array.Empty<int>(), ["contactStatus"] = "not-requested"
        };
        var preview = TimberPanelChainPreview.FromEvaluation(new DimensionRuleEvaluation([a, b], diagnostics), "snapshot", 1);
        var legacyBefore = JsonSerializer.Serialize(preview.Rows);
        var plan = ViewDimensionContext.Freeze(preview.ProjectCompositionPlan());
        Assert.Equal(1, plan.GetProperty("chains").GetArrayLength());
        Assert.All(plan.GetProperty("decisions").EnumerateArray(), decision => Assert.Equal("Combined", decision.GetProperty("decision").GetString()));
        Assert.Equal(legacyBefore, JsonSerializer.Serialize(preview.Rows));
        Assert.Equal(2, preview.Rows.Single(row => row.side == "Bottom").chains.Length);
    }

    private static DimensionRulePoint Point(string id, double x, int owner) => new(id, x, 0, [new("part", owner, "edge:" + owner)]);

    [Theory]
    [InlineData(0.099, false)]
    [InlineData(0.1, false)]
    [InlineData(0.101, true)]
    public void NearbyDifferentSupportsUseConfiguredToleranceWithoutSnapping(double separation, bool combines)
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var b = Known([Point("datum", 0, 1), Point("b", 10 + separation, 99)]);
        var plan = DimensionChainComposer.Compose([a, b], coordinateSettings: new(0.1));
        Assert.Equal(combines ? 1 : 0, plan.Chains.Count);
        Assert.Equal(0.1, ViewDimensionContext.Freeze(plan.Project()).GetProperty("coordinateToleranceViewUnits").GetDouble());
        if (combines) Assert.Equal(10 + separation, plan.Chains[0].Points[2].X);
        else Assert.All(plan.Decisions, d => Assert.Contains("support evidence", d.Reason));
    }

    [Fact]
    public void SmallerRequestToleranceCanAdmitTheSamePairWithoutChangingOriginalPoints()
    {
        var a = Known([Point("datum", 0, 1), Point("a", 10, 2)]);
        var b = Known([Point("datum", 0, 1), Point("b", 10.05, 99)]);
        Assert.Empty(DimensionChainComposer.Compose([a, b]).Chains);
        var fine = DimensionChainComposer.Compose([a, b], coordinateSettings: new(0.01));
        Assert.Equal(10.05, Assert.Single(fine.Chains).Points[2].X);
        Assert.Equal(10.05, b.Points[1].X);
    }

    [Theory]
    [InlineData("a", "b", "c")]
    [InlineData("c", "a", "b")]
    [InlineData("b", "c", "a")]
    public void CompetingUnionsRemainExplicitlyAmbiguousRegardlessOfAddressOrder(string aId, string bId, string cId)
    {
        DimensionRuleResult Address(DimensionRuleResult p, string id) => p.WithProposal(new("snapshot", 1, id,
            p.Proposal!.Purpose, p.Proposal.Reference, p.Proposal.ReferenceSupport, p.Proposal.PreviewKey));
        var a = Address(Known([Point("datum", 0, 1), Point("a-point", 10, 2)]), aId);
        var b = Address(Known([Point("datum", 0, 1), Point("b-point", 20, 3)]), bId);
        var c = Address(Known([Point("datum", 0, 1), Point("c-point", 20, 99)]), cId);
        var otherScope = new DimensionCompositionIntent(DimensionSubjectCategory.Part, "mm", "frame", "other", DimensionChainMeasurementType.Relative);
        var d = Known([Point("datum", 0, 1), Point("d", 40, 4)], otherScope);
        var e = Known([Point("datum", 0, 1), Point("e", 50, 5)], otherScope);
        var plan = DimensionChainComposer.Compose([c, e, a, d, b]);
        Assert.Equal(2, Assert.Single(plan.Chains).Proposals.Count);
        foreach (var decision in plan.Decisions.Where(d => new[] { aId, bId, cId }.Contains(d.ProposalId)))
        {
            Assert.Equal(DimensionCompositionDecisionKind.KeepSeparate, decision.Kind);
            Assert.Null(decision.ChainId);
            var issue = Assert.Single(decision.Issues);
            Assert.Equal("ambiguous-composition", issue.Code);
            Assert.Equal(new[] { "a", "b", "c" }, issue.ProposalIds);
            Assert.Equal(3, ViewDimensionContext.Freeze(issue.Project()).GetProperty("proposalIds").GetArrayLength());
        }
        Assert.Equal(JsonSerializer.Serialize(plan.Chains.Select(c => c.Project())),
            JsonSerializer.Serialize(DimensionChainComposer.Compose([b, d, a, e, c]).Chains.Select(c => c.Project())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CoordinateSettingsRejectNonpositiveOrNonfiniteTolerance(double tolerance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionCoordinateSettings(tolerance));

    private static DimensionRuleResult Known(DimensionRulePoint[] points, DimensionCompositionIntent? intent = null,
        DimensionDirection? direction = null, OutsideOutlineDimensionPlacement? placement = null, string kind = "internal",
        DimensionMeasurementReference reference = DimensionMeasurementReference.MainPart,
        DimensionReferenceSupport support = DimensionReferenceSupport.NearestEdge)
    {
        var result = new DimensionRuleResult(direction ?? new(1, 0), placement ?? new(new DimensionDirection(0, -1), 1), kind,
            points, compositionIntent: intent ?? new(DimensionSubjectCategory.Part, "mm", "frame", "structural", DimensionChainMeasurementType.Relative));
        return result.WithProposal(DimensionProposalIdentity.Create("snapshot", 1, "synthetic", "Bottom-" + kind, result, reference, support));
    }
}
