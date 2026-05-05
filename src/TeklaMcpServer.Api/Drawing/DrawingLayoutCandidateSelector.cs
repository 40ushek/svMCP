using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingLayoutCandidateSelection
{
    public DrawingLayoutCandidateEvaluation? Selected { get; set; }

    public List<DrawingLayoutCandidateEvaluation> Evaluations { get; set; } = new();

    public List<string> Diagnostics { get; set; } = new();
}

internal sealed class DrawingLayoutCandidateSelector
{
    private readonly DrawingLayoutScorer scorer;

    public DrawingLayoutCandidateSelector()
        : this(new DrawingLayoutScorer())
    {
    }

    public DrawingLayoutCandidateSelector(DrawingLayoutScorer scorer)
    {
        this.scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
    }

    public DrawingLayoutCandidateSelection SelectBest(
        IEnumerable<DrawingLayoutCandidate> candidates,
        DrawingLayoutScoreWeights? weights = null)
    {
        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));

        var selection = new DrawingLayoutCandidateSelection();
        var indexedEvaluations = candidates
            .Select((candidate, index) => (Evaluation: scorer.Evaluate(candidate, weights), Index: index))
            .ToList();

        selection.Evaluations = indexedEvaluations
            .Select(static item => item.Evaluation)
            .ToList();

        if (indexedEvaluations.Count == 0)
        {
            selection.Diagnostics.Add("candidate-selection:no-candidates");
            return selection;
        }

        var selected = indexedEvaluations
            .OrderByDescending(static item => item.Evaluation.IsFeasible)
            .ThenByDescending(static item => item.Evaluation.Score.TotalScore)
            .ThenBy(static item => item.Index)
            .First();

        selection.Selected = selected.Evaluation;
        selection.Diagnostics.Add(string.Format(
            CultureInfo.InvariantCulture,
            "candidate-selection:selected:index={0}:name={1}:feasible={2}:score={3:0.###}",
            selected.Index,
            FormatName(selected.Evaluation.Candidate),
            selected.Evaluation.IsFeasible ? 1 : 0,
            selected.Evaluation.Score.TotalScore));

        return selection;
    }

    private static string FormatName(DrawingLayoutCandidate candidate)
        => string.IsNullOrWhiteSpace(candidate.Name) ? "unnamed" : candidate.Name;
}
