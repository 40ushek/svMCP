using TeklaMcpServer.Api.Drawing.Dimensions;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>A selected chain with its final line offset, ready for preview projection.</summary>
internal sealed class BoltRowChain(BoltChainDecision decision, double distance)
{
    public BoltChainDecision Decision { get; } = decision;
    public double Distance { get; } = distance;
    public string Key => Decision.Candidate.Key;
    public string? Reason => Decision.Reason;
    public bool Create => Decision.Create;
    public object Project() => BoltChainPreviewProjection.Project(Decision, Distance);
}

/// <summary>Assigns free rows only to selected bolt chains, retaining candidate order and
/// measuring each offset from the first point. Does not decide coverage or dependencies.</summary>
internal static class BoltChainRowPlanner
{
    internal static IReadOnlyList<BoltRowChain> Place(IReadOnlyList<BoltChainDecision> decisions,
        List<(double Nx, double Ny, double Line, double From, double To)> occupied, double gap) =>
        decisions.Select(decision => {
            var candidate = decision.Candidate;
            var distance = gap + candidate.BaseOffset;
            if (decision.Create && candidate.Points.Count > 0)
                distance = NextFreeRow(occupied, candidate, distance, gap);
            return new BoltRowChain(decision, distance);
        }).ToArray();

    // Bolt chains on one side whose lines would fall on one another and whose spans overlap are
    // moved out to one paper gap beyond the line in the way, so a chain whose points stand a little
    // off the outline does not lose a row. Tekla measures the offset from the chain's first point.
    private static double NextFreeRow(List<(double Nx, double Ny, double Line, double From, double To)> rows,
        BoltChainCandidate candidate, double distance, double gap)
    {
        var normal = candidate.OutwardNormal;
        var first = candidate.Points[0];
        var along = candidate.Points.Select(point => point.X * -normal.Y + point.Y * normal.X).ToArray();
        var (from, to) = (along.Min(), along.Max());
        var start = first.X * normal.X + first.Y * normal.Y;
        while (rows.Where(row => Math.Abs(row.Nx - normal.X) < 1e-9 && Math.Abs(row.Ny - normal.Y) < 1e-9
                   // One gap apart is a free row: rounded points must not push a line one row further.
                   && Math.Abs(row.Line - (start + distance)) < gap - DimensionPlacementSettings.VerificationToleranceViewUnits
                   && row.From <= to && from <= row.To)
               .Select(row => (double?)row.Line).Max() is { } inTheWay)
            distance = inTheWay + gap - start;
        double Line(double d) => start + d;
        rows.Add((normal.X, normal.Y, Line(distance), from, to));
        return distance;
    }

}
