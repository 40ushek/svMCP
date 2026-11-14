namespace TeklaMcpServer.Api.Drawing;

/// <summary>A candidate and its selection outcome. Geometry and source identity stay on the candidate.</summary>
internal sealed class BoltChainDecision(BoltChainCandidate candidate, string? policyRefusal = null,
    string? viewRefusal = null, string? givenBy = null)
{
    public BoltChainCandidate Candidate { get; } = candidate;
    public string? PolicyRefusal { get; } = policyRefusal;
    public string? ViewRefusal { get; } = viewRefusal;
    public string? GivenBy { get; } = givenBy;
    // A position chain's coverage takes precedence, including for edgeless chains that must not
    // subsequently be carried into a structural location chain.
    public string? Reason => GivenBy ?? PolicyRefusal ?? ViewRefusal;
    public bool Create => Reason == null;

    public BoltChainDecision WithViewRefusal(string? reason) => new(Candidate, PolicyRefusal, reason, GivenBy);
    public BoltChainDecision WithPositionCoverage(string key) =>
        new(Candidate, PolicyRefusal, ViewRefusal, $"bolt positions are given by {key}");
}
