using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class PartLayerRulesTransportTests
{
    private const string Rules = """
        [{"id":"batten","className":"batten","priority":10,"conditions":[
          {"property":"Name","matchKind":"StartsWith","value":"BATTEN"},
          {"property":"Material","value":"TIMBER"}],
          "data":{"label":"Рейки \"B\"","future":{"flags":[true,1,null]}}}]
        """;

    private static string[] Args(string json) => ModelTools.BuildViewDimensionContextArgs(
        7, "chainDetails", "Bottom", "R", "wool", false, "", "horizontal", 8, "panel", "", json);

    [Fact]
    public void McpArgumentsPreserveExistingPositionsAndJsonExactly()
    {
        var args = Args(Rules);
        Assert.Equal(new[] { "get_view_dimension_context", "7", "chaindetails", "Bottom", "R", "wool",
            "False", "", "horizontal", "8", "panel", "", Rules }, args);
        var rule = Assert.Single(DrawingCommandParsers.ParseViewDimensionLayerRules(args));
        Assert.Equal("batten", rule.Id);
        Assert.Equal(10, rule.Priority);
        Assert.Equal(PartLayerMatchKind.StartsWith, rule.Conditions.Single(c => c.Property == PartLayerProperty.Name).MatchKind);
        Assert.Equal(PartLayerMatchKind.Equals, rule.Conditions.Single(c => c.Property == PartLayerProperty.Material).MatchKind);
        Assert.Equal("Рейки \"B\"", rule.Data["label"].GetString());
        Assert.Equal(JsonValueKind.Null, rule.Data["future"].GetProperty("flags")[2].ValueKind);
        var context = ViewDimensionContextTests.Context(partName: "Batten 28x70", partMaterial: "TIMBER");
        var response = context.Query(DrawingCommandParsers.NormalizePreviewQuestions(args[2]), args[3], ruleSet: args[10],
            layerRules: DrawingCommandParsers.ParseViewDimensionLayerRules(args));
        Assert.Equal("batten", response.GetProperty("compositionPlan").GetProperty("partClassification").GetProperty("parts")[0].GetProperty("className").GetString());
        Assert.Equal(context.Query("chainDetails", "Bottom", ruleSet: "panel").GetProperty("chainPreview").GetRawText(),
            response.GetProperty("chainPreview").GetRawText());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[]")]
    public void EmptyRulesAndOlderBridgeArgumentsKeepTheCompactPlan(string json)
    {
        var args = Args(json);
        Assert.Empty(DrawingCommandParsers.ParseViewDimensionLayerRules(args));
        Assert.Empty(DrawingCommandParsers.ParseViewDimensionLayerRules(args.Take(12).ToArray()));
        var response = ViewDimensionContextTests.Context().Query("chainDetails", ruleSet: "panel",
            layerRules: DrawingCommandParsers.ParseViewDimensionLayerRules(args));
        Assert.False(response.GetProperty("compositionPlan").TryGetProperty("partClassification", out _));
        Assert.Equal("", typeof(ModelTools).GetMethod(nameof(ModelTools.GetViewDimensionContext))!
            .GetParameters().Single(p => p.Name == "layerRules").DefaultValue);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"id\":\"x\",\"className\":\"c\",\"conditions\":[]}]")]
    [InlineData("[{\"id\":\"x\",\"className\":\"c\",\"conditions\":[null]}]")]
    [InlineData("[{\"id\":\"x\",\"className\":\"c\",\"conditions\":[{\"property\":\"Name\",\"value\":\" \"}]}]")]
    [InlineData("[{\"id\":\"x\",\"className\":\"c\",\"conditions\":[{\"property\":\"Future\",\"value\":\"x\"}]}]")]
    [InlineData("[{\"id\":\"x\",\"className\":\"c\",\"conditions\":[{\"property\":\"Name\",\"matchKind\":\"Regex\",\"value\":\"x\"}]}]")]
    public void InvalidJsonIsRejectedByMcpBeforeBridgeAndByBridgeBeforeGeometry(string json)
    {
        Assert.Throws<ArgumentException>(() => ModelTools.GetViewDimensionContext(7, "chainDetails", ruleSet: "panel", layerRules: json));
        var args = Args("");
        args[12] = json;
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseViewDimensionLayerRules(args));
    }

    [Theory]
    [InlineData("\"priority\":1.5,")]
    [InlineData("\"priority\":2147483648,")]
    [InlineData("\"priority\":\"10\",")]
    [InlineData("\"data\":null,")]
    [InlineData("\"data\":[],")]
    [InlineData("\"data\":{\"\":1},")]
    [InlineData("\"data\":{\"x\":1,\"x\":2},")]
    [InlineData("\"unknown\":true,")]
    [InlineData("\"id\":\"duplicate-field\",")]
    public void InvalidOptionalFieldsAreNeverSilentlyIgnored(string field)
    {
        var json = "[{\"id\":\"r\",\"className\":\"c\"," + field + "\"conditions\":[{\"property\":\"Name\",\"value\":\"BATTEN\"}]}]";
        Assert.Throws<ArgumentException>(() => Args(json));
        var args = Args(""); args[12] = json;
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseViewDimensionLayerRules(args));
    }

    [Fact]
    public void DuplicateRuleIdsAreRejectedAtBothBoundaries()
    {
        var rule = "{\"id\":\"r\",\"className\":\"c\",\"conditions\":[{\"property\":\"Name\",\"value\":\"BATTEN\"}]}";
        var json = "[" + rule + "," + rule + "]";
        Assert.Throws<ArgumentException>(() => Args(json));
        var args = Args(""); args[12] = json;
        Assert.Throws<ArgumentException>(() => DrawingCommandParsers.ParseViewDimensionLayerRules(args));
    }

    [Fact]
    public void WireVocabularyCoversEverySupportedDomainPropertyAndMatchKind()
    {
        foreach (var property in Enum.GetValues<PartLayerProperty>())
        foreach (var match in Enum.GetValues<PartLayerMatchKind>())
        {
            var json = JsonSerializer.Serialize(new[] { new { id = "r", className = "c", conditions = new[] {
                new { property = property.ToString().ToLowerInvariant(), matchKind = match.ToString().ToLowerInvariant(), value = "x" }
            } } });
            var condition = Assert.Single(Assert.Single(DrawingCommandParsers.ParseViewDimensionLayerRules(Args(json))).Conditions);
            Assert.Equal(property, condition.Property);
            Assert.Equal(match, condition.MatchKind);
        }
    }
}
