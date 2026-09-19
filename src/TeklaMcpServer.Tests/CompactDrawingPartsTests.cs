using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The compact parts answer hides and merges, so what it must never do is lose an object or
/// blur what was read. Held on the serialized JSON, which is what a caller reads.
/// </summary>
public sealed class CompactDrawingPartsTests
{
    private static DrawingPartInfo Part(int id, string type, string pos = "P/1", string prefix = "P",
        bool prefixKnown = true, string profile = "BLE10*100") => new()
    {
        ModelId = id, Type = type, PartPos = pos, PartPrefix = prefix, PartPrefixKnown = prefixKnown,
        AssemblyPos = "M/1", Profile = profile, Material = "S235JR", Name = "Rippe"
    };

    private static JsonElement Run(IReadOnlyList<DrawingPartInfo> parts, string? include = null) =>
        JsonSerializer.SerializeToElement(CompactDrawingParts.Project(parts, parts.Count, include));

    [Fact]
    public void IdenticalRecordsMergeAndKeepEveryModelId()
    {
        var wire = Run([Part(1, "Beam"), Part(2, "Beam"), Part(3, "Beam")]);

        var entry = Assert.Single(wire.GetProperty("parts").EnumerateArray());
        Assert.Equal(3, entry.GetProperty("count").GetInt32());
        Assert.Equal([1, 2, 3], entry.GetProperty("modelIds").EnumerateArray().Select(i => i.GetInt32()));
    }

    [Fact]
    public void AnUnreadPrefixIsNeverMergedWithAReadOne()
    {
        var wire = Run([Part(1, "Beam"), Part(2, "Beam", prefix: "", prefixKnown: false), Part(3, "Beam", prefix: "")]);

        var entries = wire.GetProperty("parts").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Single(entries, e => !e.GetProperty("partPrefixKnown").GetBoolean());
    }

    [Fact]
    public void AnyDifferingFieldKeepsRecordsApart()
    {
        var wire = Run([Part(1, "Beam"), Part(2, "Beam", profile: "BLE20*100"), Part(3, "ContourPlate")]);

        Assert.Equal(3, wire.GetProperty("returnedGroups").GetInt32());
    }

    [Fact]
    public void HiddenTypesAreCountedByTypeNotDropped()
    {
        var wire = Run([Part(1, "Beam"), Part(2, "BoltArray"), Part(3, "BoltArray"), Part(4, "ReferenceModel")]);

        Assert.Equal(4, wire.GetProperty("total").GetInt32());
        Assert.Equal(1, wire.GetProperty("returnedObjects").GetInt32());
        Assert.Equal(3, wire.GetProperty("hiddenObjects").GetInt32());
        Assert.Equal(2, wire.GetProperty("hiddenByType").GetProperty("BoltArray").GetInt32());
        Assert.Equal(1, wire.GetProperty("hiddenByType").GetProperty("ReferenceModel").GetInt32());
    }

    [Fact]
    public void TheCountsAlwaysAccountForEveryObject()
    {
        var wire = Run([Part(1, "Beam"), Part(2, "Beam"), Part(3, "Connection"), Part(4, "EdgeChamfer"), Part(5, "ContourPlate")]);

        Assert.Equal(wire.GetProperty("total").GetInt32(),
            wire.GetProperty("returnedObjects").GetInt32() + wire.GetProperty("hiddenObjects").GetInt32());
        Assert.Equal(wire.GetProperty("returnedObjects").GetInt32(),
            wire.GetProperty("parts").EnumerateArray().Sum(e => e.GetProperty("count").GetInt32()));
    }

    [Fact]
    public void IncludeTypesBringsNamedTypesBackAndAllBringsEverything()
    {
        var parts = new[] { Part(1, "Beam"), Part(2, "BoltArray"), Part(3, "Connection") };

        var named = Run(parts, "boltarray");
        Assert.Equal(2, named.GetProperty("returnedObjects").GetInt32());
        Assert.False(named.GetProperty("hiddenByType").TryGetProperty("BoltArray", out _));
        Assert.True(named.GetProperty("hiddenByType").TryGetProperty("Connection", out _));

        var all = Run(parts, "all");
        Assert.Equal(3, all.GetProperty("returnedObjects").GetInt32());
        Assert.Equal(0, all.GetProperty("hiddenObjects").GetInt32());
    }

    [Fact]
    public void TheAnswerNamesItsScopeAndKeepsThePartsKey()
    {
        var wire = Run([Part(1, "Beam")]);

        Assert.Equal("drawing", wire.GetProperty("scope").GetString());
        Assert.Equal("compact", wire.GetProperty("format").GetString());
        Assert.True(wire.TryGetProperty("parts", out _));
    }
}
