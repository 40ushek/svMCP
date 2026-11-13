using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Proposes the standard steel part-location chain for one side.</summary>
internal sealed class SteelPartLocationRule(SteelPartLocationSettings settings) : IDimensionRule
{
    private sealed class CalculationContext(bool alongX, int outer, IReadOnlyCollection<int> mainIds,
        IReadOnlyList<DimensionPoint> line, IReadOnlyList<DimensionPoint> all,
        IReadOnlyList<DimensionPoint> main, IReadOnlyList<DimensionPoint> otherLine, double sameCoordinate)
        : ChainAxis(alongX, outer, line, sameCoordinate)
    {
        public IReadOnlyCollection<int> MainIds { get; } = mainIds;

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
    }

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var input = context.Require<SteelPartLocationInput>();
        var catalog = input.Catalog;
        var all = catalog.AllPoints.ToArray();
        IReadOnlyList<DimensionPoint> line = catalog.LinePoints(settings.Side);
        var main = all.Where(point => point.Parents.Any(parent =>
            parent.ModelId is { } id && input.MainPartIds.Contains(id))).ToArray();
        if (main.Length == 0)
            return new DimensionRuleEvaluation([Empty("main part has no dimension points")]);

        var alongX = settings.Side.RunsAlongX();
        var mainAlongX = main.Max(point => point.X) - main.Min(point => point.X)
            >= main.Max(point => point.Y) - main.Min(point => point.Y);
        // Along the main part, a point that belongs only to parts carried by an inclined chain is
        // not offered at all: its place along the axis is a projection, not a measurable length.
        if (alongX == mainAlongX && input.CarriedPartIds.Count > 0)
            line = line.Where(point => {
                var owners = point.Parents.Where(parent => parent.ModelId.HasValue)
                    .Select(parent => parent.ModelId!.Value).Distinct().ToArray();
                return owners.Length == 0 || owners.Any(id => !input.CarriedPartIds.Contains(id));
            }).ToArray();
        var calculation = new CalculationContext(alongX,
            settings.Side.OutwardSign(),
            input.MainPartIds, line, all, main, catalog.LinePoints(settings.Side.Opposite()),
            settings.SameCoordinateTolerance);
        var result = alongX == mainAlongX
            ? AlongMain(context, calculation, input.FaceOnPartIds, input.PartThickness,
                input.InclinedPartIds.Concat(input.CarriedPartIds).ToArray())
            : AcrossMain(context, all, main, mainAlongX, calculation, input.InclinedPartIds);
        return new DimensionRuleEvaluation([result]);
    }

    private DimensionRuleResult AlongMain(DimensionRuleContext context, CalculationContext calculation,
        IReadOnlyCollection<int> faceOnPartIds, IReadOnlyDictionary<int, double> partThickness,
        IReadOnlyCollection<int> locatedAlongThemselves)
    {
        var line = calculation.Line;
        if (line.Count == 0) return Empty("no points on this side");
        if (settings.PartAnchor == DimensionPartAnchor.Center)
            return Empty("locating parts by their center is not implemented: the center is not a captured part point");
        var picks = new List<ChainPick>();
        var outer = OuterHalf(line, calculation);
        picks.AddRange(Ends(outer, calculation, "extreme", 1));
        var main = outer.Where(calculation.IsMain).ToArray();
        // Parts standing far out (gussets) push the main part and the end parts out of the outer
        // half; the chain still closes on the ends of the whole line.
        if (main.Length == 0)
        {
            picks.AddRange(Ends(line, calculation, "extreme", 1));
            main = line.Where(calculation.IsMain).ToArray();
        }
        if (main.Length > 0) picks.AddRange(Ends(main, calculation, "main-end", 2));

        // A tilted part is located along itself; here it can only be where the chain closes.
        var partIds = line.SelectMany(calculation.Parts)
            .Where(id => !calculation.MainIds.Contains(id) && !locatedAlongThemselves.Contains(id)).Distinct().ToArray();
        var offeredOnOtherSide = partIds.Where(id =>
            !calculation.LiesOnThisSide(id, settings.SameCoordinateTolerance)).ToArray();
        foreach (var id in partIds.Where(id =>
                     calculation.LiesOnThisSide(id, settings.SameCoordinateTolerance)))
        {
            var own = line.Where(point => calculation.Parts(point).Contains(id)).ToArray();
            var low = own.Min(calculation.Along);
            var high = own.Max(calculation.Along);
            // A plate seen face-on shows both of its edges, and both are located whatever the anchor.
            // So does a straight part lying along the main part: its size along the chain is a
            // length, not its thickness, and one end alone does not place it.
            // Only a part with square-on edges: a tilted one projects longer than it is.
            var squareOn = own.SelectMany(point => point.Parents)
                .Any(parent => parent.ModelId == id && parent.PartExtentAlongChain.HasValue);
            var liesAlong = squareOn && partThickness.TryGetValue(id, out var thickness)
                && high - low > thickness * settings.LiesAlongThicknessRatio;
            var both = settings.PartAnchor == DimensionPartAnchor.BothFaces || faceOnPartIds.Contains(id) || liesAlong;
            var anchors = both && high - low > settings.SameCoordinateTolerance
                ? new[] { low, high }
                : new[] { settings.PartAnchor == DimensionPartAnchor.HighFace ? high : low };
            foreach (var anchor in anchors)
                picks.Add(new ChainPick(calculation.Best(own.Where(point =>
                    Math.Abs(calculation.Along(point) - anchor) <= settings.SameCoordinateTolerance)),
                    "part-edge", 1));
        }
        return Result(context, picks, calculation, [], offeredOnOtherSide: offeredOnOtherSide);
    }

    private DimensionRuleResult AcrossMain(DimensionRuleContext context, IReadOnlyList<DimensionPoint> all,
        IReadOnlyList<DimensionPoint> main, bool mainAlongX, CalculationContext calculation,
        IReadOnlyCollection<int> inclinedPartIds)
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
        var sticking = partIds.Where(Sticks).ToArray();
        // A tilted end part is located along itself; its projection on this axis says nothing.
        var endParts = sticking.Where(id => !inclinedPartIds.Contains(id)).ToArray();
        if (endParts.Length == 0 && sticking.Length > 0)
            return Empty("the end part on this side is tilted; it is located by the inclined chain", partIds);
        if (settings.EndPartAnchor == DimensionPartAnchor.Center)
            return Empty("locating parts by their center is not implemented: the center is not a captured part point");
        var picks = new List<ChainPick>();
        foreach (var id in endParts)
        {
            var own = calculation.Line.Where(point => calculation.Parts(point).Contains(id)).ToArray();
            var groups = Cluster(own, calculation).ToArray();
            var chosen = settings.EndPartAnchor switch {
                DimensionPartAnchor.LowFace => groups.Take(1),
                DimensionPartAnchor.HighFace => groups.Skip(Math.Max(groups.Length - 1, 0)),
                _ => groups
            };
            foreach (var group in chosen)
                picks.Add(new ChainPick(calculation.Best(group), "end-part-outer", 1));
        }
        var mainOnLine = calculation.Line.Where(calculation.IsMain).ToArray();
        if (mainOnLine.Length > 0)
            picks.AddRange(Ends(mainOnLine, calculation, "main-edge", 2));
        var skipped = partIds.Except(endParts).ToArray();
        if (endParts.Length == 0)
            return Empty("no part sticks out past the main part's end on this side", skipped);
        return Result(context, picks, calculation, skipped, minPoints: 3,
            tooFewNote: "the end part is flush with the main part; nothing to locate",
            minimumSegment: settings.MinimumEndPartSegmentViewUnits);
    }

    private DimensionRuleResult Result(DimensionRuleContext context, List<ChainPick> picks,
        CalculationContext calculation, IReadOnlyCollection<int> skippedParts, int minPoints = 0,
        string? tooFewNote = null, IReadOnlyCollection<int>? offeredOnOtherSide = null, double? minimumSegment = null)
    {
        // Across the main part a few millimetres are the whole point (an end plate 8 short of a
        // flange), so only the model-unit floor applies there.
        var shortest = minimumSegment ?? settings.MinimumSegmentViewUnits;
        var chosen = calculation.Settle(picks);

        var dropped = new List<DimensionPoint>();
        for (var index = chosen.Count - 2; index >= 1; index--)
            if (calculation.Along(chosen[index + 1].Point) - calculation.Along(chosen[index].Point)
                < shortest)
            {
                dropped.Add(chosen[index].Point);
                chosen.RemoveAt(index);
            }
        // The first interval too: the inner point next to the first end goes, the end stays.
        while (chosen.Count > 2
               && calculation.Along(chosen[1].Point) - calculation.Along(chosen[0].Point) < shortest)
        {
            dropped.Add(chosen[1].Point);
            chosen.RemoveAt(1);
        }

        var kept = chosen.SelectMany(item => calculation.Line.Where(point =>
                Math.Abs(calculation.Along(point) - calculation.Along(item.Point)) <= settings.SameCoordinateTolerance)
            .SelectMany(calculation.Parts)).ToHashSet();
        var skipped = skippedParts.Concat(dropped.SelectMany(calculation.Parts).Where(id => !kept.Contains(id)))
            .Distinct().ToArray();
        if (chosen.Count < minPoints)
        {
            var note = dropped.Count > 0
                ? $"fewer than {minPoints} points remain after dropping segments shorter than {shortest} view units"
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
        var alongX = settings.Side.RunsAlongX();
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

    private IEnumerable<ChainPick> Ends(IEnumerable<DimensionPoint> points, CalculationContext calculation,
        string role, int priority)
    {
        var list = points.ToArray();
        var low = list.Min(calculation.Along);
        var high = list.Max(calculation.Along);
        yield return new ChainPick(calculation.Best(list.Where(point =>
            Math.Abs(calculation.Along(point) - low) <= settings.SameCoordinateTolerance)), role, priority);
        yield return new ChainPick(calculation.Best(list.Where(point =>
            Math.Abs(calculation.Along(point) - high) <= settings.SameCoordinateTolerance)), role, priority);
    }

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

    private DimensionDirection Direction(bool alongX) => new(alongX ? 1 : 0, alongX ? 0 : 1);

    private OutsideOutlineDimensionPlacement Placement(bool alongX)
    {
        var outer = settings.Side.OutwardSign();
        return new OutsideOutlineDimensionPlacement(
            new DimensionDirection(alongX ? 0 : outer, alongX ? outer : 0), 1);
    }
}
