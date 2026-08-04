using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.Dimensions.Defects;

/// <summary>
/// Runs the mechanical dimension checks over one view and reports findings instead of geometry.
///
/// Deliberately pure: it takes the read models the bridge already produces and touches no Tekla
/// API, so the same code runs against a live view and against the states captured under
/// `cases/dimension_cases/`. That is the point — the checks used to be rewritten as a throwaway
/// script on every drawing, and two implementations of "the same" check kept diverging.
///
/// It names defects. It does not decide layout: how many chains a sheet should carry and what
/// each one is for comes from the plant's habit, not from geometry.
/// </summary>
public static class DimensionDefectDetector
{
    /// <summary>
    /// How close a point must be to a part's bounding box to count as sitting on it. Points and
    /// part faces should coincide exactly; this only absorbs rounding.
    /// </summary>
    public const double PointOnPartToleranceMm = 0.5;

    /// <summary>Two spans count as equal within this. Same reasoning as above.</summary>
    public const double SpanMatchToleranceMm = 0.5;

    /// <summary>
    /// A point counts as far from its chain when its extension line runs this much further than
    /// the chain's own offset, as a fraction of the parts' extent across the chain.
    ///
    /// Measured as an EXCESS, not as a distance to the line: every point of a chain is offset
    /// from its line by construction, and for a horizontal chain drawn below the panel that
    /// offset is most of the drawing. The first version compared against the raw distance and
    /// reported 3660 of 3679 as a defect on a chain nobody had ever objected to.
    ///
    /// PROVISIONAL. Fitted to one drawing (EW.4-6): an extension line running 800 mm past the
    /// chain's offset on a 1890 mm panel was removed by hand, 278 mm on the same chain was kept.
    /// One drawing is not evidence for a threshold; grade it before acting on this class.
    /// </summary>
    public const double FarFromChainExcessFraction = 0.3;

    public static DimensionDefectReport Detect(
        int viewId,
        IReadOnlyList<DimensionContextInfo>? dimensions,
        IReadOnlyList<PartGeometryInViewResult>? parts,
        IReadOnlyList<DimensionChainCoverageResult>? coverage)
    {
        var report = new DimensionDefectReport { Success = true, ViewId = viewId, CoordinateSpaceOk = true };

        var chains = (dimensions ?? Array.Empty<DimensionContextInfo>())
            .Where(static dimension => dimension.PointAssociations.Count >= 2)
            .ToList();
        var partList = (parts ?? Array.Empty<PartGeometryInViewResult>())
            .Where(static part => part.BboxMin.Length >= 2 && part.BboxMax.Length >= 2)
            .ToList();

        if (chains.Count == 0)
        {
            report.Warnings.Add("no dimension chains with at least two points; nothing to check");
            return report;
        }

        if (partList.Count == 0)
        {
            report.Warnings.Add("no part geometry; every check depends on matching points to parts, so none ran");
            return report;
        }

        // Resolve the axis once, and drop what has none. Tekla also reports "angled" — a chain
        // whose segments are neither horizontal nor vertical, or a set mixing both. Control
        // diagonals come back that way, and they are never thinned: they are checked with a tape
        // on the assembly table while the panel is being built, so no rule here applies to them.
        // Treating anything non-vertical as horizontal silently graded 39 of the 210 captured
        // chains along the wrong axis.
        var oriented = chains
            .Select(static chain => new { Chain = chain, Axis = TryAxisOf(chain) })
            .ToList();

        foreach (var skipped in oriented.Where(static entry => entry.Axis == null))
            report.Warnings.Add(
                "chain " + skipped.Chain.DimensionId + " is '" +
                (string.IsNullOrEmpty(skipped.Chain.Orientation) ? "unknown" : skipped.Chain.Orientation) +
                "', neither horizontal nor vertical; no check ran on it");

        var axisChains = oriented.Where(static entry => entry.Axis != null)
                                 .Select(static entry => new { entry.Chain, Axis = entry.Axis!.Value })
                                 .ToList();

        if (axisChains.Count == 0)
        {
            report.Warnings.Add("no horizontal or vertical chain in this view; nothing to check");
            return report;
        }

        // Two different extents, for two different jobs.
        //
        // Structural bounds — timber only — decide what an overall is. Insulation and fittings
        // overhang the frame: on EW.4-6 a 10 mm strip reaches 1890 where the frame ends at 1880,
        // and an extent taken over everything stops recognising the 1880 overall as an overall.
        var structural = StructuralParts(partList);
        var extentX = Extent(structural, 0);
        var extentY = Extent(structural, 1);

        // Visible bounds — every part — gate the report. A dimension may legitimately reach an
        // insulation or fitting edge outside the frame; measuring the gate against timber alone
        // would call that a coordinate-space failure and switch every other check off.
        var visibleX = Extent(partList, 0);
        var visibleY = Extent(partList, 1);

        // The gate is a heuristic, not proof. It catches the failure actually seen: on IW.10 the
        // parts came back in model coordinates while the dimensions were in view coordinates —
        // the wall's height sat in the parts' Z and the chains' Y, and the parts' Y span was the
        // wall thickness. Chains measuring far more than anything visible cannot be matched to
        // parts, and every check below depends on that matching, so it is better to report
        // nothing than to report findings that look reasonable and mean nothing. It does NOT
        // prove the two are in the same space when it passes.
        foreach (var axis in new[] { 0, 1 })
        {
            var visible = axis == 0 ? visibleX : visibleY;
            var widest = oriented.Where(entry => entry.Axis == axis)
                                 .Select(entry => Span(entry.Chain, axis))
                                 .DefaultIfEmpty(0d)
                                 .Max();

            if (widest <= (visible * 1.05) + 1)
                continue;

            report.CoordinateSpaceOk = false;
            report.Defects.Add(new DimensionDefect
            {
                Kind = DimensionDefectKind.CoordinateSpaceMismatch,
                // Provisional, not Mechanical: this is a threshold on a symptom (chains reaching
                // far past what any part spans), not a read of an actual "these are/aren't the
                // same coordinate system" signal from Tekla — no such signal is read here. State
                // it as a suspicion serious enough to gate the rest of the report, not as a proven
                // fact, until a real coordinate-space check exists to confirm it.
                Confidence = DimensionDefectConfidence.Provisional,
                Reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "SUSPECTED, not proven: chains measure up to {0:0.#} along {1} while every visible part together spans only {2:0.#} there",
                    widest, axis == 0 ? "X" : "Y", visible)
            });
        }

        foreach (var entry in axisChains)
            report.Chains.Add(Summarize(entry.Chain, entry.Axis, entry.Axis == 0 ? extentX : extentY));

        if (!report.CoordinateSpaceOk)
        {
            report.Warnings.Add("suspected coordinate-space mismatch (see CoordinateSpaceMismatch below); no further check was run");
            return report;
        }

        foreach (var entry in axisChains)
        {
            AddRedundantSpans(report, entry.Chain, entry.Axis, partList, entry.Axis == 0 ? extentX : extentY);
            AddFarFromChain(report, entry.Chain, entry.Axis, visibleX, visibleY);
            AddWrongStartPoint(report, entry.Chain, partList);
        }

        AddAnchorDefects(report, axisChains.Select(static entry => entry.Chain).ToList(), coverage, partList);
        AddContainedChains(report, axisChains.Select(static entry => (entry.Chain, entry.Axis)).ToList());

        return report;
    }

    private static void AddRedundantSpans(
        DimensionDefectReport report,
        DimensionContextInfo chain,
        int axis,
        IReadOnlyList<PartGeometryInViewResult> parts,
        double extentAlong)
    {
        var points = OrderedPoints(chain, axis);

        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var span = to[axis] - from[axis];
            if (span <= SpanMatchToleranceMm)
                continue;

            // A span across the whole assembly is an overall, and an overall is never redundant.
            // Without this it fires on every drawing whose bottom plate runs the full length —
            // the plate's own size and the overall width are then the same number, and the check
            // reported the overall on states a person had already settled.
            if (Math.Abs(span - extentAlong) <= SpanMatchToleranceMm)
                continue;

            // Compare anchors, not lengths: what makes the span redundant is that one part
            // carries both ends and the span IS that part's size. There is no length threshold —
            // 45, 60, 120 and 160 have all turned up as genuine positions elsewhere.
            var culprit = parts.FirstOrDefault(part =>
                Covers(part, from) &&
                Covers(part, to) &&
                Math.Abs((part.BboxMax[axis] - part.BboxMin[axis]) - span) <= SpanMatchToleranceMm);

            if (culprit == null)
                continue;

            var name = culprit.PartPos ?? culprit.ModelId.ToString(CultureInfo.InvariantCulture);

            report.Defects.Add(new DimensionDefect
            {
                Kind = DimensionDefectKind.RedundantPartSizeSpan,
                Confidence = DimensionDefectConfidence.Mechanical,
                DimensionId = chain.DimensionId,
                Point = new[] { from[0], from[1] },
                SpanEnd = new[] { to[0], to[1] },
                ModelObjectId = culprit.ModelId,
                PartPos = culprit.PartPos,
                Reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "span {0:0.##} equals the own size of {1}, which is fixed at fabrication",
                    span, name)
            });
        }
    }

    private static void AddFarFromChain(
        DimensionDefectReport report,
        DimensionContextInfo chain,
        int axis,
        double extentX,
        double extentY)
    {
        var line = chain.ReferenceLine;
        if (line == null)
            return;

        // The chain runs along `axis`, so it is the OTHER coordinate that says how far a point
        // sits from the line.
        var across = axis == 1 ? 0 : 1;
        var lineAcross = across == 0 ? line.StartX : line.StartY;
        var extentAcross = across == 0 ? extentX : extentY;
        var limit = extentAcross * FarFromChainExcessFraction;
        if (limit <= 0)
            return;

        var points = OrderedPoints(chain, axis);

        // The chain's own offset is the smallest extension any of its points needs. Anything
        // beyond that is the extra length this one point drags across the drawing.
        var offset = points.Min(point => Math.Abs(point[across] - lineAcross));

        foreach (var point in points)
        {
            var distance = Math.Abs(point[across] - lineAcross);
            var excess = distance - offset;
            if (excess <= limit)
                continue;

            report.Defects.Add(new DimensionDefect
            {
                Kind = DimensionDefectKind.PointFarFromChain,
                Confidence = DimensionDefectConfidence.Provisional,
                DimensionId = chain.DimensionId,
                Point = new[] { point[0], point[1] },
                Reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "its extension line runs {0:0.#} mm past the chain's own offset of {1:0.#}, across parts spanning {2:0.#}",
                    excess, offset, extentAcross)
            });
        }
    }

    /// <summary>
    /// Read-back point order is normalised by Tekla (top-to-bottom / right-to-left) and does not
    /// reflect creation order — verified by reversing a chain's input points and comparing the
    /// resulting segment geometry, not by documentation. The true order survives in how segments
    /// connect: each segment's Start/End follows the real chain direction, so the point that is
    /// never anyone's End is the true start.
    /// </summary>
    private static void AddWrongStartPoint(
        DimensionDefectReport report,
        DimensionContextInfo chain,
        IReadOnlyList<PartGeometryInViewResult> parts)
    {
        if (chain.TeklaDimensionType.IndexOf("Absolute", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        var trueStart = FindGraphStart(chain.SegmentContexts);
        if (trueStart == null)
        {
            report.Warnings.Add(
                "chain " + chain.DimensionId +
                ": could not reconstruct a single true start point from its segments; start-point check skipped");
            return;
        }

        // Only meaningful where the chain actually reaches the frame somewhere. A chain
        // dimensioning only its own overlay layer (no frame member anywhere on it) has no frame
        // reference to start from, and reporting one would invent a rule the drawing does not
        // have a subject for.
        var reachesFrame = chain.PointAssociations.Any(association =>
            parts.Any(part =>
                string.Equals(part.PartPrefix, "T", StringComparison.OrdinalIgnoreCase) &&
                Covers(part, new[] { association.Point.X, association.Point.Y })));

        if (!reachesFrame)
            return;

        var startOnFrame = parts.Any(part =>
            string.Equals(part.PartPrefix, "T", StringComparison.OrdinalIgnoreCase) &&
            Covers(part, trueStart));

        if (startOnFrame)
            return;

        report.Defects.Add(new DimensionDefect
        {
            Kind = DimensionDefectKind.WrongStartPoint,
            Confidence = DimensionDefectConfidence.Mechanical,
            DimensionId = chain.DimensionId,
            Point = trueStart,
            Reason = "the chain's true start point (reconstructed from segment order) does not sit on a frame (T) part, although the chain reaches frame parts elsewhere"
        });
    }

    /// <summary>
    /// The point that is a segment's Start somewhere and never anyone's End is the one true
    /// source of the path. More or fewer than one such point means the segments do not form a
    /// single simple chain (branching, a loop, or disconnected pieces) — report nothing rather
    /// than guess which end was meant.
    /// </summary>
    private static double[]? FindGraphStart(IReadOnlyList<DimensionContextSegmentInfo> segments)
    {
        if (segments.Count == 0)
            return null;

        var starts = segments.Select(static s => new[] { s.Geometry.StartX, s.Geometry.StartY }).ToList();
        var ends = segments.Select(static s => new[] { s.Geometry.EndX, s.Geometry.EndY }).ToList();

        bool PointsClose(double[] a, double[] b) =>
            Math.Abs(a[0] - b[0]) <= PointOnPartToleranceMm && Math.Abs(a[1] - b[1]) <= PointOnPartToleranceMm;

        // A simple chain has at most one segment leaving and at most one segment entering a
        // point. Merely finding one source is insufficient: A->B, B->C and B->D also has one
        // source (A), but is a branch and its start must not be guessed.
        for (var i = 0; i < starts.Count; i++)
        {
            if (starts.Count(start => PointsClose(start, starts[i])) > 1 ||
                ends.Count(end => PointsClose(end, ends[i])) > 1)
            {
                return null;
            }
        }

        // A loop disconnected from an otherwise valid path can still leave exactly one source.
        // Verify that all segments belong to one undirected connected component before using
        // the directed Start/End information to recover the source.
        var visited = new bool[segments.Count];
        var pending = new Stack<int>();
        pending.Push(0);
        visited[0] = true;
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            for (var candidate = 0; candidate < segments.Count; candidate++)
            {
                if (visited[candidate])
                    continue;

                var connected = PointsClose(starts[current], starts[candidate]) ||
                                PointsClose(starts[current], ends[candidate]) ||
                                PointsClose(ends[current], starts[candidate]) ||
                                PointsClose(ends[current], ends[candidate]);
                if (!connected)
                    continue;

                visited[candidate] = true;
                pending.Push(candidate);
            }
        }

        if (visited.Any(static wasVisited => !wasVisited))
            return null;

        var candidates = starts.Where(start => !ends.Any(end => PointsClose(end, start))).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static void AddAnchorDefects(
        DimensionDefectReport report,
        IReadOnlyList<DimensionContextInfo> chains,
        IReadOnlyList<DimensionChainCoverageResult>? coverage,
        IReadOnlyList<PartGeometryInViewResult> parts)
    {
        var byChain = (coverage ?? Array.Empty<DimensionChainCoverageResult>())
            .Where(static result => result.Success)
            .GroupBy(static result => result.DimensionId)
            .ToDictionary(static group => group.Key, static group => group.First());

        var partsById = parts.GroupBy(static part => part.ModelId)
                             .ToDictionary(static group => group.Key, static group => group.First());

        var overalls = report.Chains.Where(static summary => summary.IsOverall)
                                    .Select(static summary => summary.DimensionId)
                                    .ToHashSet();

        foreach (var chain in chains)
        {
            // An overall reaches the extreme corners of the assembly, and on a raked or stepped
            // panel the lowest and the highest material are not on one part — so its ends land on
            // the assembly's bounding box by nature, not by mistake. Both anchor checks fired on
            // the EW.4-6 width overall, which a person had looked at and kept.
            if (overalls.Contains(chain.DimensionId))
                continue;

            if (!byChain.TryGetValue(chain.DimensionId, out var result))
            {
                // A skipped check must never read as a clean one. Anchor quality is the one thing
                // a bounding-box test cannot reproduce: it can say "no part's box contains this",
                // but not "no candidate exists here at all".
                report.Warnings.Add("no coverage for chain " + chain.DimensionId + "; its anchors were not checked");
                continue;
            }

            foreach (var warning in result.Warnings)
                report.Warnings.Add("coverage of chain " + chain.DimensionId + ": " + warning);

            foreach (var point in result.Points)
            {
                DimensionDefectKind kind;
                DimensionDefectConfidence confidence;
                string reason;

                if (point.Status == DimensionCoverageStatus.Missing)
                {
                    // Missing survives incomplete geometry: a bounding box is always computable,
                    // so "not even a box corner lands here" does not depend on the solid read.
                    kind = DimensionDefectKind.UnanchoredPoint;
                    confidence = DimensionDefectConfidence.Mechanical;
                    reason = "no candidate point of any part lands here";
                }
                else if (point.Status == DimensionCoverageStatus.Ambiguous)
                {
                    // Checked before FallbackOnly, not after: an ambiguous point can also be
                    // fallback-derived, and reporting it as a phantom or as unverified would
                    // silently drop the fact that several candidates disagree on where it sits.
                    // Ambiguous is not itself wrong — a stud meeting its plate is ambiguous on
                    // every wall — but which part the number belongs to is not decidable from
                    // geometry alone, so this is reported and never auto-applied.
                    kind = DimensionDefectKind.AmbiguousAnchor;
                    confidence = DimensionDefectConfidence.Provisional;
                    var names = point.Matches
                        .Select(match => partsById.TryGetValue(match.ModelObjectId, out var matchedPart)
                            ? matchedPart.PartPos ?? match.ModelObjectId.ToString(CultureInfo.InvariantCulture)
                            : match.ModelObjectId.ToString(CultureInfo.InvariantCulture))
                        .Distinct()
                        .ToList();
                    reason = point.Matches.Count + " candidates at different positions" +
                        (names.Count > 0 ? ", from " + string.Join(", ", names) : "") +
                        "; which one the number belongs to is not decidable from geometry alone";
                }
                else if (point.FallbackOnly)
                {
                    // fallbackOnly means every match was derived from a bounding box. That proves
                    // a phantom ONLY if EVERY distinct part behind those matches had its solid
                    // read in full — coverage does not pick a winner among candidates, so at a
                    // junction the true anchor may be the second, incomplete part rather than the
                    // complete one an Any() check would have been satisfied by. A part whose
                    // traversal failed has no face or vertex candidates to offer, so bounding-box
                    // matches are all it could ever produce, and the flag then says nothing about
                    // where the point sits.
                    var matchedIds = point.Matches.Select(static match => match.ModelObjectId).Distinct().ToList();
                    var provenByCompleteSolid = matchedIds.Count > 0 && matchedIds.All(id =>
                        partsById.TryGetValue(id, out var matched) && matched.SolidGeometryComplete);

                    kind = provenByCompleteSolid
                        ? DimensionDefectKind.PhantomAnchor
                        : DimensionDefectKind.AnchorUnverified;
                    confidence = provenByCompleteSolid
                        ? DimensionDefectConfidence.Mechanical
                        : DimensionDefectConfidence.Provisional;
                    reason = provenByCompleteSolid
                        ? "matches only a bounding-box corner although the solid was read in full, so nothing is actually there"
                        : "matches only a bounding-box corner, but the solid of every matched part is incomplete, so no face or vertex candidate could exist to match instead";
                }
                else
                {
                    continue;
                }

                partsById.TryGetValue(point.AssociatedModelId ?? -1, out var part);

                report.Defects.Add(new DimensionDefect
                {
                    Kind = kind,
                    Confidence = confidence,
                    DimensionId = chain.DimensionId,
                    Point = point.Point.Length >= 2 ? new[] { point.Point[0], point.Point[1] } : null,
                    ModelObjectId = part?.ModelId,
                    PartPos = part?.PartPos,
                    Reason = reason
                });
            }
        }
    }

    private static void AddContainedChains(
        DimensionDefectReport report,
        IReadOnlyList<(DimensionContextInfo Chain, int Axis)> chains)
    {
        var summaries = report.Chains.ToDictionary(static summary => summary.DimensionId);

        // Compare the positions each chain measures, expressed as offsets from its own first
        // point. That form catches a chain measuring the same parts from the opposite face,
        // which comparing raw coordinates would miss.
        var positions = new Dictionary<int, HashSet<double>>();
        foreach (var (chain, axis) in chains)
        {
            var values = OrderedPoints(chain, axis).Select(point => point[axis]).ToList();
            var zero = values[0];
            positions[chain.DimensionId] = new HashSet<double>(values.Skip(1).Select(value => Math.Round(value - zero, 3)));
        }

        foreach (var (chain, axis) in chains)
        {
            if (summaries.TryGetValue(chain.DimensionId, out var summary) && summary.IsOverall)
                continue;

            var mine = positions[chain.DimensionId];
            if (mine.Count == 0)
                continue;

            foreach (var (other, otherAxis) in chains)
            {
                if (other.DimensionId == chain.DimensionId || otherAxis != axis)
                    continue;

                var theirs = positions[other.DimensionId];

                // With identical position sets each chain contains the other, so without a
                // tie-break both would be reported and following the report would delete the
                // pair. Keep the lower id.
                if (theirs.Count < mine.Count)
                    continue;
                if (theirs.Count == mine.Count && other.DimensionId < chain.DimensionId)
                    continue;

                if (!mine.IsSubsetOf(theirs))
                    continue;

                report.Defects.Add(new DimensionDefect
                {
                    Kind = DimensionDefectKind.ContainedChain,
                    Confidence = DimensionDefectConfidence.Mechanical,
                    DimensionId = chain.DimensionId,
                    ContainedIn = other.DimensionId,
                    Reason = "every position it measures is also measured by chain " + other.DimensionId
                });
                break;
            }
        }
    }

    private static DimensionChainSummary Summarize(DimensionContextInfo chain, int axis, double extent)
    {
        var values = OrderedPoints(chain, axis).Select(point => point[axis]).ToList();
        var zero = values[0];

        var relative = new List<double>();
        for (var i = 1; i < values.Count; i++)
            relative.Add(Math.Round(values[i] - values[i - 1], 3));

        return new DimensionChainSummary
        {
            DimensionId = chain.DimensionId,
            Orientation = chain.Orientation,
            TeklaDimensionType = chain.TeklaDimensionType,
            Distance = chain.Distance,
            PointCount = values.Count,
            RelativeRow = relative,
            AbsoluteRowFromReadOrder = values.Skip(1).Select(value => Math.Round(value - zero, 3)).ToList(),

            // An overall is two points spanning everything there is. Stated as a test rather than
            // taken on trust, because it is the one exemption from the containment check and an
            // exemption that cannot be checked is an invitation to keep a duplicate.
            IsOverall = values.Count == 2 && Math.Abs((values[1] - values[0]) - extent) <= SpanMatchToleranceMm
        };
    }

    private static bool Covers(PartGeometryInViewResult part, double[] point) =>
        point[0] >= part.BboxMin[0] - PointOnPartToleranceMm &&
        point[0] <= part.BboxMax[0] + PointOnPartToleranceMm &&
        point[1] >= part.BboxMin[1] - PointOnPartToleranceMm &&
        point[1] <= part.BboxMax[1] + PointOnPartToleranceMm;

    private static double Extent(IReadOnlyList<PartGeometryInViewResult> parts, int axis) =>
        parts.Max(part => part.BboxMax[axis]) - parts.Min(part => part.BboxMin[axis]);

    /// <summary>
    /// The parts that define the assembly's size. Tekla MATERIAL_TYPE 5 is timber and is the
    /// reliable signal; where it is missing the mark prefix says the same thing, R being
    /// insulation and M a fitting. Falls back to everything rather than to nothing, because an
    /// extent of zero would silently disable the overall test.
    /// </summary>
    private static IReadOnlyList<PartGeometryInViewResult> StructuralParts(IReadOnlyList<PartGeometryInViewResult> parts)
    {
        var structural = parts
            .Where(static part => part.MaterialType == 5 ||
                                  (part.MaterialType < 0 &&
                                   !string.Equals(part.PartPrefix, "R", StringComparison.OrdinalIgnoreCase) &&
                                   !string.Equals(part.PartPrefix, "M", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return structural.Count > 0 ? structural : parts;
    }

    private static double Span(DimensionContextInfo chain, int axis)
    {
        var values = chain.PointAssociations.Select(association => Coordinate(association, axis)).ToList();
        return values.Max() - values.Min();
    }

    private static List<double[]> OrderedPoints(DimensionContextInfo chain, int axis) =>
        chain.PointAssociations
             .Select(static association => new[] { association.Point.X, association.Point.Y })
             .OrderBy(point => point[axis])
             .ToList();

    private static double Coordinate(DimensionContextPointAssociationInfo association, int axis) =>
        axis == 0 ? association.Point.X : association.Point.Y;

    /// <summary>
    /// 1 for a vertical chain (it measures along Y), 0 for a horizontal one, null for anything
    /// else — Tekla reports "angled" for a diagonal or for a set mixing both directions, and no
    /// check here has a meaning along an axis the chain does not have.
    /// </summary>
    private static int? TryAxisOf(DimensionContextInfo chain) =>
        string.Equals(chain.Orientation, "vertical", StringComparison.OrdinalIgnoreCase) ? 1
        : string.Equals(chain.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase) ? 0
        : null;
}
