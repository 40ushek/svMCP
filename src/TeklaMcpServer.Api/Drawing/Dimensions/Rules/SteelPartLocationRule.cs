using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Proposes the standard steel part-location chain for one side.</summary>
internal sealed class SteelPartLocationRule(SteelPartLocationSettings settings) : IDimensionRule
{
    private sealed class Pick(DimensionPoint point, string role, int priority)
    {
        public DimensionPoint Point { get; } = point;
        public string Role { get; } = role;
        public int Priority { get; } = priority;
    }

    private sealed class CalculationContext(bool alongX, int outer, IReadOnlyCollection<int> mainIds,
        IReadOnlyList<DimensionPoint> line, IReadOnlyList<DimensionPoint> all,
        IReadOnlyList<DimensionPoint> main, IReadOnlyList<DimensionPoint> otherLine)
    {
        public bool AlongX { get; } = alongX;
        public int Outer { get; } = outer;
        public IReadOnlyCollection<int> MainIds { get; } = mainIds;
        public IReadOnlyList<DimensionPoint> Line { get; } = line;
        public double Along(DimensionPoint point) => AlongX ? point.X : point.Y;
        public double Across(DimensionPoint point) => AlongX ? point.Y : point.X;

        public bool LiesOnThisSide(int partId, double tolerance)
        {
            var own = all.Where(point => Parts(point).Contains(partId)).Select(Across).ToArray();
            if (own.Length == 0 || main.Count == 0) return true;
            var mainMiddle = (main.Max(Across) + main.Min(Across)) / 2;
            var reachesHigh = own.Max() > mainMiddle + tolerance;
            var reachesLow = own.Min() < mainMiddle - tolerance;
            if (!reachesHigh && !reachesLow) return true;
            if (Outer > 0 ? reachesHigh : reachesLow) return true;
            return !otherLine.Any(point => Parts(point).Contains(partId));
        }

        public bool IsMain(DimensionPoint point) => point.Parents.Any(parent =>
            parent.ModelId is { } id && MainIds.Contains(id));
        public IEnumerable<int> Parts(DimensionPoint point) => point.Parents
            .Where(parent => parent.ModelId.HasValue).Select(parent => parent.ModelId!.Value).Distinct();
    }

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var input = context.Require<SteelPartLocationInput>();
        var catalog = input.Catalog;
        var all = catalog.AllPoints.ToArray();
        var line = catalog.LinePoints(settings.Side);
        var main = all.Where(point => point.Parents.Any(parent =>
            parent.ModelId is { } id && input.MainPartIds.Contains(id))).ToArray();
        if (main.Length == 0)
            return new DimensionRuleEvaluation([Empty("main part has no dimension points")]);

        var alongX = settings.Side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var mainAlongX = main.Max(point => point.X) - main.Min(point => point.X)
            >= main.Max(point => point.Y) - main.Min(point => point.Y);
        var calculation = new CalculationContext(alongX,
            settings.Side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1,
            input.MainPartIds, line, all, main, catalog.LinePoints(Opposite(settings.Side)));
        var result = alongX == mainAlongX
            ? AlongMain(context, calculation)
            : AcrossMain(context, all, main, mainAlongX, calculation);
        return new DimensionRuleEvaluation([result]);
    }

    private DimensionRuleResult AlongMain(DimensionRuleContext context, CalculationContext calculation)
    {
        var line = calculation.Line;
        if (line.Count == 0) return Empty("no points on this side");
        var picks = new List<Pick>();
        var outer = OuterHalf(line, calculation);
        picks.AddRange(Ends(outer, calculation, "extreme", 1));
        var main = outer.Where(calculation.IsMain).ToArray();
        if (main.Length > 0) picks.AddRange(Ends(main, calculation, "main-end", 2));

        var partIds = line.SelectMany(calculation.Parts)
            .Where(id => !calculation.MainIds.Contains(id)).Distinct().ToArray();
        var offeredOnOtherSide = partIds.Where(id =>
            !calculation.LiesOnThisSide(id, settings.SameCoordinateTolerance)).ToArray();
        foreach (var id in partIds.Where(id =>
                     calculation.LiesOnThisSide(id, settings.SameCoordinateTolerance)))
        {
            var own = line.Where(point => calculation.Parts(point).Contains(id)).ToArray();
            var first = own.Min(calculation.Along);
            picks.Add(new Pick(Best(own.Where(point =>
                Math.Abs(calculation.Along(point) - first) <= settings.SameCoordinateTolerance), calculation),
                "part-edge", 1));
        }
        return Result(context, picks, calculation, [], offeredOnOtherSide: offeredOnOtherSide);
    }

    private DimensionRuleResult AcrossMain(DimensionRuleContext context, IReadOnlyList<DimensionPoint> all,
        IReadOnlyList<DimensionPoint> main, bool mainAlongX, CalculationContext calculation)
    {
        double MainAxis(DimensionPoint point) => mainAlongX ? point.X : point.Y;
        var mainLow = main.Min(MainAxis);
        var mainHigh = main.Max(MainAxis);
        var lowEnd = settings.Side is DimensionChainSide.Left or DimensionChainSide.Bottom;
        bool Sticks(int id)
        {
            var own = all.Where(point => calculation.Parts(point).Contains(id)).ToArray();
            return own.Length > 0 && (lowEnd
                ? own.Min(MainAxis) < mainLow - settings.SameCoordinateTolerance
                : own.Max(MainAxis) > mainHigh + settings.SameCoordinateTolerance);
        }

        var partIds = calculation.Line.SelectMany(calculation.Parts)
            .Where(id => !calculation.MainIds.Contains(id)).Distinct().ToArray();
        var endParts = partIds.Where(Sticks).ToArray();
        var picks = new List<Pick>();
        foreach (var id in endParts)
        {
            var own = calculation.Line.Where(point => calculation.Parts(point).Contains(id)).ToArray();
            foreach (var group in Cluster(own, calculation))
                picks.Add(new Pick(Best(group, calculation), "end-part-outer", 1));
        }
        var mainOnLine = calculation.Line.Where(calculation.IsMain).ToArray();
        if (mainOnLine.Length > 0)
            picks.AddRange(Ends(mainOnLine, calculation, "main-edge", 2));
        var skipped = partIds.Except(endParts).ToArray();
        if (endParts.Length == 0)
            return Empty("no part sticks out past the main part's end on this side", skipped);
        return Result(context, picks, calculation, skipped, minPoints: 3,
            tooFewNote: "the end part is flush with the main part; nothing to locate");
    }

    private DimensionRuleResult Result(DimensionRuleContext context, List<Pick> picks,
        CalculationContext calculation, IReadOnlyCollection<int> skippedParts, int minPoints = 0,
        string? tooFewNote = null, IReadOnlyCollection<int>? offeredOnOtherSide = null)
    {
        var chosen = new List<(DimensionPoint Point, List<string> Roles)>();
        foreach (var group in ClusterPicks(picks, calculation))
        {
            var winner = group.OrderByDescending(item => calculation.Outer * calculation.Across(item.Point))
                .ThenByDescending(item => item.Priority).ThenBy(item => item.Point.Id, StringComparer.Ordinal).First();
            chosen.Add((Outermost(winner.Point, calculation), group.Select(item => item.Role).Distinct().ToList()));
        }

        var dropped = new List<DimensionPoint>();
        for (var index = chosen.Count - 2; index >= 1; index--)
            if (calculation.Along(chosen[index + 1].Point) - calculation.Along(chosen[index].Point)
                < settings.MinimumSegmentViewUnits)
            {
                dropped.Add(chosen[index].Point);
                chosen.RemoveAt(index);
            }

        var kept = chosen.SelectMany(item => calculation.Line.Where(point =>
                Math.Abs(calculation.Along(point) - calculation.Along(item.Point)) <= settings.SameCoordinateTolerance)
            .SelectMany(calculation.Parts)).ToHashSet();
        var skipped = skippedParts.Concat(dropped.SelectMany(calculation.Parts).Where(id => !kept.Contains(id)))
            .Distinct().ToArray();
        if (chosen.Count < minPoints)
        {
            var note = dropped.Count > 0
                ? $"fewer than {minPoints} points remain after dropping segments shorter than {settings.MinimumSegmentViewUnits} view units"
                : tooFewNote ?? "too few points";
            return Empty(note, skipped, dropped.Select(point => point.Id).ToArray());
        }

        var ordered = chosen.OrderBy(item => calculation.Along(item.Point)).ToArray();
        var points = ordered.Select(item => context.GetPoint(item.Point.Id)).ToArray();
        var segments = points.Zip(points.Skip(1), (first, second) => Math.Round(
            calculation.AlongX ? second.X - first.X : second.Y - first.Y, 3));
        var evidence = new Dictionary<string, object> {
            ["roles"] = ordered.Select(item => item.Roles.ToArray()).ToArray(),
            ["partIds"] = ordered.Select(item => calculation.Parts(item.Point).ToArray()).ToArray(),
            ["skippedPartIds"] = skipped,
            ["offeredOnOtherSidePartIds"] = (offeredOnOtherSide ?? Array.Empty<int>()).ToArray(),
            ["droppedShortPointIds"] = dropped.Select(point => point.Id).ToArray()
        };
        return new DimensionRuleResult(Direction(calculation.AlongX), Placement(calculation.AlongX),
            "location", points, segments: segments, evidence: evidence);
    }

    private DimensionRuleResult Empty(string note, IReadOnlyCollection<int>? skipped = null,
        IReadOnlyCollection<string>? dropped = null)
    {
        var alongX = settings.Side is DimensionChainSide.Top or DimensionChainSide.Bottom;
        var evidence = new Dictionary<string, object> {
            ["roles"] = Array.Empty<string[]>(), ["partIds"] = Array.Empty<int[]>(),
            ["skippedPartIds"] = (skipped ?? Array.Empty<int>()).ToArray(),
            ["offeredOnOtherSidePartIds"] = Array.Empty<int>(),
            ["droppedShortPointIds"] = (dropped ?? Array.Empty<string>()).ToArray()
        };
        return new DimensionRuleResult(Direction(alongX), Placement(alongX), "location", [], note, evidence: evidence);
    }

    private IReadOnlyList<DimensionPoint> OuterHalf(IReadOnlyList<DimensionPoint> line,
        CalculationContext calculation)
    {
        var middle = (line.Max(calculation.Across) + line.Min(calculation.Across)) / 2;
        var outer = line.Where(point => calculation.Outer * (calculation.Across(point) - middle)
            >= -settings.SameCoordinateTolerance).ToArray();
        return outer.Length > 0 ? outer : line;
    }

    private IEnumerable<Pick> Ends(IEnumerable<DimensionPoint> points, CalculationContext calculation,
        string role, int priority)
    {
        var list = points.ToArray();
        var low = list.Min(calculation.Along);
        var high = list.Max(calculation.Along);
        yield return new Pick(Best(list.Where(point =>
            Math.Abs(calculation.Along(point) - low) <= settings.SameCoordinateTolerance), calculation), role, priority);
        yield return new Pick(Best(list.Where(point =>
            Math.Abs(calculation.Along(point) - high) <= settings.SameCoordinateTolerance), calculation), role, priority);
    }

    private static DimensionPoint Best(IEnumerable<DimensionPoint> points, CalculationContext calculation) => points
        .OrderByDescending(point => calculation.Outer * calculation.Across(point))
        .ThenByDescending(point => point.Parents.Any(parent => parent.ModelId.HasValue))
        .ThenBy(point => point.Id, StringComparer.Ordinal).First();

    private IEnumerable<DimensionPoint[]> Cluster(IEnumerable<DimensionPoint> points, CalculationContext calculation)
    {
        var current = new List<DimensionPoint>();
        foreach (var point in points.OrderBy(calculation.Along))
        {
            if (current.Count > 0 && calculation.Along(point) - calculation.Along(current[current.Count - 1])
                > settings.SameCoordinateTolerance)
            {
                yield return current.ToArray();
                current = new List<DimensionPoint>();
            }
            current.Add(point);
        }
        if (current.Count > 0) yield return current.ToArray();
    }

    private DimensionPoint Outermost(DimensionPoint point, CalculationContext calculation) => calculation.Line
        .Where(candidate => Math.Abs(calculation.Along(candidate) - calculation.Along(point))
            <= settings.SameCoordinateTolerance)
        .OrderByDescending(candidate => calculation.Outer * calculation.Across(candidate))
        .ThenBy(candidate => candidate.Id == point.Id ? 0 : 1)
        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault() ?? point;

    private IEnumerable<List<Pick>> ClusterPicks(List<Pick> picks, CalculationContext calculation)
    {
        var current = new List<Pick>();
        foreach (var pick in picks.OrderBy(item => calculation.Along(item.Point)))
        {
            if (current.Count > 0 && calculation.Along(pick.Point)
                - calculation.Along(current[current.Count - 1].Point) > settings.SameCoordinateTolerance)
            {
                yield return current;
                current = new List<Pick>();
            }
            current.Add(pick);
        }
        if (current.Count > 0) yield return current;
    }

    private DimensionDirection Direction(bool alongX) => new(alongX ? 1 : 0, alongX ? 0 : 1);

    private OutsideOutlineDimensionPlacement Placement(bool alongX)
    {
        var outer = settings.Side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;
        return new OutsideOutlineDimensionPlacement(
            new DimensionDirection(alongX ? 0 : outer, alongX ? outer : 0), "first");
    }

    private static DimensionChainSide Opposite(DimensionChainSide side) => side switch {
        DimensionChainSide.Top => DimensionChainSide.Bottom,
        DimensionChainSide.Bottom => DimensionChainSide.Top,
        DimensionChainSide.Left => DimensionChainSide.Right,
        _ => DimensionChainSide.Left
    };
}
