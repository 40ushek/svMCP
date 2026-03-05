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

        foreach (var item in sourceItems.Where(x => !x.CanMove))
        {
            placements.Add(CreatePlacement(item, item.CurrentX, item.CurrentY));
        }

        foreach (var item in sourceItems
                     .Where(x => x.CanMove)
                     .OrderByDescending(x => x.Width * x.Height)
                     .ThenByDescending(x => x.HasLeaderLine))
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

        if (layoutOptions.EnableOverlapResolver)
            placements = _overlapResolver.Resolve(placements, layoutOptions);

        return new MarkLayoutResult
        {
            Placements = placements,
            RemainingOverlaps = _overlapResolver.CountOverlaps(placements)
        };
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
