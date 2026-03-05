using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutEngine
{
    private readonly IMarkCandidateGenerator _candidateGenerator;
    private readonly IMarkCostEvaluator _costEvaluator;
    private readonly MarkOverlapResolver _overlapResolver;

    public MarkLayoutEngine(
        IMarkCandidateGenerator? candidateGenerator = null,
        IMarkCostEvaluator? costEvaluator = null,
        MarkOverlapResolver? overlapResolver = null)
    {
        _candidateGenerator = candidateGenerator ?? new SimpleMarkCandidateGenerator();
        _costEvaluator = costEvaluator ?? new SimpleMarkCostEvaluator();
        _overlapResolver = overlapResolver ?? new MarkOverlapResolver();
    }

    public MarkLayoutResult Arrange(
        IEnumerable<MarkLayoutItem> items,
        MarkLayoutOptions? options = null)
    {
        var layoutOptions = options ?? new MarkLayoutOptions();
        var sourceItems = items.ToList();
        var placements = new List<MarkLayoutPlacement>();
        var conflictCounts = CalculateConflictCounts(sourceItems, layoutOptions);

        foreach (var item in sourceItems.Where(x => !x.CanMove))
        {
            placements.Add(CreatePlacement(item, item.CurrentX, item.CurrentY));
        }

        foreach (var item in OrderMovableItems(sourceItems, conflictCounts))
        {
            var candidates = _candidateGenerator.GenerateCandidates(item, layoutOptions);
            var best = candidates
                .Select(candidate =>
                {
                    candidate.Score = _costEvaluator.EvaluateCandidate(item, candidate, placements, layoutOptions);
                    return candidate;
                })
                .OrderBy(candidate => candidate.Score)
                .First();

            placements.Add(CreatePlacement(item, best.X, best.Y));
        }

        var iterations = 0;
        if (layoutOptions.EnableOverlapResolver)
            placements = _overlapResolver.Resolve(placements, layoutOptions, out iterations);

        return new MarkLayoutResult
        {
            Placements = placements,
            Iterations = iterations,
            RemainingOverlaps = _overlapResolver.CountOverlaps(placements)
        };
    }

    private static IEnumerable<MarkLayoutItem> OrderMovableItems(
        IReadOnlyList<MarkLayoutItem> items,
        IReadOnlyDictionary<int, int> conflictCounts)
    {
        return items
            .Where(x => x.CanMove)
            .OrderByDescending(x => conflictCounts.TryGetValue(x.Id, out var conflicts) ? conflicts : 0)
            .ThenByDescending(x => x.Width * x.Height)
            .ThenByDescending(x => x.HasLeaderLine);
    }

    private static Dictionary<int, int> CalculateConflictCounts(
        IReadOnlyList<MarkLayoutItem> items,
        MarkLayoutOptions options)
    {
        var counts = items.ToDictionary(x => x.Id, _ => 0);

        for (var i = 0; i < items.Count; i++)
        for (var j = i + 1; j < items.Count; j++)
        {
            if (!Intersects(items[i], items[j], options.Gap))
                continue;

            counts[items[i].Id]++;
            counts[items[j].Id]++;
        }

        return counts;
    }

    private static bool Intersects(MarkLayoutItem a, MarkLayoutItem b, double gap)
    {
        var halfWidthA = (a.Width / 2.0) + (gap / 2.0);
        var halfHeightA = (a.Height / 2.0) + (gap / 2.0);
        var halfWidthB = (b.Width / 2.0) + (gap / 2.0);
        var halfHeightB = (b.Height / 2.0) + (gap / 2.0);

        var overlapX = Math.Min(a.CurrentX + halfWidthA, b.CurrentX + halfWidthB)
            - Math.Max(a.CurrentX - halfWidthA, b.CurrentX - halfWidthB);
        var overlapY = Math.Min(a.CurrentY + halfHeightA, b.CurrentY + halfHeightB)
            - Math.Max(a.CurrentY - halfHeightA, b.CurrentY - halfHeightB);

        return overlapX > 0 && overlapY > 0;
    }

    private static MarkLayoutPlacement CreatePlacement(MarkLayoutItem item, double x, double y)
    {
        return new MarkLayoutPlacement
        {
            Id = item.Id,
            X = x,
            Y = y,
            Width = item.Width,
            Height = item.Height,
            HasLeaderLine = item.HasLeaderLine,
            CanMove = item.CanMove
        };
    }
}
