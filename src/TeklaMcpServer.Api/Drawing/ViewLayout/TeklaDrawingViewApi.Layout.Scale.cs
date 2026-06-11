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

    private readonly struct CandidateScaleProbeResult
    {
        public CandidateScaleProbeResult(
            List<View> views,
            IReadOnlyDictionary<int, (double Width, double Height)> frameSizes,
            IReadOnlyList<(double w, double h)> frames,
            long elapsedMilliseconds)
        {
            Views = views;
            FrameSizes = frameSizes;
            Frames = frames;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public List<View> Views { get; }
        public IReadOnlyDictionary<int, (double Width, double Height)> FrameSizes { get; }
        public IReadOnlyList<(double w, double h)> Frames { get; }
        public long ElapsedMilliseconds { get; }
    }

    private static double ResolveTargetScale(
        View view,
        ViewSemanticKind semanticKind,
        double candidateScale,
        bool uniformAllNonDetail,
        IReadOnlyDictionary<int, double> originalScales)
    {
        if (!originalScales.TryGetValue(view.GetIdentifier().ID, out var originalScale))
            originalScale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;

        if (semanticKind == ViewSemanticKind.Detail)
            return originalScale;

        if (semanticKind == ViewSemanticKind.Other)
            return originalScale;

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
            new SectionPlacementSideResolver(new Model()));

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
        bool applyProbe)
    {
        var probeSw = Stopwatch.StartNew();
        if (!applyProbe)
        {
            var estimatedFrameSizes = EstimateCandidateFrameSizes(
                workspace,
                currentViews,
                originalFrameSizes,
                candidateScale,
                uniformAllNonDetail);
            var estimatedFrames = BuildFrameList(currentViews, estimatedFrameSizes);
            probeSw.Stop();
            PerfTrace.Write(
                "api-view",
                "fit_scale_probe",
                probeSw.ElapsedMilliseconds,
                $"mode=virtual candidateScale=1:{candidateScale.ToString("0.###", CultureInfo.InvariantCulture)} views={currentViews.Count}");
            return new CandidateScaleProbeResult(
                currentViews,
                estimatedFrameSizes,
                estimatedFrames,
                probeSw.ElapsedMilliseconds);
        }

        var anyScaleChanged = false;
        foreach (var view in currentViews)
        {
            var targetScale = ResolveTargetScale(
                view,
                workspace.GetSemanticKind(view.GetIdentifier().ID),
                candidateScale,
                uniformAllNonDetail,
                workspace.OriginalScalesById);
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
            probeSw.ElapsedMilliseconds);
    }

    private static IReadOnlyDictionary<int, (double Width, double Height)> EstimateCandidateFrameSizes(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        IReadOnlyDictionary<int, (double Width, double Height)> originalFrameSizes,
        double candidateScale,
        bool uniformAllNonDetail)
    {
        var result = new Dictionary<int, (double Width, double Height)>(views.Count);
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
                workspace.OriginalScalesById);
            var frame = originalFrameSizes.TryGetValue(id, out var storedFrame)
                ? storedFrame
                : (view.Width, view.Height);
            var factor = targetScale > 0 ? originalScale / targetScale : 1.0;
            result[id] = (frame.Width * factor, frame.Height * factor);
        }

        return result;
    }

    private static IReadOnlyDictionary<int, double> ResolveSelectedScales(
        DrawingLayoutWorkspace workspace,
        IReadOnlyList<View> views,
        double selectedScale,
        bool uniformAllNonDetail,
        bool preserveCurrentScales)
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
                workspace.OriginalScalesById);
        }

        return result;
    }

}
