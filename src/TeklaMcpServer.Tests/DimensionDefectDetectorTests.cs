using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.Dimensions.Defects;
using Xunit;
using Xunit.Abstractions;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Grades the detector against real drawings captured under `cases/`. These are not fixtures
/// invented to make a check pass — they are states of drawings a person actually worked on, and
/// the `after-human` states say which findings were worth acting on.
/// </summary>
public sealed class DimensionDefectDetectorTests
{
    private readonly ITestOutputHelper _output;

    public DimensionDefectDetectorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CapturedStatesLoadWithContent()
    {
        // `cases/` is gitignored — a plant's real drawings, not something this repo ships. On a
        // clean clone it does not exist, and that is not a regression: pass rather than fail, or
        // CI stays red forever for a reason no code change here can fix.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var states = DimensionCaseLoader.LoadAll().ToList();
        Assert.NotEmpty(states);

        // Case-insensitive JSON binding is what makes one loader read two different serializations.
        // If that ever breaks it fails silently — every list comes back empty and every drawing
        // looks clean — so assert content rather than merely that the files parsed.
        foreach (var state in states)
        {
            // Either the state has content, or it says why it has none. Silence is the failure.
            if (state.LoadError != null)
                continue;

            Assert.True(state.Dimensions.Count > 0, $"{state}: no dimensions parsed and no capture error");
            Assert.True(state.Parts.Count > 0, $"{state}: no parts parsed and no capture error");
            Assert.All(state.Dimensions, dimension => Assert.NotEqual(0, dimension.DimensionId));
            Assert.All(state.Parts, part => Assert.Equal(3, part.BboxMin.Length));
        }

        // Two captures on disk are saved bridge failures. Pin the count: if a future capture
        // silently fails the same way, this test says so instead of the state quietly grading as
        // a drawing with no defects.
        Assert.Equal(2, states.Count(state => state.LoadError != null));
    }

    [Fact]
    public void ReportsEveryDefectFoundByHandOnTheGable()
    {
        // EW.4-6, the gable whose `before` state is the assistant's work and whose `after-human`
        // is the person's correction. Four defects were found by hand on the `before` state and
        // three of them were then removed by the person, so this is the one drawing where the
        // detector's output can be checked against a known answer.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var state = DimensionCaseLoader.LoadAll()
            .Single(candidate => candidate.Guid.StartsWith("5cf600c9", StringComparison.Ordinal) && candidate.State == "before");

        var report = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);

        Assert.True(report.CoordinateSpaceOk);

        // The batten's own 45 mm thickness, restated between the header underside and the batten top.
        Assert.Contains(report.Defects, defect =>
            defect.Kind == DimensionDefectKind.RedundantPartSizeSpan && defect.DimensionId == 1813);

        // (0, 1728.8): the panel's top-right height, taken on the left where the panel is lower.
        Assert.Contains(report.Defects, defect =>
            defect.Kind == DimensionDefectKind.PhantomAnchor && defect.DimensionId == 1765);

        // The width overall (1829) also hangs on the assembly's bounding-box corners, and by hand
        // that was written up as a defect. The person kept it. An overall reaches the extreme
        // corners of the assembly by definition, and on a raked panel the lowest and the highest
        // material are not on one part — so it must NOT be reported. This is the grading changing
        // the detector, which is what the captured states are for.
        Assert.True(report.Chains.Single(chain => chain.DimensionId == 1829).IsOverall);
        Assert.DoesNotContain(report.Defects, defect => defect.DimensionId == 1829);

        // A batten top 960 mm across from the chain drawn down the right-hand side.
        Assert.Contains(report.Defects, defect =>
            defect.Kind == DimensionDefectKind.PointFarFromChain && defect.DimensionId == 1813);
    }

    [Fact]
    public void OverallDimensionsAreNeverReportedAsContained()
    {
        // An overall repeats positions another chain already carries, by definition. Reporting it
        // would mean following the report deletes the overall, which is the one thing that never
        // happens by hand.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        foreach (var state in DimensionCaseLoader.LoadAll().Where(static state => state.IsUsable))
        {
            var report = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);
            var overalls = report.Chains.Where(static chain => chain.IsOverall).Select(static chain => chain.DimensionId).ToHashSet();

            Assert.DoesNotContain(report.Defects, defect =>
                defect.Kind == DimensionDefectKind.ContainedChain && overalls.Contains(defect.DimensionId));
        }
    }

    [Fact]
    public void ContainmentNeverReportsBothHalvesOfAPair()
    {
        // Two chains measuring the same positions each contain the other. Reporting both would
        // read as "delete either", and acting on it twice leaves the drawing without the position.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        foreach (var state in DimensionCaseLoader.LoadAll().Where(static state => state.IsUsable))
        {
            var report = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);
            var contained = report.Defects
                .Where(static defect => defect.Kind == DimensionDefectKind.ContainedChain)
                .ToList();

            foreach (var defect in contained)
                Assert.DoesNotContain(contained, other =>
                    other.DimensionId == defect.ContainedIn && other.ContainedIn == defect.DimensionId);
        }
    }

    [Fact]
    public void AngledChainsAreSkippedWithAWarningRatherThanGradedAsHorizontal()
    {
        // Tekla reports "angled" for a diagonal and for a set mixing both directions. Control
        // diagonals come back that way and are never thinned — they are checked with a tape on
        // the assembly table, so no rule here applies to them. Treating "not vertical" as
        // horizontal silently graded 39 of the 210 captured chains along an axis they do not have.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var withAngled = DimensionCaseLoader.LoadAll()
            .Where(static state => state.IsUsable)
            .Select(static state => new
            {
                state,
                Angled = state.Dimensions
                    .Where(static dimension => dimension.Orientation == "angled" && dimension.PointAssociations.Count >= 2)
                    .Select(static dimension => dimension.DimensionId)
                    .ToHashSet()
            })
            .First(entry => entry.Angled.Count > 0);

        var report = DimensionDefectDetector.Detect(
            withAngled.state.ViewId, withAngled.state.Dimensions, withAngled.state.Parts, withAngled.state.Coverage);

        Assert.DoesNotContain(report.Defects, defect => withAngled.Angled.Contains(defect.DimensionId));
        Assert.DoesNotContain(report.Chains, chain => withAngled.Angled.Contains(chain.DimensionId));

        // Skipped is not the same as clean, and the report has to say which it was.
        foreach (var id in withAngled.Angled)
            Assert.Contains(report.Warnings, warning => warning.Contains($"chain {id} is 'angled'"));
    }

    [Fact]
    public void TheCoordinateGateMeasuresEveryVisiblePartNotOnlyTheStructuralOnes()
    {
        // Insulation and fittings overhang the frame, and a dimension may legitimately reach
        // their edge. Gating on timber alone would call that a coordinate-space failure and
        // switch every other check off — the one failure mode where a wrong answer is silent,
        // because a report with no findings reads exactly like a clean drawing.
        //
        // To actually discriminate the two gates, the injected point has to land BETWEEN them:
        // past the structural-only threshold (so the old, structural-only gate would have
        // tripped) but within the visible one (so the fixed gate must not). Merely extending a
        // part far out, with no chain reaching anywhere near it, passes both gates trivially and
        // proves nothing — that was the mistake in the first version of this test.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var state = DimensionCaseLoader.LoadAll()
            .Single(candidate => candidate.Guid.StartsWith("5cf600c9", StringComparison.Ordinal) && candidate.State == "before");

        // Mirrors the detector's own structural filter (timber, or unknown-but-not-R/M).
        var structuralX = Extent(state.Parts.Where(static part =>
            part.MaterialType == 5 || (part.MaterialType < 0 && part.PartPrefix is not ("R" or "M"))));
        var oldThreshold = (structuralX * 1.05) + 1;

        var target = oldThreshold + 50;

        var chain = state.Dimensions.First(static dimension =>
            string.Equals(dimension.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase) &&
            dimension.PointAssociations.Count >= 2);
        chain.PointAssociations[0].Point.X = target;

        // Give the visible extent enough room to legitimately cover the injected point, the way
        // a real fitting overhanging the frame would.
        var overhanging = state.Parts.Single(part => part.PartPos == "R-81");
        overhanging.BboxMax[0] = target + 200;

        var report = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);

        Assert.True(report.CoordinateSpaceOk);
        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.CoordinateSpaceMismatch);
    }

    [Fact]
    public void PhantomAnchorRequiresEveryDistinctMatchedPartToHaveCompleteSolidGeometry()
    {
        // Coverage deliberately does not pick a winner among candidates: at a junction the true
        // anchor might be the second, incomplete part rather than the complete one. An `Any()`
        // check over the matches would call this a proven phantom because ONE of the two happens
        // to be complete; it has to be `All()`, or the incomplete part's missing candidates could
        // be hiding the real anchor.
        var chain = new DimensionContextInfo
        {
            DimensionId = 9001,
            Orientation = "horizontal",
            PointAssociations = new List<DimensionContextPointAssociationInfo>
            {
                new() { Point = new DrawingPointInfo { X = 0, Y = 0 } },
                new() { Point = new DrawingPointInfo { X = 100, Y = 0 } }
            }
        };

        var complete = new PartGeometryInViewResult
        {
            ModelId = 1, PartPos = "T-1",
            BboxMin = new[] { 0d, 0, 0 }, BboxMax = new[] { 10d, 10, 10 },
            SolidGeometryComplete = true
        };
        var incomplete = new PartGeometryInViewResult
        {
            ModelId = 2, PartPos = "T-2",
            BboxMin = new[] { 90d, 0, 0 }, BboxMax = new[] { 110d, 10, 10 },
            SolidGeometryComplete = false
        };

        var coverage = new DimensionChainCoverageResult
        {
            Success = true,
            DimensionId = 9001,
            Points = new List<DimensionPointCoverage>
            {
                new()
                {
                    DimensionId = 9001,
                    Point = new[] { 100d, 0d },
                    Status = DimensionCoverageStatus.Matched,
                    FallbackOnly = true,
                    Matches = new List<DimensionCoverageMatch>
                    {
                        new() { ModelObjectId = 1 },
                        new() { ModelObjectId = 2 }
                    }
                }
            }
        };

        var report = DimensionDefectDetector.Detect(1, new[] { chain }, new[] { complete, incomplete }, new[] { coverage });

        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.PhantomAnchor);
        Assert.Contains(report.Defects, defect =>
            defect.Kind == DimensionDefectKind.AnchorUnverified &&
            defect.Confidence == DimensionDefectConfidence.Provisional &&
            defect.DimensionId == 9001);
    }

    [Fact]
    public void AmbiguousStatusIsReportedEvenWhenThePointIsAlsoFallbackOnly()
    {
        // Status is checked before FallbackOnly for exactly this reason: an ambiguous point can
        // also be fallback-derived, and if FallbackOnly were checked first the ambiguity would be
        // silently swallowed into PhantomAnchor or AnchorUnverified — reporting a shape defect
        // while hiding the fact that two candidates disagree on where the point sits at all.
        var chain = new DimensionContextInfo
        {
            DimensionId = 9002,
            Orientation = "horizontal",
            PointAssociations = new List<DimensionContextPointAssociationInfo>
            {
                new() { Point = new DrawingPointInfo { X = 0, Y = 0 } },
                new() { Point = new DrawingPointInfo { X = 100, Y = 0 } }
            }
        };

        // Both candidates are bounding-box corners (FallbackOnly = true) AND sit at different
        // positions (Ambiguous) — solids read in full, so a PhantomAnchor read would otherwise be
        // available and has to lose to AmbiguousAnchor. Bounds span 0..110 so the coordinate gate
        // (widest chain span 100, against a visible extent of 110) does not itself trip.
        var first = new PartGeometryInViewResult
        {
            ModelId = 1, PartPos = "T-1",
            BboxMin = new[] { 0d, 0, 0 }, BboxMax = new[] { 10d, 10, 10 },
            SolidGeometryComplete = true
        };
        var second = new PartGeometryInViewResult
        {
            ModelId = 2, PartPos = "T-2",
            BboxMin = new[] { 90d, 0, 0 }, BboxMax = new[] { 110d, 10, 10 },
            SolidGeometryComplete = true
        };

        var coverage = new DimensionChainCoverageResult
        {
            Success = true,
            DimensionId = 9002,
            Points = new List<DimensionPointCoverage>
            {
                new()
                {
                    DimensionId = 9002,
                    Point = new[] { 100d, 0d },
                    Status = DimensionCoverageStatus.Ambiguous,
                    FallbackOnly = true,
                    Matches = new List<DimensionCoverageMatch>
                    {
                        new() { ModelObjectId = 1, Point = new[] { 100d, 0d } },
                        new() { ModelObjectId = 2, Point = new[] { 100.5, 0d } }
                    }
                }
            }
        };

        var report = DimensionDefectDetector.Detect(1, new[] { chain }, new[] { first, second }, new[] { coverage });

        Assert.Contains(report.Defects, defect =>
            defect.Kind == DimensionDefectKind.AmbiguousAnchor &&
            defect.Confidence == DimensionDefectConfidence.Provisional &&
            defect.DimensionId == 9002);
        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.PhantomAnchor);
        Assert.DoesNotContain(report.Defects, static defect => defect.Kind == DimensionDefectKind.AnchorUnverified);

        // Exactly one anchor defect for this point — Ambiguous must not ALSO fall through to a
        // second, contradictory finding.
        Assert.Single(report.Defects, defect => defect.DimensionId == 9002);
    }

    private static double Extent(IEnumerable<PartGeometryInViewResult> parts)
    {
        var list = parts.ToList();
        return list.Max(static part => part.BboxMax[0]) - list.Min(static part => part.BboxMin[0]);
    }

    [Fact]
    public void FallbackOnlyIsOnlyAPhantomWhenTheSolidWasReadInFull()
    {
        // fallbackOnly says every match came from a bounding box. On a part whose solid traversal
        // failed there were no face or vertex candidates to match against in the first place, so
        // the flag says nothing about where the point sits. Reporting that as a proven phantom
        // would invite deleting a point that is perfectly well placed.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var state = DimensionCaseLoader.LoadAll()
            .Single(candidate => candidate.Guid.StartsWith("5cf600c9", StringComparison.Ordinal) && candidate.State == "before");

        var before = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);
        var phantom = before.Defects.First(static defect => defect.Kind == DimensionDefectKind.PhantomAnchor);
        Assert.Equal(DimensionDefectConfidence.Mechanical, phantom.Confidence);

        foreach (var part in state.Parts)
            part.SolidGeometryComplete = false;

        var after = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);

        Assert.DoesNotContain(after.Defects, static defect => defect.Kind == DimensionDefectKind.PhantomAnchor);
        Assert.Contains(after.Defects, defect =>
            defect.Kind == DimensionDefectKind.AnchorUnverified &&
            defect.Confidence == DimensionDefectConfidence.Provisional &&
            defect.DimensionId == phantom.DimensionId);
    }

    [Fact]
    public void WriteReportOverEveryCapturedState()
    {
        // Not an assertion — the grading table. It prints what the detector says about every
        // captured state so the findings can be read against what the person actually changed.
        if (!DimensionCaseLoader.HasCases)
        {
            _output.WriteLine("cases/ (gitignored local corpus) not present — skipping");
            return;
        }

        var text = new StringBuilder();

        foreach (var state in DimensionCaseLoader.LoadAll())
        {
            if (!state.IsUsable)
            {
                text.AppendLine($"== {state}  UNUSABLE: {state.LoadError}");
                text.AppendLine();
                continue;
            }

            var report = DimensionDefectDetector.Detect(state.ViewId, state.Dimensions, state.Parts, state.Coverage);
            text.AppendLine($"== {state}  view {state.ViewId}  chains {report.Chains.Count}  parts {state.Parts.Count}  coverage {state.Coverage.Count}");

            if (!report.CoordinateSpaceOk)
                text.AppendLine("   COORDINATE SPACE MISMATCH - no other check ran");

            foreach (var group in report.Defects.GroupBy(static defect => defect.Kind).OrderBy(static group => group.Key.ToString()))
            {
                foreach (var defect in group)
                {
                    var where = defect.Point is { Length: >= 2 }
                        ? $"({defect.Point[0]:0.#}, {defect.Point[1]:0.#})"
                        : "-";
                    text.AppendLine($"   {group.Key,-24} dim {defect.DimensionId,-5} {where,-20} {defect.Reason}");
                }
            }

            foreach (var warning in report.Warnings)
                text.AppendLine($"   ! {warning}");

            text.AppendLine();
        }

        var path = Path.Combine(Path.GetTempPath(), "dimension_defect_report.txt");
        File.WriteAllText(path, text.ToString());
        _output.WriteLine(path);
        _output.WriteLine(text.ToString());
    }
}
