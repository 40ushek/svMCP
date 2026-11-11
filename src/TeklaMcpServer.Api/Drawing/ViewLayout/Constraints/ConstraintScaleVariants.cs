using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>One way to scale the drawing: a scale for the assembly views and one for the sections.</summary>
internal sealed class ConstraintScaleVariant
{
    public double MainScale { get; set; }

    public double SectionScale { get; set; }

    /// <summary>The scale of every assembly view and every section under this variant.</summary>
    public IReadOnlyDictionary<int, double> ScalesById { get; set; } = new Dictionary<int, double>();

    /// <summary>Fill ratio of the planner's layout at these scales.</summary>
    public double Fill { get; set; }

    /// <summary>What that layout's deviations cost under the policy.</summary>
    public double Cost { get; set; }

    /// <summary>Two variants with the same key scale every view alike.</summary>
    public string Key => string.Join(
        ",",
        ScalesById.OrderBy(static pair => pair.Key).Select(static pair =>
            pair.Key.ToString(CultureInfo.InvariantCulture) + ":" + pair.Value.ToString("0.###", CultureInfo.InvariantCulture)));
}

/// <summary>
/// The size of a view's frame at a scale it has not been drawn at. Only Tekla
/// knows it for sure: a frame holds marks and dimensions that do not shrink with
/// the scale. The size is "fixed part + model part over scale". With how much of
/// the model the view draws known, one measurement gives the fixed part; two
/// measurements give both parts without it. With neither, the size is taken as
/// proportional, which is too large when the view grows and too small when it
/// shrinks.
/// </summary>
internal sealed class ConstraintFrameSizeEstimator
{
    private readonly Dictionary<int, List<(double Scale, double Width, double Height)>> _measured = new();
    private readonly Dictionary<int, (double Width, double Height)> _modelExtents = new();

    /// <param name="width">Model millimeters the view draws along its X axis.</param>
    /// <param name="height">The same along its Y axis.</param>
    public void RecordModelExtent(int viewId, double width, double height)
    {
        if (width > 0 && height > 0)
            _modelExtents[viewId] = (width, height);
    }

    public void Record(int viewId, double scale, double width, double height)
    {
        if (scale <= 0 || width <= 0 || height <= 0)
            return;

        if (!_measured.TryGetValue(viewId, out var list))
            _measured[viewId] = list = new List<(double, double, double)>();

        list.RemoveAll(entry => Same(entry.Scale, scale));
        list.Add((scale, width, height));
    }

    public (double Width, double Height)? Estimate(int viewId, double scale)
    {
        if (scale <= 0 || !_measured.TryGetValue(viewId, out var list) || list.Count == 0)
            return null;

        var nearest = list.OrderBy(entry => Math.Abs((1.0 / entry.Scale) - (1.0 / scale))).ToList();
        var first = nearest[0];
        if (Same(first.Scale, scale))
            return (first.Width, first.Height);

        if (nearest.Count == 1)
        {
            return _modelExtents.TryGetValue(viewId, out var extent)
                ? (FromModelPart(extent.Width, first.Scale, first.Width, scale),
                    FromModelPart(extent.Height, first.Scale, first.Height, scale))
                : (first.Width * first.Scale / scale, first.Height * first.Scale / scale);
        }

        var second = nearest[1];
        return (
            Fit(first.Scale, first.Width, second.Scale, second.Width, scale),
            Fit(first.Scale, first.Height, second.Scale, second.Height, scale));
    }

    private static double FromModelPart(double modelPart, double measuredScale, double measuredSize, double scale)
    {
        // A model part larger than the whole frame is not what the frame holds
        // (a view cropped in a way the extent does not show); proportional then.
        var fixedPart = measuredSize - (modelPart / measuredScale);
        return fixedPart < 0
            ? measuredSize * measuredScale / scale
            : fixedPart + (modelPart / scale);
    }

    private static double Fit(double scale1, double size1, double scale2, double size2, double scale)
    {
        var modelPart = (size1 - size2) / ((1.0 / scale1) - (1.0 / scale2));
        var fixedPart = size1 - (modelPart / scale1);

        // A fit that says the view grows as the scale gets smaller, or has a
        // negative fixed part, comes from marks having been rearranged between the
        // two measurements; the proportional estimate is the safer one then.
        return modelPart <= 0 || fixedPart < 0
            ? size1 * scale1 / scale
            : fixedPart + (modelPart / scale);
    }

    private static bool Same(double left, double right)
        => Math.Abs(left - right) <= 1e-6 * Math.Max(1.0, Math.Abs(right));
}

/// <summary>
/// Lays the drawing out at several combinations of assembly scale and section
/// scale without touching Tekla, and puts the combinations in the order they are
/// worth trying on the drawing. The user's criteria (2026-11-11): everything fits
/// the sheet; then the fill ratio; then the smaller price of deviations.
/// </summary>
internal static class ConstraintScaleVariants
{
    public static List<ConstraintScaleVariant> Rank(
        ConstraintLayoutCase layoutCase,
        IReadOnlyCollection<int> assemblyViewIds,
        IReadOnlyCollection<int> sectionViewIds,
        ConstraintLayoutPolicy policy,
        ConstraintFrameSizeEstimator estimator)
    {
        var reference = layoutCase.Reference;
        var margin = Math.Max(reference.ReservedLayout.Margin, 0.0);
        var availableWidth = reference.Sheet.Width - (2 * margin);
        var availableHeight = reference.Sheet.Height - (2 * margin);
        var currentScales = reference.Views.Where(static view => view.Scale > 0).ToDictionary(static view => view.Id, static view => view.Scale);

        bool FitsAlone(IReadOnlyDictionary<int, double> scales)
            => scales.All(pair => estimator.Estimate(pair.Key, pair.Value) is not { } size
                || (size.Width <= availableWidth && size.Height <= availableHeight));

        var mainScales = policy.MainViewScales.OrderBy(static scale => scale).ToList();
        var sectionScales = policy.SectionViewScales.OrderBy(static scale => scale).ToList();
        var assembly = assemblyViewIds.Where(currentScales.ContainsKey).ToList();
        var sections = sectionViewIds.Where(currentScales.ContainsKey).ToList();

        var seen = new HashSet<string>();
        var candidates = new List<ConstraintScaleVariant>();
        foreach (var mainScale in mainScales
            .SkipWhile(scale => !FitsAlone(assembly.ToDictionary(static id => id, _ => scale)))
            .Take(policy.MainScaleDepth))
        {
            // A section is never drawn smaller than the assembly, and never smaller
            // than it was: a section the author already drew larger stays as it is.
            var sectionChoices = sections.Count == 0
                ? new List<double> { mainScale }
                : sectionScales.Where(scale => scale <= mainScale + 1e-9).Reverse().Take(policy.SectionScaleDepth).ToList();
            foreach (var sectionScale in sectionChoices)
            {
                var scales = assembly.ToDictionary(static id => id, _ => mainScale);
                foreach (var id in sections)
                    scales[id] = Math.Min(currentScales[id], sectionScale);

                var variant = new ConstraintScaleVariant { MainScale = mainScale, SectionScale = sectionScale, ScalesById = scales };
                if (seen.Add(variant.Key) && FitsAlone(scales))
                    candidates.Add(variant);
            }
        }

        // The combinations do not depend on each other and nothing here touches
        // Tekla, so they are laid out side by side; the order they came in is kept.
        bool LaysOut(ConstraintScaleVariant variant)
        {
            var result = ConstraintLayoutShadow.Evaluate(Rescale(layoutCase, variant.ScalesById, estimator), policy);
            if (result?.Best?.IsFeasible != true || result.SkippedViews > 0)
                return false;

            variant.Fill = result.Best.Score.Breakdown.FillRatioRaw;
            variant.Cost = result.BestPlan!.Cost;
            return true;
        }

        try
        {
            return Order(candidates.AsParallel().AsOrdered().Where(LaysOut).ToList(), policy.ScaleFillTie);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.Count > 0)
        {
            // The failure of one combination as it was, not wrapped by the parallel run.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerExceptions[0]).Throw();
            throw;
        }
    }

    /// <summary>
    /// Best first: the fullest sheet, except that among variants about as full as
    /// the fullest the one with the larger assembly comes first, and of those the
    /// one that gives up least.
    /// </summary>
    /// <param name="fillTie">Fill ratios closer than this count as the same (the policy's ScaleFillTie).</param>
    public static List<ConstraintScaleVariant> Order(IEnumerable<ConstraintScaleVariant> variants, double fillTie)
    {
        var left = variants.ToList();
        var ordered = new List<ConstraintScaleVariant>(left.Count);
        while (left.Count > 0)
        {
            var fullest = left.Max(static variant => variant.Fill);
            var next = left
                .Where(variant => variant.Fill >= fullest - fillTie)
                .OrderBy(static variant => variant.MainScale)
                .ThenBy(static variant => variant.Cost)
                .ThenByDescending(static variant => variant.Fill)
                .First();
            ordered.Add(next);
            left.Remove(next);
        }

        return ordered;
    }

    /// <summary>
    /// The case as it would be with these scales: every rescaled view keeps its
    /// frame center, takes the estimated frame size, and has what hangs on its
    /// scale -- origin, model anchor, the marks it carries -- moved to match.
    /// </summary>
    public static ConstraintLayoutCase Rescale(
        ConstraintLayoutCase source,
        IReadOnlyDictionary<int, double> scalesById,
        ConstraintFrameSizeEstimator estimator)
    {
        var result = ConstraintLayoutCaseStore.Clone(source);
        var rescaled = new Dictionary<int, DrawingLayoutCandidateView>();
        foreach (var view in result.Reference.Views)
        {
            if (view.LayoutRect == null
                || view.Scale <= 0
                || !scalesById.TryGetValue(view.Id, out var scale)
                || Math.Abs(scale - view.Scale) <= 1e-9
                || estimator.Estimate(view.Id, scale) is not { } size)
            {
                continue;
            }

            var factor = view.Scale / scale;
            var centerX = (view.LayoutRect.MinX + view.LayoutRect.MaxX) / 2.0;
            var centerY = (view.LayoutRect.MinY + view.LayoutRect.MaxY) / 2.0;
            view.OriginX = centerX + ((view.OriginX - centerX) * factor);
            view.OriginY = centerY + ((view.OriginY - centerY) * factor);
            view.LayoutRect = new ReservedRect(
                centerX - (size.Width / 2.0),
                centerY - (size.Height / 2.0),
                centerX + (size.Width / 2.0),
                centerY + (size.Height / 2.0));
            view.BBoxMinX = view.LayoutRect.MinX;
            view.BBoxMinY = view.LayoutRect.MinY;
            view.BBoxMaxX = view.LayoutRect.MaxX;
            view.BBoxMaxY = view.LayoutRect.MaxY;
            view.Width *= factor;
            view.Height *= factor;
            view.Scale = scale;

            foreach (var anchor in result.ModelAnchors.Where(anchor => anchor.ViewId == view.Id))
            {
                anchor.X *= factor;
                anchor.Y *= factor;
            }

            rescaled[view.Id] = view;
        }

        foreach (var anchor in result.SectionLineAnchors.Concat(result.DetailMarkAnchors))
        {
            if (anchor.OwnerViewId is { } ownerId
                && rescaled.TryGetValue(ownerId, out var owner)
                && anchor.OwnerLocalX is { } localX
                && anchor.OwnerLocalY is { } localY)
            {
                anchor.X = owner.OriginX + (localX / owner.Scale);
                anchor.Y = owner.OriginY + (localY / owner.Scale);
            }
        }

        return result;
    }
}
