using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>A point proposed for a chain, with the reason it was proposed.</summary>
internal sealed class ChainPick(DimensionPoint point, string role, int priority)
{
    public DimensionPoint Point { get; } = point;
    public string Role { get; } = role;
    public int Priority { get; } = priority;
}

/// <summary>
/// One side's line of points seen along the chain: which coordinate runs along it, which way is
/// outward, and how proposed points are settled into one point per coordinate.
/// </summary>
internal class ChainAxis(bool alongX, int outer, IReadOnlyList<DimensionPoint> line, double sameCoordinate)
{
    public bool AlongX { get; } = alongX;
    public int Outer { get; } = outer;
    public IReadOnlyList<DimensionPoint> Line { get; } = line;
    public double Along(DimensionPoint point) => AlongX ? point.X : point.Y;
    public double Across(DimensionPoint point) => AlongX ? point.Y : point.X;

    public IEnumerable<int> Parts(DimensionPoint point) => point.Parents
        .Where(parent => parent.ModelId.HasValue).Select(parent => parent.ModelId!.Value).Distinct();

    /// <summary>The point farthest to the outside; a real part edge beats a bare group extent; then id.</summary>
    public DimensionPoint Best(IEnumerable<DimensionPoint> points) => points
        .OrderByDescending(point => Outer * Across(point))
        .ThenByDescending(point => point.Parents.Any(parent => parent.ModelId.HasValue))
        .ThenBy(point => point.Id, StringComparer.Ordinal).First();

    /// <summary>
    /// The witness line starts at the point farthest toward the dimension line on the same
    /// coordinate, whichever part it belongs to, so it does not run along a part outline.
    /// </summary>
    public DimensionPoint Outermost(DimensionPoint point) => Line
        .Where(candidate => Math.Abs(Along(candidate) - Along(point)) <= sameCoordinate)
        .OrderByDescending(candidate => Outer * Across(candidate))
        .ThenBy(candidate => candidate.Id == point.Id ? 0 : 1)
        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault() ?? point;

    /// <summary>
    /// One point per coordinate, in the order along the chain: the pick farthest toward the
    /// dimension line wins, then the strongest role. All roles of the coordinate are kept.
    /// </summary>
    public List<(DimensionPoint Point, List<string> Roles)> Settle(IEnumerable<ChainPick> picks)
    {
        var chosen = new List<(DimensionPoint Point, List<string> Roles)>();
        foreach (var group in Cluster(picks))
        {
            var winner = group.OrderByDescending(item => Outer * Across(item.Point))
                .ThenByDescending(item => item.Priority).ThenBy(item => item.Point.Id, StringComparer.Ordinal).First();
            chosen.Add((Outermost(winner.Point), group.Select(item => item.Role).Distinct().ToList()));
        }
        return chosen;
    }

    private IEnumerable<List<ChainPick>> Cluster(IEnumerable<ChainPick> picks)
    {
        var current = new List<ChainPick>();
        foreach (var pick in picks.OrderBy(item => Along(item.Point)))
        {
            if (current.Count > 0 && Along(pick.Point) - Along(current[current.Count - 1].Point) > sameCoordinate)
            {
                yield return current;
                current = new List<ChainPick>();
            }
            current.Add(pick);
        }
        if (current.Count > 0) yield return current;
    }
}
