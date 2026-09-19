using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The default, smaller answer of `get_structural_chain_positions`. Nothing a decision needs is
/// dropped: supports are merged only when they agree on part, point, axis-aligned extent and hole
/// flag, and the merged entry states the point once and lists every original support as
/// `refs: [{supportIndex, kind}]`, so each kind stays attached to the index it belongs to. What is
/// left out is `sourceId` (a ring path nobody plans from) and a false `isHole`; `verbose` restores
/// them.
///
/// `positionIndex` and every `refs[].supportIndex` stay indices into the calculated source,
/// because a preview resolves a decision against `source.Positions[i].Supports[j]`. There is no
/// entry-level `supportIndex`: a plan may name any listed one, and none of them is "the" index.
/// </summary>
public static class CompactChainPositions
{
    public static IReadOnlyList<Dictionary<string, object?>> Project(
        DimensionChain chain,
        Func<DimensionChainPositionSupport, double?> extentAlongChain)
    {
        if (chain == null) throw new ArgumentNullException(nameof(chain));
        if (extentAlongChain == null) throw new ArgumentNullException(nameof(extentAlongChain));

        var positions = new List<Dictionary<string, object?>>();
        for (var positionIndex = 0; positionIndex < chain.Positions.Count; positionIndex++)
        {
            var position = chain.Positions[positionIndex];
            positions.Add(new Dictionary<string, object?>
            {
                ["positionIndex"] = positionIndex,
                ["coordinate"] = position.Coordinate,
                ["supports"] = MergeSupports(position.Supports, extentAlongChain)
            });
        }

        return positions;
    }

    private static List<Dictionary<string, object?>> MergeSupports(
        IReadOnlyList<DimensionChainPositionSupport> supports,
        Func<DimensionChainPositionSupport, double?> extentAlongChain)
    {
        var order = new List<(int? ModelId, double X, double Y, double? Extent, bool IsHole)>();
        var refs = new Dictionary<(int? ModelId, double X, double Y, double? Extent, bool IsHole),
            List<Dictionary<string, object?>>>();

        for (var index = 0; index < supports.Count; index++)
        {
            var support = supports[index];
            var key = (support.ModelId, support.Point.X, support.Point.Y,
                extentAlongChain(support), support.Source.IsHole);
            if (!refs.TryGetValue(key, out var group))
            {
                order.Add(key);
                refs[key] = group = new List<Dictionary<string, object?>>();
            }

            group.Add(new Dictionary<string, object?>
            {
                ["supportIndex"] = index,
                ["kind"] = support.Kind.ToString()
            });
        }

        var merged = new List<Dictionary<string, object?>>();
        foreach (var key in order)
        {
            var entry = new Dictionary<string, object?>
            {
                ["modelId"] = key.ModelId,
                ["partExtentAlongChain"] = key.Extent
            };
            if (key.IsHole)
                entry["isHole"] = true;
            entry["point"] = new[] { key.X, key.Y };
            entry["refs"] = refs[key];
            merged.Add(entry);
        }

        return merged;
    }
}
