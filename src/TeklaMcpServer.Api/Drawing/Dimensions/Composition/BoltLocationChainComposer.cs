namespace TeklaMcpServer.Api.Drawing;

/// <summary>Carries edgeless Plan bolts into structural location chains. Typed inputs retain
/// the original order, refusal precedence and overall restoration; no preview JSON is read.</summary>
internal static class BoltLocationChainComposer
{
    internal static DimensionPreviewRow[] Compose(DimensionPreviewRow[] rows, double tolerance,
        Func<string[], string, double[]> resolvePoints, Func<string, double[], double> distance)
    {
        var boltIndex = Array.FindIndex(rows, row => row.Side == ViewDimensionContext.BoltSide);
        if (boltIndex < 0 || rows[boltIndex].ReadIncomplete != null) return rows;
        var working = rows.Select(row => row.Chains.ToList()).ToArray();
        var carried = new Dictionary<DimensionChainSide, DimensionPreviewChain>();
        foreach (var chain in rows[boltIndex].Chains)
        {
            if (!chain.Incomplete || chain.Reason != ViewDimensionContext.BoltEdgeless
                || chain.ViewClass != BoltGroupViewClass.Plan || chain.ChainKind != BoltChainCandidate.PartKind
                || chain.PlacementSide is not { } side) continue;
            var captured = chain.Sources.CapturedPositions;
            var bolts = captured.Select(position => new[] { position.Point[0], position.Point[1] }).ToArray();
            if (bolts.Length == 0) continue;
            var rowIndex = Array.FindIndex(rows, row => row.Side == side.ToString());
            if (rowIndex < 0) continue;
            carried.TryGetValue(side, out var existing);
            var location = existing != null
                ? working[rowIndex].First(candidate => candidate.Key == existing.Carries)
                : working[rowIndex].FirstOrDefault(candidate => candidate.Kind == "location" && !candidate.Incomplete);
            if (location?.Direction == null || existing == null && location.PointIds.Length < 2) continue;
            double[][] points;
            if (existing != null) points = existing.Points;
            else
            {
                var flat = resolvePoints(location.PointIds, location.Direction);
                points = Enumerable.Range(0, flat.Length / 3).Select(i => new[] { flat[i * 3], flat[i * 3 + 1] }).ToArray();
            }
            var axis = side.RunsAlongX() ? 0 : 1;
            var (from, to) = (points.Min(point => point[axis]), points.Max(point => point[axis]));
            if (bolts.Any(point => point[axis] <= from + tolerance || point[axis] >= to - tolerance)) continue;
            var merged = points.Concat(bolts).OrderBy(point => point[axis]).Aggregate(new List<double[]>(), (list, point) => {
                if (list.Count == 0 || point[axis] - list[list.Count - 1][axis] > tolerance) list.Add(point);
                return list;
            }).ToArray();
            var segments = merged.Skip(1).Select((point, index) => Math.Round(point[axis] - merged[index][axis], 3)).ToArray();
            var sources = (existing?.Sources.CapturedPositions ?? [])
                .Concat(captured).ToArray();
            var target = DimensionPreviewChain.Carried(location, existing?.PartId ?? chain.PartId, side, merged, segments,
                distance(location.Direction, merged.SelectMany(point => new[] { point[0], point[1], 0d }).ToArray()), sources);
            if (existing == null)
            {
                working[boltIndex].Add(target);
                Replace(working[rowIndex], location.Refused("carried with the bolts of its part by " + target.Key));
            }
            else Replace(working[boltIndex], target);
            carried[side] = target;
            Replace(working[boltIndex], chain.Refused(ViewDimensionContext.BoltEdgeless + "; its bolts are carried by " + target.Key));
        }
        if (carried.Count == 0) return rows;
        foreach (var pair in carried)
        {
            var rowIndex = Array.FindIndex(rows, row => row.Side == pair.Key.ToString());
            var location = working[rowIndex].First(chain => chain.Key == pair.Value.Carries);
            if (location.PointIds.Length != 2 || pair.Value.Points.Length <= 2) continue;
            var overall = working[rowIndex].FirstOrDefault(chain => chain.Kind == "overall" && chain.Incomplete
                && chain.Reason?.EndsWith(DimensionChainPreview.OverallSpanned, StringComparison.Ordinal) == true);
            if (overall != null) Replace(working[rowIndex], overall.Restore(location, pair.Value.Key));
        }
        return rows.Select((row, index) => row.WithChains(working[index].Select(chain => chain.PreserveTemplatePrecision()))).ToArray();
    }

    private static void Replace(List<DimensionPreviewChain> chains, DimensionPreviewChain replacement) =>
        chains[chains.FindIndex(chain => chain.Key == replacement.Key)] = replacement;
}
