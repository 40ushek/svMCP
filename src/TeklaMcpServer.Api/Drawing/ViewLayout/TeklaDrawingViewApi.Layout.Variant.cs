using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    private DrawingLayoutVariantResult RunDefaultLayoutVariant(SharedLayoutContext ctx)
    {
        var workspace          = ctx.Workspace;
        var drawing            = ctx.Drawing;
        var offsetById         = ctx.OffsetById.ToDictionary(x => x.Key, x => x.Value);
        var sheetW             = workspace.SheetWidth;
        var sheetH             = workspace.SheetHeight;
        var gap                = ctx.Gap;
        var optimalScale       = ctx.OptimalScale;
        var effectiveMargin    = ctx.EffectiveMargin;
        var allowTeklaMutation = ctx.AllowTeklaMutation;
        var preserveExistingScales = ctx.PreserveExistingScales;
        var reserved           = workspace.ReservedAreas;

        // ── Arrange ──────────────────────────────────────────────────────────
        var arrangeSw = Stopwatch.StartNew();
        PerfTrace.Write(
            "api-view",
            "layout_branch",
            0,
            $"branch=arrangement-selector action={(ctx.ArrangedViews.Count == 0 ? "skip-no-views" : "run")} views={ctx.ArrangedViews.Count}");
        var arranged = ctx.ArrangedViews.Count == 0
            ? new List<ArrangedView>()
            : _arrangementSelector.Arrange(
                new DrawingArrangeContext(drawing, workspace, ctx.ArrangedViews, gap, applyChanges: false));
        arrangeSw.Stop();
        var arrangeMs = arrangeSw.ElapsedMilliseconds;
        PerfTrace.Write(
            "api-view",
            "layout_stage",
            0,
            $"stage=primary-arrangement result=ok arranged={arranged.Count} elapsedMs={arrangeMs}");

        // ── Detail scale restore (Tekla mutation) ─────────────────────────────
        var detailScalesChanged = false;
        if (!preserveExistingScales)
        {
            foreach (var detailView in ctx.CurrentViews.Where(v => workspace.GetSemanticKind(v.GetIdentifier().ID) == ViewSemanticKind.Detail))
            {
                if (!workspace.OriginalScalesById.TryGetValue(detailView.GetIdentifier().ID, out var detailScale))
                    continue;
                if (detailScale <= 0 || System.Math.Abs(detailView.Attributes.Scale - detailScale) < 0.01)
                    continue;
                if (allowTeklaMutation)
                {
                    detailView.Attributes.Scale = detailScale;
                    if (detailView.Modify())
                        detailScalesChanged = true;
                }
            }
        }

        // ── Frame-offset correction ───────────────────────────────────────────
        var postAdjustMs = 0L;
        if (offsetById.Count > 0)
        {
            var adjustSw = Stopwatch.StartNew();
            for (int i = 0; i < arranged.Count; i++)
            {
                if (!workspace.RuntimeViewsById.TryGetValue(arranged[i].Id, out var v))
                    continue;
                if (!offsetById.TryGetValue(arranged[i].Id, out var off))
                    continue;

                var fallbackScale = v.Attributes.Scale > 0 ? v.Attributes.Scale : optimalScale;
                var correctionScale = workspace.GetSelectedScale(arranged[i].Id, fallbackScale);
                var corrX = off.X / correctionScale;
                var corrY = off.Y / correctionScale;
                var semanticKind = workspace.GetSemanticKind(arranged[i].Id);
                var selectedFrameSize = workspace.GetSelectedFrameSize(arranged[i].Id, v.Width, v.Height);
                var maxPlausibleCorrection = System.Math.Max(selectedFrameSize.Width, selectedFrameSize.Height) * 2.0;
                if (semanticKind != ViewSemanticKind.Detail
                    && maxPlausibleCorrection > 0
                    && (System.Math.Abs(corrX) > maxPlausibleCorrection || System.Math.Abs(corrY) > maxPlausibleCorrection))
                {
                    PerfTrace.Write(
                        "api-view",
                        "view_frame_offset_skip",
                        0,
                        $"view={arranged[i].Id} reason=implausible offset=({corrX:F2},{corrY:F2}) limit={maxPlausibleCorrection:F2}");
                    continue;
                }

                var currentOrigin = v.Origin;
                var o = new Point(currentOrigin?.X ?? 0, currentOrigin?.Y ?? 0, currentOrigin?.Z ?? 0);
                o.X = arranged[i].OriginX - corrX;
                o.Y = arranged[i].OriginY - corrY;
                arranged[i] = new ArrangedView
                {
                    Id                    = arranged[i].Id,
                    ViewType              = arranged[i].ViewType,
                    OriginX               = o.X,
                    OriginY               = o.Y,
                    PreferredPlacementSide = arranged[i].PreferredPlacementSide,
                    ActualPlacementSide   = arranged[i].ActualPlacementSide,
                    PlacementFallbackUsed = arranged[i].PlacementFallbackUsed,
                    LayoutMargin          = arranged[i].LayoutMargin,
                    LayoutGap             = arranged[i].LayoutGap,
                    IsSnapshotFallback    = arranged[i].IsSnapshotFallback
                };
            }

            adjustSw.Stop();
            postAdjustMs = adjustSw.ElapsedMilliseconds;
        }

        var selectedLayoutMargin = ResolveSelectedLayoutMargin(effectiveMargin, arranged);
        var selectedLayoutGap    = ResolveSelectedLayoutGap(gap, arranged);
        PerfTrace.Write(
            "api-view",
            "fit_layout_spacing",
            0,
            $"effectiveMargin={effectiveMargin:F1} effectiveGap={gap:F1} selectedMargin={selectedLayoutMargin:F1} selectedGap={selectedLayoutGap:F1}");

        if (allowTeklaMutation)
        {
            TracePlannedVsActualParity(
                "post-arrange-pre-projection",
                workspace,
                arranged,
                DrawingViewFrameGeometry.BuildActualViewRects(drawing));
        }

        // ── Projection alignment ──────────────────────────────────────────────
        var projectionSw = Stopwatch.StartNew();
        var projectionScaleGuardViews = ctx.ArrangedViews
            .Where(v => IsUniformScaleDriverKind(workspace.GetSemanticKind(v.GetIdentifier().ID)))
            .ToList();
        ProjectionAlignmentResult projectionResult;
        if (ShouldSkipProjectionAlignment(optimalScale, projectionScaleGuardViews, out var projectionSkipMode, out var projectionSkipDiagnostic))
        {
            PerfTrace.Write(
                "api-view",
                "layout_branch",
                0,
                $"branch=projection action=skip mode={projectionSkipMode} reason={projectionSkipDiagnostic}");
            projectionResult = new ProjectionAlignmentResult { Mode = projectionSkipMode, SkippedMoves = 1 };
            if (!string.IsNullOrWhiteSpace(projectionSkipDiagnostic))
                projectionResult.Diagnostics.Add(projectionSkipDiagnostic);
        }
        else
        {
            PerfTrace.Write("api-view", "layout_branch", 0, "branch=projection action=run");
            projectionResult = new DrawingProjectionAlignmentService(new Tekla.Structures.Model.Model()).Apply(
                drawing, workspace, ctx.ArrangedViews, arranged);
        }
        projectionSw.Stop();
        var projectionMs = projectionSw.ElapsedMilliseconds;

        // ── Detail-scale commit + offset refresh ──────────────────────────────
        var commitSw = Stopwatch.StartNew();
        if (allowTeklaMutation && detailScalesChanged)
            drawing.CommitChanges();
        commitSw.Stop();
        var finalCommitMs = allowTeklaMutation && detailScalesChanged ? commitSw.ElapsedMilliseconds : 0L;

        var postProjectionViews = allowTeklaMutation
            ? EnumerateViews(drawing).ToList()
            : ctx.CurrentViews;
        workspace.SetRuntimeViews(postProjectionViews);

        if (allowTeklaMutation && detailScalesChanged)
        {
            var actualRects = DrawingViewFrameGeometry.BuildActualViewRects(drawing);
            workspace.SetActualViewRects(actualRects);
            var detailIds = postProjectionViews
                .Where(view => workspace.GetSemanticKind(view.GetIdentifier().ID) == ViewSemanticKind.Detail)
                .Select(view => view.GetIdentifier().ID)
                .ToHashSet();
            var refreshedSizes = DrawingViewFrameGeometry.TryGetFrameSizes(postProjectionViews, actualRects);
            var mergedSizes = workspace.SelectedFrameSizesById.ToDictionary(static item => item.Key, static item => item.Value);
            foreach (var item in refreshedSizes.Where(item => detailIds.Contains(item.Key)))
                mergedSizes[item.Key] = item.Value;
            workspace.SetSelectedFrameSizes(mergedSizes);

            var refreshedOffsets = DrawingViewFrameGeometry.TryGetFrameOffsets(postProjectionViews, actualRects);
            var mergedOffsets = offsetById.ToDictionary(static item => item.Key, static item => item.Value);
            foreach (var item in refreshedOffsets.Where(item => detailIds.Contains(item.Key)))
                mergedOffsets[item.Key] = item.Value;
            offsetById = mergedOffsets;
            workspace.SetFrameOffsets(offsetById);
        }

        // ── Centering ─────────────────────────────────────────────────────────
        var finalArrangedViews = postProjectionViews
            .Where(v =>
            {
                var id   = v.GetIdentifier().ID;
                var kind = workspace.GetSemanticKind(id);
                return kind != ViewSemanticKind.Detail
                    && kind != ViewSemanticKind.Other
                    && kind != ViewSemanticKind.Model3D
                    && workspace.GetLayoutViewKind(id) != LayoutViewKind.AnchorDetailSection;
            })
            .ToList();
        arranged = TryCenterViewGroup(drawing, workspace, finalArrangedViews, arranged,
            selectedLayoutMargin, sheetW - selectedLayoutMargin,
            selectedLayoutMargin, sheetH - selectedLayoutMargin,
            reserved, allowTeklaMutation);
        PerfTrace.Write("api-view", "layout_stage", 0, $"stage=center-group result=done arranged={arranged.Count}");

        var finalViews = allowTeklaMutation ? EnumerateViews(drawing).ToList() : postProjectionViews;
        workspace.SetRuntimeViews(finalViews);

        // ── Detail reposition ─────────────────────────────────────────────────
        arranged = TryRepositionDetailViews(
            workspace, finalViews, arranged,
            selectedLayoutMargin, sheetW - selectedLayoutMargin,
            selectedLayoutMargin, sheetH - selectedLayoutMargin,
            selectedLayoutGap, reserved, offsetById);
        PerfTrace.Write("api-view", "layout_stage", 0, $"stage=detail-reposition result=done arranged={arranged.Count}");

        finalViews = allowTeklaMutation ? EnumerateViews(drawing).ToList() : finalViews;
        workspace.SetRuntimeViews(finalViews);

        // ── Free-view reposition ──────────────────────────────────────────────
        var arrangedBeforeFree = arranged.ToList();
        arranged = TryRepositionFreeViews(
            workspace, finalViews, arranged,
            selectedLayoutMargin, sheetW - selectedLayoutMargin,
            selectedLayoutMargin, sheetH - selectedLayoutMargin,
            selectedLayoutGap, reserved);
        PerfTrace.Write("api-view", "layout_stage", 0, $"stage=free-view-reposition result=done arranged={arranged.Count}");

        finalViews = allowTeklaMutation ? EnumerateViews(drawing).ToList() : finalViews;
        workspace.SetRuntimeViews(finalViews);

        // ── Build candidates ──────────────────────────────────────────────────
        var beforeFreeCandidate = DrawingLayoutCandidateBuilder.FromPlannedViews(
            "fit_views_to_sheet:planned-before-free",
            workspace,
            DrawingLayoutCandidateBuilder.ToPlannedViews(workspace, workspace.RuntimeViews, arrangedBeforeFree));
        DrawingLayoutCandidateBuilder.AttachFallbackStackOrderGroups(beforeFreeCandidate, workspace.RuntimeViews);

        var finalCandidate = DrawingLayoutCandidateBuilder.FromPlannedViews(
            "fit_views_to_sheet:planned-final",
            workspace,
            DrawingLayoutCandidateBuilder.ToPlannedViews(workspace, workspace.RuntimeViews, arranged));
        DrawingLayoutCandidateBuilder.AttachFallbackStackOrderGroups(finalCandidate, workspace.RuntimeViews);

        return new DrawingLayoutVariantResult
        {
            Candidates         = new[] { beforeFreeCandidate, finalCandidate },
            Arranged           = arranged,
            ArrangedBeforeFree = arrangedBeforeFree,
            ProjectionResult   = projectionResult,
            ArrangeMs          = arrangeMs,
            PostAdjustMs       = postAdjustMs,
            ProjectionMs       = projectionMs,
            FinalCommitMs      = finalCommitMs
        };
    }
}
