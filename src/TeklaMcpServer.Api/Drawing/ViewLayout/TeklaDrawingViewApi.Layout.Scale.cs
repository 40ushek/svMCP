using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    private readonly struct KeepScaleFitResult
    {
        public KeepScaleFitResult(
            IReadOnlyDictionary<int, (double Width, double Height)> frameSizes,
            double optimalScale,
            long elapsedMilliseconds)
        {
            FrameSizes = frameSizes;
            OptimalScale = optimalScale;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public IReadOnlyDictionary<int, (double Width, double Height)> FrameSizes { get; }
        public double OptimalScale { get; }
        public long ElapsedMilliseconds { get; }
    }

    internal const double ScaleEstimateOversizeTolerance = 1.05;

    private readonly struct CandidateScaleProbeResult
    {
        public CandidateScaleProbeResult(
            List<View> views,
            IReadOnlyDictionary<int, (double Width, double Height)> frameSizes,
            IReadOnlyList<(double w, double h)> frames,
            long elapsedMilliseconds,
            IReadOnlyDictionary<int, (double Width, double Height)>? estimatedFrameSizes = null,
            bool teklaMutationApplied = false,
            bool preRejectedByEstimate = false)
        {
            Views = views;
            FrameSizes = frameSizes;
            Frames = frames;
            ElapsedMilliseconds = elapsedMilliseconds;
            EstimatedFrameSizes = estimatedFrameSizes;
            TeklaMutationApplied = teklaMutationApplied;
            PreRejectedByEstimate = preRejectedByEstimate;
        }

        public List<View> Views { get; }
        public IReadOnlyDictionary<int, (double Width, double Height)> FrameSizes { get; }
        public IReadOnlyList<(double w, double h)> Frames { get; }
        public long ElapsedMilliseconds { get; }
        public IReadOnlyDictionary<int, (double Width, double Height)>? EstimatedFrameSizes { get; }
        public bool TeklaMutationApplied { get; }
        public bool PreRejectedByEstimate { get; }
    }

    private static List<(int Id, string ViewType, double W, double H)> FindEstimateOversizeViews(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> estimatedSizes,
        double availW,
        double availH,
        double tolerance)
    {
        var result = new List<(int, string, double, double)>();
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (!estimatedSizes.TryGetValue(id, out var size))
                continue;
            if (size.Width > availW * tolerance || size.Height > availH * tolerance)
                result.Add((id, view.ViewType.ToString(), size.Width, size.Height));
        }
        return result;
    }

    private static double ResolveTargetScale(
        View view,
        ViewSemanticKind semanticKind,
        double candidateScale,
        bool uniformAllNonDetail,
        IReadOnlyDictionary<int, double> originalScales,
        SecondaryScalePolicy secondaryScalePolicy = SecondaryScalePolicy.SameAsMain)
    {
        if (!originalScales.TryGetValue(view.GetIdentifier().ID, out var originalScale))
            originalScale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;

        if (semanticKind == ViewSemanticKind.Detail)
            return originalScale;

        if (semanticKind is ViewSemanticKind.Other or ViewSemanticKind.Model3D)
            return originalScale;

        // Secondary policy for Section takes priority over uniformAllNonDetail when active:
        // it explicitly opts this Section out of uniform scaling.
        if (semanticKind == ViewSemanticKind.Section
            && (secondaryScalePolicy == SecondaryScalePolicy.PreserveIfNotSmaller
                || secondaryScalePolicy == SecondaryScalePolicy.PreserveLargerIfFits))
        {
            // originalScale denominator < candidateScale denominator → original is larger scale
            // Keep it; if it doesn't fit, EstimateCandidateFrameSizes downgrades.
            // If original is equal or smaller — downgrade to candidateScale (never go below main).
            return originalScale < candidateScale ? originalScale : candidateScale;
        }

        if (uniformAllNonDetail)
            return candidateScale;

        if (semanticKind == ViewSemanticKind.BaseProjected)
            return candidateScale;

        return originalScale;
    }

    internal static bool IsUniformScaleDriverKind(ViewSemanticKind semanticKind)
        => semanticKind is ViewSemanticKind.BaseProjected or ViewSemanticKind.Section;

    private static List<DrawingFitConflict> BuildOversizeConflicts(
        IReadOnlyList<View> views,
        IReadOnlyList<(double w, double h)> frames,
        double availW,
        double availH)
    {
        var conflicts = new List<DrawingFitConflict>();
        for (int i = 0; i < views.Count && i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.w <= availW && frame.h <= availH)
                continue;

            conflicts.Add(new DrawingFitConflict
            {
                ViewId = views[i].GetIdentifier().ID,
                ViewType = views[i].ViewType.ToString(),
                AttemptedZone = "sheet",
                Conflicts = new List<DrawingFitConflictItem>
                {
                    new()
                    {
                        Type = "sheet-oversize",
                        Target = $"usable={availW:F1}x{availH:F1};view={frame.w:F1}x{frame.h:F1}"
                    }
                }
            });
        }

        return conflicts;
    }

    private static List<(double w, double h)> BuildFrameList(
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> frameSizes)
        => views
            .Select(v =>
            {
                if (frameSizes.TryGetValue(v.GetIdentifier().ID, out var size))
                    return (w: size.Width, h: size.Height);

                return (w: v.Width, h: v.Height);
            })
            .ToList();

    internal static HashSet<int> CollectOversizedStandardSectionScaleDriverIds(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        double gap)
    {
        var baseViewSelection = BaseViewSelection.Select(views);
        var baseView = baseViewSelection.View;
        if (baseView == null)
            return new HashSet<int>();

        var baseId = baseView.GetIdentifier().ID;
        var baseFrame = workspace.GetSelectedFrameSize(baseId, baseView.Width, baseView.Height);
        var baseWidth = baseFrame.Width;
        var baseHeight = baseFrame.Height;
        var semanticViews = SemanticViewSet.Build(views);
        var sectionGroups = SectionGroupSet.Build(
            semanticViews.Sections,
            drawing,
            baseView,
            new SectionPlacementSideResolver(new Model()),
            workspace,
            semanticViews.BaseProjected);

        var result = new HashSet<int>();
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Top, SectionPlacementSide.Top, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Bottom, SectionPlacementSide.Bottom, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Left, SectionPlacementSide.Left, baseWidth, baseHeight, gap);
        CollectOversizedStandardSectionScaleDriverIds(workspace, result, sectionGroups.Right, SectionPlacementSide.Right, baseWidth, baseHeight, gap);
        return result;
    }

    private static void CollectOversizedStandardSectionScaleDriverIds(
        DrawingLayoutWorkspace workspace,
        HashSet<int> oversizedIds,
        IReadOnlyList<View> sections,
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double gap)
    {
        foreach (var section in sections)
        {
            var sectionId = section.GetIdentifier().ID;
            var size = workspace.GetSelectedFrameSize(sectionId, section.Width, section.Height);
            var width = size.Width;
            var height = size.Height;
            if (BaseProjectedDrawingArrangeStrategy.IsOversizedStandardSection(placementSide, baseWidth, baseHeight, width, height, gap))
                oversizedIds.Add(sectionId);
        }
    }

    internal static bool IsOversizedStandardSectionScaleDriver(
        SectionPlacementSide placementSide,
        double baseWidth,
        double baseHeight,
        double sectionWidth,
        double sectionHeight,
        double gap)
        => BaseProjectedDrawingArrangeStrategy.IsOversizedStandardSection(
            placementSide,
            baseWidth,
            baseHeight,
            sectionWidth,
            sectionHeight,
            gap);


    private KeepScaleFitResult ValidateCurrentScaleFit(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> currentViews,
        IReadOnlyDictionary<int, ReservedRect> actualRects,
        double gap,
        double availW,
        double availH,
        double currentScale,
        string stage,
        string oversizeMessage,
        string fitFailedMessage)
    {
        var keepFrameSizes = DrawingViewFrameGeometry.TryGetFrameSizes(currentViews, actualRects);
        var keepFrames = BuildFrameList(currentViews, keepFrameSizes);
        var keepCtx = new DrawingArrangeContext(drawing, workspace, currentViews, gap, keepFrameSizes);
        var keepFitSw = Stopwatch.StartNew();
        var oversizeConflicts = BuildOversizeConflicts(currentViews, keepFrames, availW, availH);
        if (oversizeConflicts.Count > 0)
        {
            TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                stage,
                currentScale,
                fits: false,
                oversizeConflicts,
                diagnosedConflicts: null));
            throw new DrawingFitFailedException(oversizeMessage, oversizeConflicts);
        }

        var fits = _arrangementSelector.EstimateFit(keepCtx, keepFrames);
        TraceScaleCandidate(currentScale, currentViews, keepFrames, fits, oversizeConflicts);
        keepFitSw.Stop();

        if (!fits)
        {
            var conflicts = _arrangementSelector.DiagnoseFitConflicts(keepCtx, keepFrames);
            TraceEstimateFailureDecision(new EstimateFitFailureDecision(
                stage,
                currentScale,
                fits: false,
                oversizeConflicts: null,
                diagnosedConflicts: conflicts));
            throw new DrawingFitFailedException(fitFailedMessage, conflicts);
        }

        var optimalScale = currentViews
            .Select(v => v.Attributes.Scale)
            .Where(s => s > 0)
            .DefaultIfEmpty(1.0)
            .Max();
        return new KeepScaleFitResult(keepFrameSizes, optimalScale, keepFitSw.ElapsedMilliseconds);
    }

    private static CandidateScaleProbeResult ProbeCandidateScale(
        Tekla.Structures.Drawing.Drawing drawing,
        DrawingLayoutWorkspace workspace,
        List<View> currentViews,
        IReadOnlyDictionary<int, (double Width, double Height)> originalFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail,
        bool applyProbe,
        double availW,
        double availH,
        SecondaryScalePolicy secondaryScalePolicy = SecondaryScalePolicy.SameAsMain)
    {
        var probeSw = Stopwatch.StartNew();
        var estimatedSizes = EstimateCandidateFrameSizes(
            workspace,
            currentViews,
            originalFrameSizes,
            candidateScale,
            uniformAllNonDetail,
            secondaryScalePolicy,
            availW,
            availH,
            out var resolvedScales);

        // Log estimate before any Modify() so it reflects pre-mutation state
        TraceScaleCandidateEstimate(candidateScale, workspace, currentViews, estimatedSizes, availW, availH);

        // Pre-reject by estimate before mutating Tekla: if any view clearly exceeds the sheet
        // within tolerance, skip Modify() entirely to avoid visible scale jumps on screen.
        if (applyProbe && availW > 0 && availH > 0)
        {
            var overEstimate = FindEstimateOversizeViews(currentViews, estimatedSizes, availW, availH, ScaleEstimateOversizeTolerance);
            if (overEstimate.Count > 0)
            {
                var estimatedFrames = BuildFrameList(currentViews, estimatedSizes);
                probeSw.Stop();
                TraceScaleCandidatePreReject(candidateScale, overEstimate, ScaleEstimateOversizeTolerance);
                return new CandidateScaleProbeResult(
                    currentViews,
                    estimatedSizes,
                    estimatedFrames,
                    probeSw.ElapsedMilliseconds,
                    estimatedFrameSizes: estimatedSizes,
                    preRejectedByEstimate: true);
            }
        }

        if (!applyProbe)
        {
            var estimatedFrames = BuildFrameList(currentViews, estimatedSizes);
            probeSw.Stop();
            PerfTrace.Write(
                "api-view",
                "fit_scale_probe",
                probeSw.ElapsedMilliseconds,
                $"mode=virtual candidateScale=1:{candidateScale.ToString("0.###", CultureInfo.InvariantCulture)} views={currentViews.Count}");
            return new CandidateScaleProbeResult(
                currentViews,
                estimatedSizes,
                estimatedFrames,
                probeSw.ElapsedMilliseconds,
                estimatedFrameSizes: estimatedSizes);
        }

        var anyScaleChanged = false;
        foreach (var view in currentViews)
        {
            var id = view.GetIdentifier().ID;
            var targetScale = resolvedScales.TryGetValue(id, out var rs) ? rs
                : ResolveTargetScale(
                    view,
                    workspace.GetSemanticKind(id),
                    candidateScale,
                    uniformAllNonDetail,
                    workspace.OriginalScalesById,
                    secondaryScalePolicy);
            if (System.Math.Abs(view.Attributes.Scale - targetScale) < 0.01)
                continue;

            view.Attributes.Scale = targetScale;
            view.Modify();
            anyScaleChanged = true;
        }

        if (anyScaleChanged)
            drawing.CommitChanges();
        probeSw.Stop();

        var candidateViews = anyScaleChanged
            ? EnumerateViews(drawing).ToList()
            : currentViews;
        var effectiveFrameSizes = anyScaleChanged
            ? DrawingViewFrameGeometry.TryGetFrameSizes(candidateViews)
            : originalFrameSizes;
        var actualFrames = BuildFrameList(candidateViews, effectiveFrameSizes);

        return new CandidateScaleProbeResult(
            candidateViews,
            effectiveFrameSizes,
            actualFrames,
            probeSw.ElapsedMilliseconds,
            estimatedFrameSizes: estimatedSizes,
            teklaMutationApplied: anyScaleChanged);
    }

    private static IReadOnlyDictionary<int, (double Width, double Height)> EstimateCandidateFrameSizes(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> originalFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail,
        SecondaryScalePolicy secondaryScalePolicy,
        double availW,
        double availH,
        out IReadOnlyDictionary<int, double> resolvedScales)
    {
        var frameSizes = new Dictionary<int, (double Width, double Height)>(views.Count);
        var scales = new Dictionary<int, double>(views.Count);
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            var originalScale = workspace.OriginalScalesById.TryGetValue(id, out var storedScale) && storedScale > 0
                ? storedScale
                : (view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0);
            var targetScale = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(id),
                candidateScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById,
                secondaryScalePolicy);
            var frame = originalFrameSizes.TryGetValue(id, out var storedFrame)
                ? storedFrame
                : (view.Width, view.Height);
            var factor = targetScale > 0 ? originalScale / targetScale : 1.0;
            var estimatedW = frame.Width * factor;
            var estimatedH = frame.Height * factor;

            // PreserveLargerIfFits: if the preserved larger scale makes this view exceed the
            // available sheet area, fall back to candidateScale for this view.
            if (secondaryScalePolicy == SecondaryScalePolicy.PreserveLargerIfFits
                && targetScale < candidateScale   // view was promoted to larger scale
                && availW > 0 && availH > 0
                && (estimatedW > availW * ScaleEstimateOversizeTolerance
                    || estimatedH > availH * ScaleEstimateOversizeTolerance))
            {
                targetScale = candidateScale;
                factor = originalScale / targetScale;
                estimatedW = frame.Width * factor;
                estimatedH = frame.Height * factor;
            }

            frameSizes[id] = (estimatedW, estimatedH);
            scales[id] = targetScale;
            TraceSecondaryScaleDecision(workspace, view, id, originalScale, targetScale, candidateScale, secondaryScalePolicy);
        }

        resolvedScales = scales;
        return frameSizes;
    }

    private static IReadOnlyDictionary<int, double> ResolveSelectedScales(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        double selectedScale,
        bool uniformAllNonDetail,
        bool preserveCurrentScales,
        SecondaryScalePolicy secondaryScalePolicy = SecondaryScalePolicy.SameAsMain)
    {
        var result = new Dictionary<int, double>(views.Count);
        foreach (var view in views)
        {
            var id = view.GetIdentifier().ID;
            if (preserveCurrentScales)
            {
                result[id] = workspace.OriginalScalesById.TryGetValue(id, out var originalScale) && originalScale > 0
                    ? originalScale
                    : (view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0);
                continue;
            }

            result[id] = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(id),
                selectedScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById,
                secondaryScalePolicy);
        }

        return result;
    }

}
