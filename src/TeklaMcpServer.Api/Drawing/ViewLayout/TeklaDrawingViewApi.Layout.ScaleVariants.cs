using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class TeklaDrawingViewApi
{
    /// <summary>How many scale combinations are tried on the drawing.</summary>
    private const int MaxScaleVariantAttempts = 4;

    /// <param name="margin">Margin from sheet edges in mm. Pass <c>null</c> to auto-read from drawing layout. Pass 0 for a true zero margin.</param>
    /// <param name="scalePolicy">Controls whether scales are unified, partially unified, or preserved as-is.</param>
    public FitViewsResult FitViewsToSheet(
        double? margin,
        double gap,
        double titleBlockHeight,
        DrawingScalePolicy scalePolicy = DrawingScalePolicy.UniformAllNonDetail,
        DrawingLayoutApplyMode applyMode = DrawingLayoutApplyMode.DebugPreview,
        SecondaryScalePolicy secondaryScalePolicy = SecondaryScalePolicy.SameAsMain,
        DrawingLayoutPlannerMode plannerMode = DrawingLayoutPlannerMode.Current,
        int plannerVariant = 0)
    {
        // The constraint planner picking the scales goes its own way; when it
        // cannot (no base view, a view larger than the sheet as it stands), the
        // scale loop of the core does the job as before.
        if (plannerMode != DrawingLayoutPlannerMode.Current
            && scalePolicy == DrawingScalePolicy.UniformAllNonDetail
            && plannerVariant == 0)
        {
            var planned = TryFitWithScaleVariants(margin, gap, titleBlockHeight, applyMode, plannerMode);
            if (planned != null)
                return planned;
        }

        return FitViewsToSheetCore(
            margin, gap, titleBlockHeight, scalePolicy, applyMode, secondaryScalePolicy, plannerMode, plannerVariant);
    }

    /// <summary>
    /// Two passes. Dry: the planner lays the drawing out at several combinations of
    /// assembly scale and section scale, with frame sizes estimated, and the
    /// combinations are ordered -- fits the sheet, then fill ratio, then the price
    /// of deviations. On the drawing: the best one is set in Tekla and laid out
    /// with the real frames. Every such try also measures the frames, so the
    /// estimates are corrected and the rest ordered again; the next one is tried
    /// when this one did not fit, or when the corrected figures promise a clearly
    /// fuller sheet than the one in hand. Null when the planner has nothing to
    /// offer here.
    /// </summary>
    private FitViewsResult? TryFitWithScaleVariants(
        double? margin,
        double gap,
        double titleBlockHeight,
        DrawingLayoutApplyMode applyMode,
        DrawingLayoutPlannerMode plannerMode)
    {
        var stopwatch = Stopwatch.StartNew();

        FitViewsResult? AtPresentScales(DrawingLayoutApplyMode mode)
        {
            try
            {
                return FitViewsToSheetCore(
                    margin,
                    gap,
                    titleBlockHeight,
                    DrawingScalePolicy.PreserveExistingScales,
                    mode,
                    SecondaryScalePolicy.SameAsMain,
                    plannerMode,
                    plannerVariant: 0);
            }
            catch (DrawingFitFailedException)
            {
                return null;
            }
        }

        var measured = AtPresentScales(DrawingLayoutApplyMode.DebugPreview);
        var layoutCase = measured?.PlannerCase;
        if (layoutCase == null)
            return null;

        // On a drawing of sections only, the one the layout is built around stands
        // in for the assembly: it takes the main scale, the others follow as sections.
        var assemblyViewIds = measured!.PlannerAssemblyViewIds.Count > 0
            ? measured.PlannerAssemblyViewIds
            : new[] { layoutCase.BaseViewId };
        var sectionViewIds = measured.PlannerSectionViewIds.Where(id => !assemblyViewIds.Contains(id)).ToList();

        var policy = ResolveConstraintLayoutPolicy(plannerMode);
        var estimator = new ConstraintFrameSizeEstimator();
        RecordFrameSizes(estimator, layoutCase);
        var originalScales = layoutCase.Reference.Views
            .Where(static view => view.Scale > 0)
            .ToDictionary(static view => view.Id, static view => view.Scale);

        List<ConstraintScaleVariant> Rank()
            => ConstraintScaleVariants.Rank(
                layoutCase, assemblyViewIds, sectionViewIds, policy, estimator);

        var variants = Rank();
        TraceScaleVariants("dry", variants, stopwatch.ElapsedMilliseconds);
        if (variants.Count == 0)
            return null;

        if (applyMode != DrawingLayoutApplyMode.FinalOnly)
        {
            // A dry run changes no scale, so it can only name the choice; the views
            // it reports stand as the planner would put them at the present scales.
            measured.OptimalScale = variants[0].MainScale;
            return measured;
        }

        var tried = new HashSet<string>();
        ConstraintScaleVariant? best = null;
        while (variants.Count > 0 && tried.Count < MaxScaleVariantAttempts)
        {
            var variant = variants[0];
            tried.Add(variant.Key);
            SetViewScales(variant.ScalesById);

            var probe = AtPresentScales(DrawingLayoutApplyMode.DebugPreview);
            if (probe?.PlannerCase != null)
                RecordFrameSizes(estimator, probe.PlannerCase);

            var fits = probe?.PlannerCost != null;
            var measuredVariant = fits
                ? new ConstraintScaleVariant
                {
                    MainScale = variant.MainScale,
                    SectionScale = variant.SectionScale,
                    ScalesById = variant.ScalesById,
                    Fill = probe!.PlannerFill,
                    Cost = probe.PlannerCost!.Value
                }
                : null;
            if (measuredVariant != null
                && (best == null || ConstraintScaleVariants.Order(new[] { best, measuredVariant })[0] == measuredVariant))
            {
                best = measuredVariant;
            }
            PerfTrace.Write(
                "api-view",
                "constraint_layout_scale_variant",
                stopwatch.ElapsedMilliseconds,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "attempt={0} main=1:{1:0.###} sections=1:{2:0.###} estimatedFill={3:0.###} estimatedCost={4:0.##} result={5} fill={6:0.###} cost={7}",
                    tried.Count,
                    variant.MainScale,
                    variant.SectionScale,
                    variant.Fill,
                    variant.Cost,
                    fits ? "fits" : "no-fit",
                    measuredVariant?.Fill ?? 0.0,
                    probe?.PlannerCost?.ToString("0.##", CultureInfo.InvariantCulture) ?? "none"));

            variants = Rank().Where(next => !tried.Contains(next.Key)).ToList();
            TraceScaleVariants("after-measure", variants, stopwatch.ElapsedMilliseconds);

            // With a layout in hand, another is worth a try on the drawing only when
            // the corrected estimate promises a clearly fuller sheet.
            if (best != null && (variants.Count == 0 || variants[0].Fill <= best.Fill + ConstraintScaleVariants.FillTie))
                break;
        }

        if (best != null)
        {
            SetViewScales(best.ScalesById);
            var applied = AtPresentScales(DrawingLayoutApplyMode.FinalOnly);
            if (applied != null)
            {
                applied.OptimalScale = best.MainScale;
                applied.ScalePreserved = false;
                applied.ScalePolicy = DrawingScalePolicy.UniformAllNonDetail.ToString();
                applied.TotalMs = stopwatch.ElapsedMilliseconds;
                return applied;
            }
        }

        // Nothing held on the drawing: the scales go back to what they were and the
        // views are laid out at those.
        SetViewScales(originalScales);
        PerfTrace.Write("api-view", "constraint_layout_scale_variant", stopwatch.ElapsedMilliseconds, "result=restored-original-scales");
        return AtPresentScales(DrawingLayoutApplyMode.FinalOnly);
    }

    private static void RecordFrameSizes(ConstraintFrameSizeEstimator estimator, ConstraintLayoutCase layoutCase)
    {
        foreach (var extent in layoutCase.ModelExtents)
            estimator.RecordModelExtent(extent.ViewId, extent.Width, extent.Height);

        foreach (var view in layoutCase.Reference.Views)
        {
            if (view.LayoutRect != null)
                estimator.Record(view.Id, view.Scale, view.LayoutRect.Width, view.LayoutRect.Height);
        }
    }

    private static void TraceScaleVariants(string pass, IReadOnlyList<ConstraintScaleVariant> variants, long elapsedMs)
        => PerfTrace.Write(
            "api-view",
            "constraint_layout_scale_variants",
            elapsedMs,
            string.Format(
                CultureInfo.InvariantCulture,
                "pass={0} count={1} order={2}",
                pass,
                variants.Count,
                string.Join(
                    " ",
                    variants.Select(static variant => string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.###}/{1:0.###}:fill={2:0.###}:cost={3:0.##}",
                        variant.MainScale,
                        variant.SectionScale,
                        variant.Fill,
                        variant.Cost)))));

    /// <summary>Sets the scales that differ from what the views have now, one commit per scale value.</summary>
    private void SetViewScales(IReadOnlyDictionary<int, double> scalesById)
    {
        var activeDrawing = new DrawingHandler().GetActiveDrawing();
        if (activeDrawing == null)
            throw new DrawingNotOpenException();

        var changes = EnumerateViews(activeDrawing)
            .Select(view => (Id: view.GetIdentifier().ID, Current: view.Attributes.Scale))
            .Where(view => scalesById.TryGetValue(view.Id, out var target) && System.Math.Abs(target - view.Current) > 1e-6)
            .GroupBy(view => scalesById[view.Id]);
        foreach (var group in changes.ToList())
            SetViewScale(group.Select(static view => view.Id), group.Key);
    }
}
