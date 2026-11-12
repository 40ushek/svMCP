using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class LocalPartDimensionSettings
{
    /// <summary>A part edge this close to a main-part level, in view units, stands on that face.</summary>
    public double AbuttingGapViewUnits { get; }

    /// <summary>Shortest segment worth a chain, in view units.</summary>
    public double MinimumSegmentViewUnits { get; }

    /// <summary>
    /// The same chain next to a neighbouring part is not repeated when the two parts are closer
    /// than this along the main part, in view units.
    /// </summary>
    public double RepeatGapViewUnits { get; }

    /// <summary>Coordinate comparison tolerance in view units.</summary>
    public double SameCoordinateTolerance { get; }

    public LocalPartDimensionSettings(double abuttingGapViewUnits = 2.0, double minimumSegmentViewUnits = 3.0,
        double repeatGapViewUnits = 300.0, double sameCoordinateTolerance = 0.5)
    {
        foreach (var value in new[] { abuttingGapViewUnits, minimumSegmentViewUnits, repeatGapViewUnits, sameCoordinateTolerance })
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(abuttingGapViewUnits), "Settings must be finite and non-negative.");
        AbuttingGapViewUnits = abuttingGapViewUnits;
        MinimumSegmentViewUnits = minimumSegmentViewUnits;
        RepeatGapViewUnits = repeatGapViewUnits;
        SameCoordinateTolerance = sameCoordinateTolerance;
    }
}

/// <summary>
/// What the rule needs of a view: the main part's box and the levels of its faces, and the box
/// of every straight secondary part. Parts with tilted edges are not given: a box around them
/// spans empty space.
/// </summary>
internal sealed class LocalPartDimensionInput
{
    public (double MinX, double MaxX, double MinY, double MaxY) Main { get; }
    public IReadOnlyList<double> MainLevelsX { get; }
    public IReadOnlyList<double> MainLevelsY { get; }
    public IReadOnlyDictionary<int, (double MinX, double MaxX, double MinY, double MaxY)> Parts { get; }

    public LocalPartDimensionInput((double MinX, double MaxX, double MinY, double MaxY) main,
        IReadOnlyList<double> mainLevelsX, IReadOnlyList<double> mainLevelsY,
        IReadOnlyDictionary<int, (double MinX, double MaxX, double MinY, double MaxY)> parts)
    {
        Main = main;
        MainLevelsX = mainLevelsX ?? throw new ArgumentNullException(nameof(mainLevelsX));
        MainLevelsY = mainLevelsY ?? throw new ArgumentNullException(nameof(mainLevelsY));
        Parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }
}

/// <summary>
/// Locates a secondary part across the main part by a short chain placed next to the part, as a
/// draughtsman does on a long member: a part standing on a face of the main part is measured
/// from that face (a rib from the flange, a cover plate from the edge it lies on); a part that
/// touches no face across the chain (a gusset on the web) is closed on both outer edges of the
/// main part. Parts at one place share a chain; the same chain is not repeated next door.
/// End parts and tilted parts belong to other rules.
/// </summary>
internal sealed class LocalPartDimensionRule(LocalPartDimensionSettings settings) : IDimensionRule
{
    public const string Kind = "local";

    // Across holds each coordinate with the place along the main part where its own part ends;
    // NaN for a main-part face, which is taken where the chain stands.
    private sealed class Chain(bool floating, double alongFrom, double alongTo,
        List<(double Value, double Along)> across, List<int> partIds)
    {
        public bool Floating { get; } = floating;
        public double AlongFrom { get; set; } = alongFrom;
        public double AlongTo { get; set; } = alongTo;
        public List<(double Value, double Along)> Across { get; } = across;
        public List<int> PartIds { get; } = partIds;
    }

    public DimensionRuleEvaluation Calculate(DimensionRuleContext context)
    {
        var input = context.Require<LocalPartDimensionInput>();
        var main = input.Main;
        var alongX = main.MaxX - main.MinX >= main.MaxY - main.MinY;
        var (mainAlongMin, mainAlongMax) = alongX ? (main.MinX, main.MaxX) : (main.MinY, main.MaxY);
        var (mainAcrossMin, mainAcrossMax) = alongX ? (main.MinY, main.MaxY) : (main.MinX, main.MaxX);
        var levels = (alongX ? input.MainLevelsY : input.MainLevelsX).OrderBy(level => level).ToArray();
        double? FaceAt(double value) => levels.Where(level => Math.Abs(level - value) <= settings.AbuttingGapViewUnits)
            .OrderBy(level => Math.Abs(level - value)).Select(level => (double?)level).FirstOrDefault();

        var chains = new List<Chain>();
        foreach (var part in input.Parts.OrderBy(part => part.Key))
        {
            var box = part.Value;
            var (alongFrom, alongTo) = alongX ? (box.MinX, box.MaxX) : (box.MinY, box.MaxY);
            var (low, high) = alongX ? (box.MinY, box.MaxY) : (box.MinX, box.MaxX);
            // A part reaching past the main part's end is an end part: another rule locates it.
            if (alongFrom < mainAlongMin - settings.SameCoordinateTolerance
                || alongTo > mainAlongMax + settings.SameCoordinateTolerance) continue;
            var lowFace = FaceAt(low);
            var highFace = FaceAt(high);
            if (lowFace.HasValue && highFace.HasValue) continue; // fitted between two faces: nothing to say
            var face = double.NaN;
            var across = lowFace.HasValue ? new List<(double, double)> { (lowFace.Value, face), (high, alongTo) }
                : highFace.HasValue ? new List<(double, double)> { (low, alongTo), (highFace.Value, face) }
                : new List<(double, double)> { (mainAcrossMin, face), (low, alongTo), (high, alongTo), (mainAcrossMax, face) };
            chains.Add(new Chain(!lowFace.HasValue && !highFace.HasValue, alongFrom, alongTo, across, [part.Key]));
        }

        var merged = new List<Chain>();
        foreach (var floating in new[] { true, false })
            foreach (var chain in chains.Where(chain => chain.Floating == floating).OrderBy(chain => chain.AlongFrom))
            {
                // Parts at one place along the main part are read together, on one line.
                var last = merged.LastOrDefault(other => other.Floating == floating);
                if (last != null && chain.AlongFrom <= last.AlongTo + settings.SameCoordinateTolerance)
                {
                    last.AlongTo = Math.Max(last.AlongTo, chain.AlongTo);
                    last.Across.AddRange(chain.Across);
                    last.PartIds.AddRange(chain.PartIds);
                }
                else merged.Add(chain);
            }

        var results = new List<DimensionRuleResult>();
        var placed = new List<(double Along, double[] Across)>();
        foreach (var chain in merged.OrderBy(chain => chain.AlongTo))
        {
            var own = Distinct(chain.Across);
            var across = own.Select(item => item.Value).ToArray();
            if (across.Length < 2 || across.Zip(across.Skip(1), (a, b) => b - a)
                    .Any(segment => segment < settings.MinimumSegmentViewUnits)) continue;
            // The same reading next to a neighbour (a pair of ribs) is given once.
            if (placed.Any(other => chain.AlongTo - other.Along <= settings.RepeatGapViewUnits
                    && other.Across.Length == across.Length && other.Across.Zip(across, (a, b) => Math.Abs(a - b))
                        .All(difference => difference <= settings.SameCoordinateTolerance))) continue;
            placed.Add((chain.AlongTo, across));

            var id = chain.PartIds.Min();
            var owners = chain.PartIds.Distinct().OrderBy(partId => partId).ToArray();
            // Each witness line starts at its own part: a rib standing on a gusset is taken at the
            // rib, not at the gusset's end.
            double At(double along) => double.IsNaN(along) ? chain.AlongTo : along;
            var points = own.Select((item, index) => new DimensionRulePoint(
                "l" + id.ToString(CultureInfo.InvariantCulture) + "-" + index.ToString(CultureInfo.InvariantCulture),
                alongX ? At(item.Along) : item.Value, alongX ? item.Value : At(item.Along),
                owners.Select(owner => new DimensionPointSource("part", owner, "local-box")))).ToArray();
            var evidence = new Dictionary<string, object> {
                ["partId"] = id, ["partIds"] = owners,
                // Tekla measures the offset from the lowest point of the chain; the line must still
                // clear the farthest part.
                ["baseOffset"] = chain.AlongTo - At(own[0].Along)
            };
            results.Add(new DimensionRuleResult(new DimensionDirection(alongX ? 0 : 1, alongX ? 1 : 0),
                new OutsideOutlineDimensionPlacement(new DimensionDirection(alongX ? 1 : 0, alongX ? 0 : 1), 1),
                Kind, points, segments: across.Zip(across.Skip(1), (a, b) => Math.Round(b - a, 3)), evidence: evidence));
        }
        return new DimensionRuleEvaluation(results);
    }

    private (double Value, double Along)[] Distinct(IEnumerable<(double Value, double Along)> values)
    {
        var result = new List<(double Value, double Along)>();
        // At one coordinate a part's own point is preferred to a bare main-part face.
        foreach (var item in values.OrderBy(item => item.Value).ThenBy(item => double.IsNaN(item.Along) ? 1 : 0))
            if (result.Count == 0 || item.Value - result[result.Count - 1].Value > settings.SameCoordinateTolerance)
                result.Add(item);
        return result.ToArray();
    }
}
