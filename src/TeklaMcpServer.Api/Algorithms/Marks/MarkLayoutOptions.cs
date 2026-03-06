namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutOptions
{
    public double Gap { get; set; } = 2.0;

    public double CandidateOffset { get; set; } = 4.0;

    /// <summary>
    /// Multipliers applied to CandidateOffset to generate candidates at several distances.
    /// Each level produces a ring of positions; more levels help in dense drawings where
    /// the nearest ring is fully blocked.
    /// </summary>
    public double[] CandidateDistanceMultipliers { get; set; } = { 1.0, 1.5, 2.25 };

    public double CurrentPositionWeight { get; set; } = 0.15;

    public double LeaderLengthWeight { get; set; } = 0.05;

    public double CandidatePriorityWeight { get; set; } = 0.25;

    public double CrowdingPenaltyWeight { get; set; } = 5.0;

    public double PreferredSidePenaltyWeight { get; set; } = 0.75;

    public double OverlapPenalty { get; set; } = 1_000_000.0;

    public bool EnableOverlapResolver { get; set; } = true;

    public int MaxResolverIterations { get; set; } = 10;
}
