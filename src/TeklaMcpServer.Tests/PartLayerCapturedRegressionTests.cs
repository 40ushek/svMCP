using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class PartLayerCapturedRegressionTests
{
    [Theory]
    [InlineData("normalRules", "expectedNormal")]
    [InlineData("conflictRules", "expectedConflict")]
    public void ObservedFrontClassificationsAndPointOwnershipRemainStable(string rulesField, string expectedField)
    {
        using var document = LoadFixture();
        var fixture = document.RootElement;
        Assert.Equal(1, fixture.GetProperty("schemaVersion").GetInt32());
        var args = new string[13];
        args[12] = fixture.GetProperty(rulesField).GetRawText();
        var rules = DrawingCommandParsers.ParseViewDimensionLayerRules(args);
        var parts = fixture.GetProperty("parts").EnumerateArray()
            .Where(part => part.GetProperty("expectedSource").GetString() == "observed").ToArray();
        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Equal("Front", part.GetProperty("captureScope").GetString()));
        var snapshot = CreateSnapshot(parts, rules);
        var classification = ViewDimensionContext.Freeze(snapshot.Project()).GetProperty("parts").EnumerateArray()
            .ToDictionary(part => part.GetProperty("modelId").GetInt32());
        foreach (var part in parts)
        {
            var actual = classification[part.GetProperty("modelId").GetInt32()];
            var expected = part.GetProperty(expectedField);
            foreach (var field in new[] { "classStatus", "className", "matchedRuleIds", "selectedRuleIds", "unresolvedRuleIds" })
                Assert.Equal(JsonSerializer.Serialize(expected.GetProperty(field)), JsonSerializer.Serialize(actual.GetProperty(field)));
            Assert.False(string.IsNullOrWhiteSpace(actual.GetProperty("reason").GetString()));
        }

        // The capture contains ownership links only. Coordinates and support geometry are not goldens.
        var points = fixture.GetProperty("points").EnumerateArray().ToArray();
        var bound = ViewDimensionContext.Freeze(snapshot.BindPoints(points.Select(point => new DimensionRulePoint(
            point.GetProperty("pointId").GetString()!, 0, 0,
            point.GetProperty("sources").EnumerateArray().Select(source => new DimensionPointSource(
                source.GetProperty("objectKind").GetString()!, source.GetProperty("modelId").ValueKind == JsonValueKind.Null
                    ? null : source.GetProperty("modelId").GetInt32(), "fixture-owner-link"))))));
        var byPoint = bound.EnumerateArray().ToDictionary(point => point.GetProperty("pointId").GetString()!);
        Assert.NotEmpty(byPoint);
        foreach (var point in points)
        {
            var actual = byPoint[point.GetProperty("pointId").GetString()!];
            var expected = point.GetProperty("expected");
            foreach (var field in new[] { "partModelIds", "missingPartModelIds", "unidentifiedPartSourceCount", "nonPartSourceKinds" })
                Assert.Equal(JsonSerializer.Serialize(expected.GetProperty(field)), JsonSerializer.Serialize(actual.GetProperty(field)));
            Assert.All(actual.GetProperty("partModelIds").EnumerateArray(), owner => Assert.True(classification.ContainsKey(owner.GetInt32())));
        }
    }

    [Fact]
    public void CapturedTopAttributesExerciseRulesWithoutClaimingLiveRegression()
    {
        using var document = LoadFixture();
        var fixture = document.RootElement;
        var parts = fixture.GetProperty("parts").EnumerateArray()
            .Where(part => part.GetProperty("expectedSource").GetString() == "evaluated").ToArray();
        Assert.Equal(2, parts.Length);
        Assert.All(parts, part => Assert.Equal("Top", part.GetProperty("captureScope").GetString()));
        foreach (var rulesField in new[] { "normalRules", "conflictRules" })
        {
            var args = new string[13];
            args[12] = fixture.GetProperty(rulesField).GetRawText();
            var snapshot = CreateSnapshot(parts, DrawingCommandParsers.ParseViewDimensionLayerRules(args));
            var results = ViewDimensionContext.Freeze(snapshot.Project()).GetProperty("parts").EnumerateArray()
                .ToDictionary(part => part.GetProperty("modelId").GetInt32());
            var batten = results[parts.Single(part => part.GetProperty("alias").GetString() == "c24-batten").GetProperty("modelId").GetInt32()];
            Assert.Equal("Classified", batten.GetProperty("classStatus").GetString());
            Assert.Equal("batten", batten.GetProperty("className").GetString());
            Assert.Equal(rulesField == "normalRules" ? "c24-batten" : "batten-by-name",
                Assert.Single(batten.GetProperty("selectedRuleIds").EnumerateArray()).GetString());
            var sheathing = results[parts.Single(part => part.GetProperty("alias").GetString() == "sheathing").GetProperty("modelId").GetInt32()];
            Assert.Equal("Unknown", sheathing.GetProperty("classStatus").GetString());
            Assert.Empty(sheathing.GetProperty("matchedRuleIds").EnumerateArray());
        }
    }

    [Fact]
    public void EveryCapturedPartDeclaresItsExpectationProvenance()
    {
        using var document = LoadFixture();
        Assert.All(document.RootElement.GetProperty("parts").EnumerateArray(), part =>
        {
            var scope = part.GetProperty("captureScope").GetString();
            Assert.Contains(scope, new[] { "Front", "Top" });
            Assert.Equal(scope == "Front" ? "observed" : "evaluated", part.GetProperty("expectedSource").GetString());
        });
    }

    private static JsonDocument LoadFixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "PartLayers", "timber-batten-mixed-prefixes.v1.json")));

    private static PartLayerSnapshot CreateSnapshot(IEnumerable<JsonElement> parts, IReadOnlyList<PartLayerRule> rules) =>
        new(parts.Select(part => new PartRoleInView(
            part.GetProperty("modelId").GetInt32(), part.GetProperty("alias").GetString(), part.GetProperty("prefix").GetString(),
            new PartRoleResult(PartRole.Included, "captured-attributes", "Fixture inclusion is not a planner decision."),
            profile: part.GetProperty("profile").GetString(), material: part.GetProperty("material").GetString(), name: part.GetProperty("name").GetString())), rules);
}
