using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>The bolt-chain settings file, read once per context, and the values kept with the context.</summary>
public sealed class BoltChainSettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "svmcp-bolt-settings-" + Guid.NewGuid().ToString("N"));

    public BoltChainSettingsFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string File(string text)
    {
        var path = Path.Combine(_directory, "bolt-chains.json");
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void NoFileGivesTheDefaults()
    {
        var settings = BoltChainSettingsFile.Load(Path.Combine(_directory, "missing.json"));
        Assert.Equal(BoltPositionTo.MainPart, settings.PositionBoltsTo);
        Assert.Equal(BoltSecondaryPartPosition.ByBolt, settings.SecondaryPart);
    }

    [Fact]
    public void TheFileSwitchesBackToTodaysChains()
    {
        var settings = BoltChainSettingsFile.Load(File("""
            // Back to the edge chains along the main part.
            { "positionBoltsTo": "None", "secondaryPart": "ByPart", "combineMinDistance": 120 }
            """));
        Assert.Equal(BoltPositionTo.None, settings.PositionBoltsTo);
        Assert.Equal(BoltSecondaryPartPosition.ByPart, settings.SecondaryPart);
        Assert.Equal(120, settings.CombineMinDistance);
        // A value left out keeps its default.
        Assert.Equal(BoltInternalDimensions.All, settings.MainPartInternal);
    }

    [Theory]
    [InlineData("{ \"positionBoltsTo\": ", "cannot be read")]
    [InlineData("{ \"positionBoltsTo\": \"Reference\" }", "PositionBoltsTo \"Reference\" is not one of")]
    [InlineData("{ \"positionBoltsTo\": \"7\" }", "PositionBoltsTo \"7\" is not one of")]
    [InlineData("{ \"positionBoltTo\": \"None\" }", "cannot be read")]
    [InlineData("{ \"classToleranceDegrees\": 50 }", "Class tolerance")]
    [InlineData("null", "is empty")]
    public void ABrokenFileIsAnErrorNamingIt(string text, string reason)
    {
        var path = File(text);
        var error = Assert.Throws<InvalidOperationException>(() => BoltChainSettingsFile.Load(path));
        Assert.Contains(path, error.Message);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void TheContextKeepsItsSettingsAndTheSavedCaseCarriesThem()
    {
        var settings = new BoltChainSettings(positionBoltsTo: BoltPositionTo.None, secondaryPart: BoltSecondaryPartPosition.ByPart);
        var original = DimensionChainPreviewTests.Context(76, boltSettings: settings);
        var shown = original.Query("chain").GetProperty("boltSettings");
        Assert.Equal("None", shown.GetProperty("PositionBoltsTo").GetString());
        Assert.Equal("ByPart", shown.GetProperty("SecondaryPart").GetString());
        var replayed = JsonSerializer.Deserialize<ViewDimensionContextCase>(JsonSerializer.Serialize(original.ToCase()))!.ToContext();
        Assert.Equal(shown.GetRawText(), replayed.Query("chain").GetProperty("boltSettings").GetRawText());
    }
}
