using System.Linq;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutOptions
{
    private static readonly double[] DefaultCandidateDistanceMultipliers = { 1.0, 1.5, 2.25 };
    private double[] _candidateDistanceMultipliers = DefaultCandidateDistanceMultipliers;

    public double Gap { get; set; } = 2.0;

    public double CandidateOffset { get; set; } = 4.0;

    /// <summary>
    /// Multipliers applied to the base candidate offset
    /// ((mark half-size) + CandidateOffset + Gap) to generate several rings of positions.
    /// Invalid values are ignored; if nothing valid remains, defaults are restored.
    /// </summary>
    public double[] CandidateDistanceMultipliers
    {
        get => _candidateDistanceMultipliers;
        set => _candidateDistanceMultipliers = NormalizeCandidateDistanceMultipliers(value);
    }

    public double CurrentPositionWeight { get; set; } = 0.15;

    public double AnchorDistanceWeight { get; set; } = 0.0;

    public double SourceDistanceWeight { get; set; } = 0.0;

    public double SourceOutsideOwnPartPenalty { get; set; } = 0.0;

    public double ForeignPartOverlapPenalty { get; set; } = 0.0;

    /// <summary>
    /// Maximum allowed distance from mark anchor to generated candidate center.
    /// Set <= 0 to disable.
    /// </summary>
    public double MaxDistanceFromAnchor { get; set; } = 0.0;

    public double LeaderLengthWeight { get; set; } = 0.05;

    public double CandidatePriorityWeight { get; set; } = 0.25;

    public double CrowdingPenaltyWeight { get; set; } = 5.0;

    public double PreferredSidePenaltyWeight { get; set; } = 0.75;

    public double OverlapPenalty { get; set; } = 1_000_000.0;

    public bool EnableOverlapResolver { get; set; } = true;

    public int MaxResolverIterations { get; set; } = 10;

    internal DrawingViewContext? ViewContext { get; set; }

    internal Dictionary<int, List<double[]>> PartPolygonsByModelId { get; set; } = [];

    private static double[] NormalizeCandidateDistanceMultipliers(double[]? values)
    {
        if (values == null || values.Length == 0)
            return DefaultCandidateDistanceMultipliers.ToArray();

        var normalized = values
            .Where(x => x > 0 && !double.IsNaN(x) && !double.IsInfinity(x))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return normalized.Length == 0
            ? DefaultCandidateDistanceMultipliers.ToArray()
            : normalized;
    }
}
