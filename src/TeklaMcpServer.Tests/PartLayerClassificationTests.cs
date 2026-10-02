using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class PartLayerClassificationTests
{
    private static PartLayerRule Rule(string id, string className, PartLayerProperty property, string value, int priority = 0) =>
        new(id, className, new Dictionary<PartLayerProperty, string> { [property] = value }, priority);

    [Theory]
    [InlineData("BEAM")]
    [InlineData("BATTEN")]
    [InlineData("Roof beam")]
    public void ShortContainsValueMatchesLiteralSubstringWithoutWordBoundaries(string name)
    {
        var rule = new PartLayerRule("b-substring", "b-matched", new[] {
            new PartLayerCondition(PartLayerProperty.Name, "B", PartLayerMatchKind.Contains)
        });
        var result = PartRoleClassifier.ClassifyLayer(name, null, null, [rule]);
        Assert.Equal(PartLayerStatus.Classified, result.Status);
        Assert.Equal(new[] { "b-substring" }, result.SelectedRuleIds);
    }

    [Theory]
    [InlineData(PartLayerMatchKind.Equals, "batten", true)]
    [InlineData(PartLayerMatchKind.Equals, "Batten 28x70", false)]
    [InlineData(PartLayerMatchKind.Contains, "Batten 28x70", true)]
    [InlineData(PartLayerMatchKind.Contains, "Roof BATTEN 28x70", true)]
    [InlineData(PartLayerMatchKind.Contains, "FRAME", false)]
    [InlineData(PartLayerMatchKind.StartsWith, "Batten 28x70", true)]
    [InlineData(PartLayerMatchKind.StartsWith, "Roof BATTEN 28x70", false)]
    public void ExplicitMatchKindsHandleSuffixesWithoutChangingDefaultEquality(PartLayerMatchKind matchKind, string name, bool matches)
    {
        var rule = new PartLayerRule("name", "batten", new[] { new PartLayerCondition(PartLayerProperty.Name, "BATTEN", matchKind) });
        Assert.Equal(matches ? PartLayerStatus.Classified : PartLayerStatus.Unknown,
            PartRoleClassifier.ClassifyLayer(name, null, null, [rule]).Status);
        Assert.Equal(PartLayerMatchKind.Equals, new PartLayerCondition(PartLayerProperty.Name, "BATTEN").MatchKind);
    }

    [Theory]
    [InlineData(PartLayerMatchKind.Equals)]
    [InlineData(PartLayerMatchKind.Contains)]
    [InlineData(PartLayerMatchKind.StartsWith)]
    public void MissingPropertyAndKnownMismatchKeepTheirSemanticsForEveryMatchKind(PartLayerMatchKind matchKind)
    {
        var rule = new PartLayerRule("name", "batten", new[] {
            new PartLayerCondition(PartLayerProperty.Name, "BATTEN", matchKind),
            new PartLayerCondition(PartLayerProperty.Material, "TIMBER")
        });
        var unresolved = PartRoleClassifier.ClassifyLayer(null, null, null, [rule], material: "TIMBER");
        Assert.Equal(new[] { "name" }, unresolved.UnresolvedRuleIds);
        Assert.Contains("name (Name)", unresolved.Reason);
        var disproved = PartRoleClassifier.ClassifyLayer(null, null, null, [rule], material: "C24");
        Assert.Empty(disproved.UnresolvedRuleIds);
        Assert.Empty(disproved.MatchedRuleIds);
    }

    [Fact]
    public void ExplicitConditionsAreCapturedAndProjectedDeterministically()
    {
        var conditions = new List<PartLayerCondition> {
            new(PartLayerProperty.Name, "BATTEN", PartLayerMatchKind.Contains),
            new(PartLayerProperty.Name, "Roof", PartLayerMatchKind.StartsWith)
        };
        var rule = new PartLayerRule("roof", "batten", conditions);
        var reversed = new PartLayerRule("roof", "batten", conditions.AsEnumerable().Reverse());
        conditions.Clear();
        Assert.Equal(PartLayerStatus.Classified, PartRoleClassifier.ClassifyLayer("Roof BATTEN 28x70", null, null, [rule]).Status);
        Assert.Equal(PartLayerStatus.Unknown, PartRoleClassifier.ClassifyLayer("BATTEN 28x70", null, null, [rule]).Status);
        Assert.Equal(ViewDimensionContext.Freeze(rule.Project()).GetRawText(), ViewDimensionContext.Freeze(reversed.Project()).GetRawText());
        Assert.Throws<ArgumentException>(() => new PartLayerCondition(PartLayerProperty.Name, "BATTEN", (PartLayerMatchKind)99));
        Assert.Throws<ArgumentException>(() => new PartLayerCondition(PartLayerProperty.Name, " ", PartLayerMatchKind.Contains));
        Assert.Throws<ArgumentException>(() => new PartLayerRule("r", "batten", new PartLayerCondition[] { null! }));
    }

    [Fact]
    public void NoOrEmptyRulesLeaveDetailedPlanWithoutClassificationTables()
    {
        var context = ViewDimensionContextTests.Context();
        var absent = context.Query("chainDetails", ruleSet: "panel");
        var empty = context.Query("chainDetails", ruleSet: "panel", layerRules: []);
        Assert.Equal(absent.GetRawText(), empty.GetRawText());
        var plan = absent.GetProperty("compositionPlan");
        foreach (var field in new[] { "partClassification", "pointPartBindings", "proposalPointIds" })
            Assert.False(plan.TryGetProperty(field, out _));
        Assert.True(plan.TryGetProperty("proposals", out _));
        Assert.True(plan.TryGetProperty("decisions", out _));
    }

    [Fact]
    public void CapturedMaterialCanResolveAClassConflictWithExplicitPriority()
    {
        var context = ViewDimensionContextTests.Context(partName: "Batten 28x70", partMaterial: "TIMBER");
        PartLayerRule[] rules = [
            new("name", "batten", new[] { new PartLayerCondition(PartLayerProperty.Name, "BATTEN", PartLayerMatchKind.StartsWith) }),
            Rule("prefix", "frame", PartLayerProperty.Prefix, "P"),
            Rule("material", "timber-batten", PartLayerProperty.Material, "timber", 1)
        ];
        var baseline = context.Query("chainDetails", ruleSet: "panel");
        var answer = context.Query("chainDetails", ruleSet: "panel", layerRules: rules);
        Assert.Equal(baseline.GetProperty("chainPreview").GetRawText(), answer.GetProperty("chainPreview").GetRawText());
        var classification = answer.GetProperty("compositionPlan").GetProperty("partClassification");
        var part = classification.GetProperty("parts")[0];
        Assert.Equal("TIMBER", part.GetProperty("material").GetString());
        Assert.Equal("timber-batten", part.GetProperty("className").GetString());
        Assert.Equal("material", Assert.Single(part.GetProperty("selectedRuleIds").EnumerateArray()).GetString());
        Assert.Equal("StartsWith", classification.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("ruleId").GetString() == "name").GetProperty("conditions")[0].GetProperty("matchKind").GetString());
        Assert.Equal("C24", PartRoleClassifier.ClassifyLayer(null, null, null,
            [Rule("c24", "C24", PartLayerProperty.Material, "C24")], material: "c24").ClassName);
    }

    [Fact]
    public void NoRulesDoNotInferAClassFromPlantPrefixes()
    {
        var result = PartRoleClassifier.ClassifyLayer("BATTEN", "B", "45X70");
        Assert.Equal(PartLayerStatus.Unknown, result.Status);
        Assert.Null(result.ClassName);
        Assert.Empty(result.MatchedRuleIds);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("T")]
    public void NameRuleWorksAcrossPrefixesAndIgnoresCase(string prefix)
    {
        var result = PartRoleClassifier.ClassifyLayer("batten", prefix, "45X70", [Rule("battens", "batten", PartLayerProperty.Name, "BATTEN")]);
        Assert.Equal(PartLayerStatus.Classified, result.Status);
        Assert.Equal("batten", result.ClassName);
        Assert.Equal(new[] { "battens" }, result.SelectedRuleIds);
    }

    [Fact]
    public void AllConditionsMustMatchWholeValues()
    {
        var rule = new PartLayerRule("specific", "frame", new Dictionary<PartLayerProperty, string> {
            [PartLayerProperty.Name] = "FRAME", [PartLayerProperty.Profile] = "45X70", [PartLayerProperty.Prefix] = "P"
        });
        Assert.Equal(PartLayerStatus.Classified, PartRoleClassifier.ClassifyLayer("FRAME", "P", "45x70", [rule]).Status);
        Assert.Equal(PartLayerStatus.Unknown, PartRoleClassifier.ClassifyLayer("FRAME", "P", "45X700", [rule]).Status);
        Assert.Equal(PartLayerStatus.Unknown, PartRoleClassifier.ClassifyLayer("FRAME EXTRA", "P", "45X70", [rule]).Status);
    }

    [Fact]
    public void ExplicitPriorityResolvesDifferentClassesAndRetainsMatchedRules()
    {
        var result = PartRoleClassifier.ClassifyLayer("FRAME", "P", null, [
            Rule("prefix", "generic", PartLayerProperty.Prefix, "P"),
            Rule("name", "frame", PartLayerProperty.Name, "FRAME", 10)
        ]);
        Assert.Equal("frame", result.ClassName);
        Assert.Equal(new[] { "name", "prefix" }, result.MatchedRuleIds);
        Assert.Equal(new[] { "name" }, result.SelectedRuleIds);
    }

    [Fact]
    public void EqualPriorityConflictIsExplicitAndOrderIndependent()
    {
        PartLayerRule[] rules = [Rule("prefix", "generic", PartLayerProperty.Prefix, "P"), Rule("name", "frame", PartLayerProperty.Name, "FRAME")];
        var result = PartRoleClassifier.ClassifyLayer("FRAME", "P", null, rules);
        Assert.Equal(PartLayerStatus.Conflict, result.Status);
        Assert.Null(result.ClassName);
        Assert.Equal(new[] { "name", "prefix" }, result.MatchedRuleIds);
        Assert.Empty(result.SelectedRuleIds);
        Assert.Contains("frame,generic", result.Reason);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(PartRoleClassifier.ClassifyLayer("FRAME", "P", null, rules.Reverse().ToArray())));
    }

    [Fact]
    public void EqualPrioritySameClassKeepsBothWinningRules()
    {
        var result = PartRoleClassifier.ClassifyLayer("FRAME", "P", null, [Rule("prefix", "frame", PartLayerProperty.Prefix, "P"), Rule("name", "frame", PartLayerProperty.Name, "FRAME")]);
        Assert.Equal(PartLayerStatus.Classified, result.Status);
        Assert.Equal(new[] { "name", "prefix" }, result.SelectedRuleIds);
    }

    [Theory]
    [InlineData(0, PartLayerStatus.Unknown)]
    [InlineData(1, PartLayerStatus.Unknown)]
    [InlineData(-1, PartLayerStatus.Classified)]
    public void MissingPropertyBlocksOnlyPotentiallyDecidingRules(int priority, PartLayerStatus status)
    {
        var result = PartRoleClassifier.ClassifyLayer(null, "P", null, [Rule("missing-name", "frame", PartLayerProperty.Name, "FRAME", priority), Rule("prefix", "generic", PartLayerProperty.Prefix, "P")]);
        Assert.Equal(status, result.Status);
        Assert.Equal(new[] { "missing-name" }, result.UnresolvedRuleIds);
        if (status == PartLayerStatus.Unknown) Assert.Contains("missing-name (Name)", result.Reason);
    }

    [Fact]
    public void KnownMismatchDisprovesRuleEvenWithOtherMissingProperties()
    {
        var rule = new PartLayerRule("irrelevant", "frame", new Dictionary<PartLayerProperty, string> {
            [PartLayerProperty.Name] = "FRAME", [PartLayerProperty.Prefix] = "B"
        }, 100);
        var result = PartRoleClassifier.ClassifyLayer(null, "P", null, [rule, Rule("prefix", "generic", PartLayerProperty.Prefix, "P")]);
        Assert.Equal(PartLayerStatus.Classified, result.Status);
        Assert.Empty(result.UnresolvedRuleIds);
    }

    [Fact]
    public void ConfigurationRejectsInvalidConditionsAndDuplicateRuleIds()
    {
        Assert.Throws<ArgumentException>(() => new PartLayerRule("", "frame", new Dictionary<PartLayerProperty, string> { [PartLayerProperty.Name] = "FRAME" }));
        Assert.Throws<ArgumentException>(() => new PartLayerRule("r", "", new Dictionary<PartLayerProperty, string> { [PartLayerProperty.Name] = "FRAME" }));
        Assert.Throws<ArgumentException>(() => new PartLayerRule("r", "frame", new Dictionary<PartLayerProperty, string>()));
        Assert.Throws<ArgumentException>(() => Rule("r", "frame", (PartLayerProperty)99, "FRAME"));
        Assert.Throws<ArgumentException>(() => Rule("r", "frame", PartLayerProperty.Name, " "));
        var rule = Rule("r", "frame", PartLayerProperty.Name, "FRAME");
        Assert.Throws<ArgumentException>(() => PartRoleClassifier.ClassifyLayer("FRAME", null, null, [rule, rule]));
        Assert.Throws<ArgumentException>(() => PartRoleClassifier.ClassifyLayer("FRAME", null, null, [null!]));
    }

    [Fact]
    public void RuleCapturesConditionsAndArbitraryJsonDataIndependentlyOfTheirOwners()
    {
        var conditions = new Dictionary<PartLayerProperty, string> { [PartLayerProperty.Name] = "FRAME" };
        var data = new Dictionary<string, JsonElement>();
        PartLayerRule rule;
        using (var document = JsonDocument.Parse("{\"ui\":{\"color\":\"blue\",\"tags\":[1,true]},\"future\":null}"))
        {
            data["ui"] = document.RootElement.GetProperty("ui");
            data["future"] = document.RootElement.GetProperty("future");
            rule = new PartLayerRule("r", "frame", conditions, data: data);
        }
        conditions[PartLayerProperty.Name] = "OTHER";
        data.Clear();
        Assert.Equal(PartLayerStatus.Classified, PartRoleClassifier.ClassifyLayer("FRAME", null, null, [rule]).Status);
        Assert.Equal("blue", rule.Data["ui"].GetProperty("color").GetString());
        Assert.Equal(JsonValueKind.Null, rule.Data["future"].ValueKind);
    }

    [Fact]
    public void DetailedPanelQueryLinksProposalPointsToOnePartAndRuleTable()
    {
        var context = ViewDimensionContextTests.Context(partName: "FRAME", partProfile: "45X70");
        var rule = Rule("frame-name", "frame", PartLayerProperty.Name, "FRAME");
        var answer = context.Query("chainDetails,parts", ruleSet: "panel", layerRules: [rule]);
        var plan = answer.GetProperty("compositionPlan");
        var classification = plan.GetProperty("partClassification");
        Assert.Equal(PartRoleClassifier.LayerPolicyVersion, classification.GetProperty("policyVersion").GetString());
        Assert.Single(classification.GetProperty("rules").EnumerateArray());
        var parts = classification.GetProperty("parts").EnumerateArray().ToDictionary(p => p.GetProperty("modelId").GetInt32());
        Assert.Equal("FRAME", parts[10].GetProperty("name").GetString());
        Assert.Equal("P", parts[10].GetProperty("prefix").GetString());
        Assert.Equal("45X70", parts[10].GetProperty("profile").GetString());
        Assert.Equal("frame", parts[10].GetProperty("className").GetString());
        Assert.Equal("Included", parts[10].GetProperty("structuralStatus").GetString());
        Assert.Equal("Excluded", parts[20].GetProperty("structuralStatus").GetString());
        Assert.Equal("Unknown", parts[20].GetProperty("classStatus").GetString());
        var bindings = plan.GetProperty("pointPartBindings").EnumerateArray().ToDictionary(p => p.GetProperty("pointId").GetString()!);
        Assert.NotEmpty(bindings);
        Assert.All(bindings.Values, binding => {
            Assert.Empty(binding.GetProperty("missingPartModelIds").EnumerateArray());
            Assert.All(binding.GetProperty("partModelIds").EnumerateArray(), id => Assert.True(parts.ContainsKey(id.GetInt32())));
        });
        Assert.Contains(bindings.Values, p => p.GetProperty("partModelIds").EnumerateArray().Any(id => id.GetInt32() == 10));
        Assert.All(plan.GetProperty("proposalPointIds").EnumerateArray(), proposal =>
            Assert.All(proposal.GetProperty("pointIds").EnumerateArray(), id => Assert.True(bindings.ContainsKey(id.GetString()!))));
    }

    [Fact]
    public void ClassificationAndConflictsDoNotChangePreviewOrCompositionDecisions()
    {
        var context = ViewDimensionContextTests.Context(partName: "FRAME");
        var baseline = context.Query("chainDetails", ruleSet: "panel");
        PartLayerRule[] rules = [Rule("frame", "frame", PartLayerProperty.Name, "FRAME"), Rule("prefix", "batten", PartLayerProperty.Prefix, "P")];
        var classified = context.Query("chainDetails", ruleSet: "panel", layerRules: rules);
        Assert.Equal(baseline.GetProperty("chainPreview").GetRawText(), classified.GetProperty("chainPreview").GetRawText());
        foreach (var field in new[] { "proposals", "decisions", "viewIssues" })
            Assert.Equal(baseline.GetProperty("compositionPlan").GetProperty(field).GetRawText(), classified.GetProperty("compositionPlan").GetProperty(field).GetRawText());
        Assert.Equal(classified.GetProperty("compositionPlan").GetRawText(), context.Query("chainDetails", ruleSet: "panel", layerRules: rules.Reverse().ToArray()).GetProperty("compositionPlan").GetRawText());
        Assert.Equal("Conflict", classified.GetProperty("compositionPlan").GetProperty("partClassification").GetProperty("parts")[0].GetProperty("classStatus").GetString());
        Assert.Equal(context.Query("chain", ruleSet: "panel").GetRawText(), context.Query("chain", ruleSet: "panel", layerRules: rules).GetRawText());
        Assert.Equal(context.Query("chainDetails").GetRawText(), context.Query("chainDetails", layerRules: rules).GetRawText());
    }

    [Fact]
    public void InvalidLayerConfigurationIsIsolatedFromLegacyPreview()
    {
        var context = ViewDimensionContextTests.Context();
        var rule = Rule("duplicate", "frame", PartLayerProperty.Prefix, "P");
        var baseline = context.Query("chainDetails", ruleSet: "panel");
        var invalid = context.Query("chainDetails", ruleSet: "panel", layerRules: [rule, rule]);
        Assert.Equal(baseline.GetProperty("chainPreview").GetRawText(), invalid.GetProperty("chainPreview").GetRawText());
        Assert.Contains("unique", invalid.GetProperty("compositionPlan").GetProperty("error").GetString());
        Assert.False(context.Query("chain", ruleSet: "panel", layerRules: [rule, rule]).TryGetProperty("compositionPlan", out _));
    }

    [Fact]
    public void PointBindingsKeepMultipleOwnersAndReportAbsentOrUnidentifiedParts()
    {
        var snapshot = new PartLayerSnapshot([
            new PartRoleInView(10, "P10", "P", new PartRoleResult(PartRole.Included, "included", "test"))
        ], []);
        DimensionRulePoint[] points = [
            new("p1", 0, 0, [new("part", 10, "edge-a"), new("part", 99, "edge-b"), new("geometry", null, "datum")]),
            new("p1", 0, 0, [new("part", 10, "edge-c")]),
            new("p2", 1, 0, [new("part", null, "unidentified")])
        ];
        var bindings = ViewDimensionContext.Freeze(snapshot.BindPoints(points));
        Assert.Equal(new[] { 10, 99 }, bindings[0].GetProperty("partModelIds").EnumerateArray().Select(id => id.GetInt32()));
        Assert.Equal(99, Assert.Single(bindings[0].GetProperty("missingPartModelIds").EnumerateArray()).GetInt32());
        Assert.Equal("geometry", Assert.Single(bindings[0].GetProperty("nonPartSourceKinds").EnumerateArray()).GetString());
        Assert.Equal(1, bindings[1].GetProperty("unidentifiedPartSourceCount").GetInt32());
        Assert.Equal(bindings.GetRawText(), ViewDimensionContext.Freeze(snapshot.BindPoints(points.Reverse())).GetRawText());
    }
}
