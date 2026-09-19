using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The compact answer is only a shorter way to say the same thing. A preview resolves a decision
/// against `source.Positions[i].Supports[j]`, so merging duplicates must never move, drop or
/// renumber a support: every original index has to be reachable exactly once, and each index has
/// to keep the kind, part and point of the support it names. The wire shape is pinned on the
/// serialized JSON, since that, not the in-memory dictionary, is what a caller reads.
/// </summary>
public sealed class CompactChainPositionsTests
{
    private static GeometryGroup Group()
    {
        var boundary = Shape("outline", false, null, (0, 0), (200, 0), (200, 100), (0, 100));
        var plate = Shape("part:11:outer", false, 11, (0, 0), (100, 0), (100, 50), (0, 50));
        var hole = Shape("part:11:hole", true, 11, (40, 10), (60, 10), (60, 40), (40, 40));
        var raked = Shape("part:12:outer", false, 12, (120, 0), (160, 0), (180, 60), (120, 60));
        var group = new GeometryGroup("assembly", [boundary], [plate, hole, raked]);
        CalcDimensionChains.Apply(group);
        return group;
    }

    private static JsonElement Wire(DimensionChain chain, System.Func<DimensionChainPositionSupport, double?>? extent = null) =>
        JsonSerializer.SerializeToElement(CompactChainPositions.Project(chain, extent ?? (_ => null)));

    [Fact]
    public void EveryOriginalSupportIndexIsReachableExactlyOnce()
    {
        foreach (var chain in Group().DimensionChains!.Chains)
        {
            var wire = Wire(chain);
            Assert.Equal(chain.Positions.Count, wire.GetArrayLength());
            for (var p = 0; p < chain.Positions.Count; p++)
            {
                var position = wire[p];
                Assert.Equal(p, position.GetProperty("positionIndex").GetInt32());
                var indices = position.GetProperty("supports").EnumerateArray()
                    .SelectMany(s => s.GetProperty("refs").EnumerateArray())
                    .Select(r => r.GetProperty("supportIndex").GetInt32())
                    .OrderBy(i => i).ToList();
                Assert.Equal(Enumerable.Range(0, chain.Positions[p].Supports.Count), indices);
            }
        }
    }

    [Fact]
    public void EachIndexKeepsTheKindPartAndPointOfTheSupportItNames()
    {
        foreach (var chain in Group().DimensionChains!.Chains)
        {
            var wire = Wire(chain);
            for (var p = 0; p < chain.Positions.Count; p++)
            foreach (var entry in wire[p].GetProperty("supports").EnumerateArray())
            {
                var point = entry.GetProperty("point");
                foreach (var reference in entry.GetProperty("refs").EnumerateArray())
                {
                    var original = chain.Positions[p].Supports[reference.GetProperty("supportIndex").GetInt32()];
                    Assert.Equal(original.Kind.ToString(), reference.GetProperty("kind").GetString());
                    Assert.Equal(original.ModelId, entry.GetProperty("modelId").ValueKind == JsonValueKind.Null
                        ? null : entry.GetProperty("modelId").GetInt32());
                    Assert.Equal(original.Point.X, point[0].GetDouble());
                    Assert.Equal(original.Point.Y, point[1].GetDouble());
                    Assert.Equal(original.Source.IsHole, entry.TryGetProperty("isHole", out _));
                }
            }
        }
    }

    [Fact]
    public void TwoKindsAtOnePointStayPairedWithTheirOwnIndex()
    {
        var merged = Group().DimensionChains!.Chains
            .SelectMany(chain => Wire(chain).EnumerateArray())
            .SelectMany(position => position.GetProperty("supports").EnumerateArray())
            .Where(entry => entry.GetProperty("refs").GetArrayLength() > 1)
            .ToList();

        Assert.NotEmpty(merged);
        Assert.All(merged, entry => Assert.All(entry.GetProperty("refs").EnumerateArray(), reference =>
        {
            Assert.True(reference.TryGetProperty("supportIndex", out _));
            Assert.True(reference.TryGetProperty("kind", out _));
        }));
    }

    [Fact]
    public void SupportsAreMergedOnlyWhenTheyAgreeOnPartPointExtentAndHoleFlag()
    {
        var chain = Group().DimensionChains![DimensionChainSide.Top];
        var wire = Wire(chain, s => s.ModelId == 11 ? 100d : null);

        foreach (var position in wire.EnumerateArray())
        {
            var keys = position.GetProperty("supports").EnumerateArray().Select(e => (
                e.GetProperty("modelId").ToString(),
                e.GetProperty("point")[0].GetDouble(),
                e.GetProperty("point")[1].GetDouble(),
                e.GetProperty("partExtentAlongChain").ToString(),
                e.TryGetProperty("isHole", out _))).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void TheWireShapeCarriesNoSourceIdNoTopLevelIndexAndNoFalseIsHole()
    {
        var entries = Group().DimensionChains!.Chains
            .SelectMany(chain => Wire(chain).EnumerateArray())
            .SelectMany(position => position.GetProperty("supports").EnumerateArray())
            .ToList();

        Assert.All(entries, e =>
        {
            Assert.False(e.TryGetProperty("sourceId", out _));
            Assert.False(e.TryGetProperty("supportIndex", out _));
            Assert.False(e.TryGetProperty("kinds", out _));
            Assert.True(e.TryGetProperty("refs", out _));
        });
        Assert.Contains(entries, e => !e.TryGetProperty("isHole", out _));
    }

    private static GeometryGroupShape Shape(string id, bool isHole, int? modelId, params (double X, double Y)[] points) =>
        new(id, RegionFlattener.Flatten(points.Select(point => new Vec3(point.X, point.Y, 0)).ToList()), isHole, modelId);
}
