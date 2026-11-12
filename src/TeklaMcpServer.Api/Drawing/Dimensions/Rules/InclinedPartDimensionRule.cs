using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Locates a straight part that stands at an angle to the view axes by a chain along the part
/// itself: the part's two ends and the main-part points that reach past them. A projection of
/// such a part onto a view axis gives lengths that cannot be measured on the part.
/// </summary>
internal sealed class InclinedPartDimensionRule(InclinedPartDimensionSettings settings) : IDimensionRule
{
    public const string Kind = "inclined";

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var input = context.Require<InclinedPartDimensionInput>();
        if (!input.PartContours.TryGetValue(input.MainPartId, out var main) || main.Count < 3)
            return new DimensionRuleEvaluation([]);
        var results = input.PartContours.Where(part => part.Key != input.MainPartId).OrderBy(part => part.Key)
            .Select(part => Locate(part.Key, part.Value, input.MainPartId, main))
            .Where(result => result != null).Select(result => result!).ToArray();
        return new DimensionRuleEvaluation(results);
    }

    private DimensionRuleResult? Locate(int partId, IReadOnlyList<(double X, double Y)> contour,
        int mainId, IReadOnlyList<(double X, double Y)> main)
    {
        if (contour.Count < 3 || LongEdge(contour) is not { } along) return null;
        var tilt = Math.Atan2(Math.Min(Math.Abs(along.X), Math.Abs(along.Y)),
            Math.Max(Math.Abs(along.X), Math.Abs(along.Y))) * 180 / Math.PI;
        if (tilt < settings.MinimumTiltDegrees || !IsStraight(contour, along)) return null;

        // Across points away from the main part: that is the side the dimension line goes to.
        var across = (X: -along.Y, Y: along.X);
        var toPart = (X: contour.Average(p => p.X) - main.Average(p => p.X),
            Y: contour.Average(p => p.Y) - main.Average(p => p.Y));
        if (toPart.X * across.X + toPart.Y * across.Y < 0) across = (-across.X, -across.Y);
        double Along((double X, double Y) p) => p.X * along.X + p.Y * along.Y;
        double Across((double X, double Y) p) => p.X * across.X + p.Y * across.Y;

        var low = contour.Min(Along);
        var high = contour.Max(Along);
        var bandMin = contour.Min(Across) - settings.ContactBandToleranceViewUnits;
        var bandMax = contour.Max(Across) + settings.ContactBandToleranceViewUnits;
        // Main-part points standing against the part, within its thickness.
        var against = main.Where(p => Across(p) >= bandMin && Across(p) <= bandMax).ToArray();
        if (against.Length == 0) return null;

        (double X, double Y) End(double coordinate) => contour
            .Where(p => Math.Abs(Along(p) - coordinate) <= settings.SameCoordinateTolerance)
            .OrderByDescending(Across).First();
        var points = new List<((double X, double Y) Point, int Owner)> { (End(low), partId), (End(high), partId) };
        var beyond = against.Where(p => Along(p) < low - settings.MinimumSegmentViewUnits).OrderBy(Along).Take(1)
            .Concat(against.Where(p => Along(p) > high + settings.MinimumSegmentViewUnits)
                .OrderByDescending(Along).Take(1)).ToArray();
        // Where the main part does not reach past the part, it is located by its own limits inside it.
        var inside = beyond.Length > 0
            ? Array.Empty<(double X, double Y)>()
            : new[] { against.OrderBy(Along).First(), against.OrderBy(Along).Last() }
                .Where(p => Along(p) - low >= settings.MinimumSegmentViewUnits
                    && high - Along(p) >= settings.MinimumSegmentViewUnits).Distinct().ToArray();
        points.AddRange(beyond.Concat(inside).Select(p => (p, mainId)));

        var ordered = points.OrderBy(p => Along(p.Point)).ToArray();
        var rulePoints = ordered.Select((p, index) => new DimensionRulePoint(
            "i" + partId.ToString(CultureInfo.InvariantCulture) + "-" + index.ToString(CultureInfo.InvariantCulture),
            p.Point.X, p.Point.Y, [new DimensionPointSource("part", p.Owner, "inclined-contour")])).ToArray();
        var segments = ordered.Zip(ordered.Skip(1), (a, b) => Math.Round(Along(b.Point) - Along(a.Point), 3));
        var evidence = new Dictionary<string, object> {
            ["partId"] = partId,
            ["tiltDegrees"] = Math.Round(tilt, 3),
            // From the lowest chain point across the part to the highest: the line clears all of them.
            ["acrossSpan"] = ordered.Max(p => Across(p.Point)) - ordered.Min(p => Across(p.Point))
        };
        return new DimensionRuleResult(new DimensionDirection(along.X, along.Y),
            new OutsideOutlineDimensionPlacement(new DimensionDirection(across.X, across.Y), 1),
            Kind, rulePoints, segments: segments, evidence: evidence);
    }

    // The direction of the longest edge, pointing right (or up when vertical).
    private (double X, double Y)? LongEdge(IReadOnlyList<(double X, double Y)> contour)
    {
        var best = (X: 0d, Y: 0d, Length: 0d);
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (length > best.Length) best = ((b.X - a.X) / length, (b.Y - a.Y) / length, length);
        }
        if (best.Length <= settings.SameCoordinateTolerance) return null;
        return best.X < 0 || (Math.Abs(best.X) < 1e-12 && best.Y < 0) ? (-best.X, -best.Y) : (best.X, best.Y);
    }

    // Every edge worth a segment runs along the part or square to it: a straight bar or plate.
    private bool IsStraight(IReadOnlyList<(double X, double Y)> contour, (double X, double Y) along)
    {
        var limit = Math.Sin(settings.MinimumTiltDegrees * Math.PI / 180);
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (length < settings.MinimumSegmentViewUnits) continue;
            var cos = Math.Abs(((b.X - a.X) * along.X + (b.Y - a.Y) * along.Y) / length);
            var sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
            if (sin > limit && cos > limit) return false;
        }
        return true;
    }
}
