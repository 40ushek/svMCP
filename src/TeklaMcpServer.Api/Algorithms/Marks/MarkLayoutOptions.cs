namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkLayoutOptions
{
    public double Gap { get; set; } = 2.0;

    public double CandidateOffset { get; set; } = 4.0;

    public double CurrentPositionWeight { get; set; } = 0.15;

    public double LeaderLengthWeight { get; set; } = 0.05;

    public double CandidatePriorityWeight { get; set; } = 0.25;

    public double CrowdingPenaltyWeight { get; set; } = 5.0;

    public double PreferredSidePenaltyWeight { get; set; } = 0.75;

    public double OverlapPenalty { get; set; } = 1_000_000.0;

    public bool EnableOverlapResolver { get; set; } = true;

    public int MaxResolverIterations { get; set; } = 10;
}
