using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Locates secondary section parts relative to a prior main-profile proposal.</summary>
internal sealed class SectionPartLocationRule(SectionPartLocationSettings settings) : IDimensionRule
{
    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var geometry = context.AxisAlignedGeometry;
        if (geometry == null)
            return new DimensionRuleEvaluation([Refused("axis-aligned snapshot geometry is unavailable")]);
        var profile = context.PriorResults.SingleOrDefault(result => result.Kind == "profile"
            && AxisAlignedDimensionRulePreviewAdapter.GetSide(result) == settings.Side);
        if (profile == null)
            return new DimensionRuleEvaluation([Refused("section location requires a prior profile proposal")]);
        if (profile.Note != null)
            return new DimensionRuleEvaluation([Refused(profile.Note)]);

        var profileInput = context.Require<SectionProfileDimensionInput>();
        var locationInput = context.Require<SectionPartLocationInput>();
        var catalog = geometry.Points;
        var side = settings.Side;
        var alongX = side.RunsAlongX();
        var levels = alongX ? profileInput.XLevels : profileInput.YLevels;
        var line = catalog.LinePoints(side);
        var originalById = catalog.AllPoints.ToDictionary(point => point.Id, StringComparer.Ordinal);
        var profilePoints = profile.Points.Select(point => originalById[point.Id]).ToArray();
        double Along(DimensionPoint point) => alongX ? point.X : point.Y;
        double Across(DimensionPoint point) => alongX ? point.Y : point.X;
        double Outside(DimensionPoint point)
        {
            var outer = side.OutwardSign();
            return outer * Across(point);
        }
        bool Owns(DimensionPoint point, int modelId) => point.Parents.Any(parent => parent.ModelId == modelId);

        // A part is handed to the opposite side only when that side is built.
        var opposite = catalog.LinePoints(side.Opposite());
        var oppositeBuilt = SectionProfileDimensionRule.Offered(opposite, profileInput.MainPartId, levels, alongX,
            settings.SameCoordinateTolerance);
        var location = new List<DimensionPoint> { profilePoints[0], profilePoints[profilePoints.Length - 1] };
        var locatedByProfile = new List<int>();
        var otherSide = new List<int>();
        var candidates = new List<(int Id, DimensionPoint Point)>();
        foreach (var id in locationInput.SecondaryPartIds)
        {
            var own = line.Where(point => Owns(point, id)).ToArray();
            if (own.Length == 0) continue;
            var allOwn = catalog.AllPoints.Where(point => Owns(point, id)).ToArray();
            var center = alongX
                ? (profileInput.YLevels[0] + profileInput.YLevels[profileInput.YLevels.Length - 1]) / 2
                : (profileInput.XLevels[0] + profileInput.XLevels[profileInput.XLevels.Length - 1]) / 2;
            var reachesHigh = allOwn.Any(point => Across(point) > center + settings.SameCoordinateTolerance);
            var reachesLow = allOwn.Any(point => Across(point) < center - settings.SameCoordinateTolerance);
            var thisSide = side is DimensionChainSide.Top or DimensionChainSide.Right ? reachesHigh : reachesLow;
            var belongsOpposite = !thisSide && (reachesHigh || reachesLow) && opposite.Any(point => Owns(point, id));
            if (belongsOpposite && oppositeBuilt)
            {
                otherSide.Add(id);
                continue;
            }

            // A part is located by both of its edges, inside the profile limits or past them: one
            // edge alone leaves its size along the chain to be worked out (a plate cut across shows
            // its thickness, 71 - 10 - 71), whichever side it is read from and however long it is.
            var low = own.Min(Along);
            var high = own.Max(Along);
            var wanted = high - low > settings.SameCoordinateTolerance ? new List<double> { low, high } : new List<double> { low };
            foreach (var coordinate in wanted)
            {
                var pick = own.Where(point => Math.Abs(Along(point) - coordinate) <= settings.SameCoordinateTolerance)
                    .OrderByDescending(Outside).ThenBy(point => point.Id, StringComparer.Ordinal).First();
                if (profilePoints.Any(point => Math.Abs(Along(point) - Along(pick)) <= settings.SameCoordinateTolerance))
                    locatedByProfile.Add(id);
                else
                    candidates.Add((id, pick));
            }
        }

        var mergedNearby = new List<int>();
        var kept = new HashSet<int>();
        var located = new List<int>(locatedByProfile);
        DimensionPoint? last = null;
        foreach (var candidate in candidates.OrderBy(candidate => Along(candidate.Point)).ThenBy(candidate => candidate.Id))
        {
            // A gap of exactly the readable size stays: rounding must not merge two edges of one plate.
            if (last != null && Along(candidate.Point) - Along(last) < settings.ReadableGapViewUnits - settings.SameCoordinateTolerance)
                mergedNearby.Add(candidate.Id);
            else
            {
                location.Add(candidate.Point);
                last = candidate.Point;
                kept.Add(candidate.Id);
            }
            located.Add(candidate.Id);
        }
        // A part is reported merged only when none of its own points is left on the chain.
        mergedNearby = mergedNearby.Distinct().Where(id => !kept.Contains(id)).ToList();

        var ordered = location.GroupBy(Along)
            .Select(group => group.OrderByDescending(Outside).ThenBy(point => point.Id, StringComparer.Ordinal).First())
            .OrderBy(Along).ToArray();
        var points = ordered.Select(point => context.GetPoint(point.Id)).ToArray();
        var segments = points.Zip(points.Skip(1), (first, second) =>
            Math.Round(alongX ? second.X - first.X : second.Y - first.Y, 3));
        var evidence = new Dictionary<string, object> {
            ["locatedByProfilePartIds"] = locatedByProfile.ToArray(),
            ["offeredOnOtherSidePartIds"] = otherSide.ToArray(),
            ["mergedNearbyPartIds"] = mergedNearby.ToArray(),
            ["locatedPartIds"] = located.Distinct().ToArray()
        };
        return new DimensionRuleEvaluation([new DimensionRuleResult(Direction(alongX), Placement(side, alongX),
            "location", points, segments: segments, evidence: evidence)]);
    }

    private DimensionRuleResult Refused(string reason)
    {
        var alongX = settings.Side.RunsAlongX();
        return new DimensionRuleResult(Direction(alongX), Placement(settings.Side, alongX), "location", [], reason);
    }

    private static DimensionDirection Direction(bool alongX) => new(alongX ? 1 : 0, alongX ? 0 : 1);

    private static OutsideOutlineDimensionPlacement Placement(DimensionChainSide side, bool alongX)
    {
        var outer = side.OutwardSign();
        return new OutsideOutlineDimensionPlacement(
            new DimensionDirection(alongX ? 0 : outer, alongX ? outer : 0), 1);
    }
}
