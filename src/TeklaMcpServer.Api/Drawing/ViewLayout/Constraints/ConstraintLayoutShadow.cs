using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

internal sealed class ConstraintLayoutShadowResult
{
    public ConstraintLayoutPlan Plan { get; set; } = new();

    /// <summary>Views the planner laid out: the base view, its projections and standard sections.</summary>
    public int IncludedViews { get; set; }

    /// <summary>Views left out: details, 3D, other, and views without a frame rectangle.</summary>
    public int SkippedViews { get; set; }

    /// <summary>The current layout, scored on the included views only so both sides compare like for like.</summary>
    public DrawingLayoutCandidateEvaluation Reference { get; set; } = new();

    /// <summary>The planner's best layout by the same scorer. Null when no topology fits.</summary>
    public DrawingLayoutCandidateEvaluation? Best { get; set; }

    public ConstraintLayoutPlanCandidate? BestPlan { get; set; }

    /// <summary>Views with a known side and a model anchor, compared with the current layout.</summary>
    public int AlignmentCheckedViews { get; set; }

    /// <summary>
    /// Of those, the views whose offset from the base view in the current layout
    /// equals the offset the model anchors predict. These are the views the current
    /// path projection-aligned, and each one confirms the anchors. The rest it left
    /// unaligned: placed freely, or centered on the base view's frame.
    /// </summary>
    public int AlignmentMatchedViews { get; set; }

    /// <summary>Largest gap between predicted and actual offset over the checked views.</summary>
    public double AlignmentResidualMax { get; set; }

    /// <summary>
    /// False when the current path ran no projection alignment on the reference
    /// (a dry run that leaves mixed scales, for one). The residual then compares the
    /// anchors with an unaligned layout and says nothing about them.
    /// </summary>
    public bool AlignmentCheckValid { get; set; }
}

/// <summary>
/// Runs the constraint planner next to the current layout path for comparison.
/// It reads an already built candidate and produces scores; nothing here can be
/// selected for apply.
/// </summary>
internal static class ConstraintLayoutShadow
{
    public const string CandidateName = "constraint-layout:planner";

    /// <summary>Sheet millimeters within which a predicted and an actual offset count as the same.</summary>
    private const double AlignmentMatchTolerance = 0.05;

    /// <returns>Null when the base view is not among the case's views or has no frame rectangle.</returns>
    /// <param name="variant">
    /// Which of the layouts that fit to return, counted from the cheapest: 0 is the
    /// planner's own choice, 1 the next one, and so on. A number past the last
    /// layout returns no layout.
    /// </param>
    public static ConstraintLayoutShadowResult? Evaluate(
        ConstraintLayoutCase layoutCase,
        ConstraintLayoutPolicy? policy = null,
        int variant = 0)
    {
        if (layoutCase == null)
            throw new ArgumentNullException(nameof(layoutCase));

        var reference = layoutCase.Reference;
        var baseViewId = layoutCase.BaseViewId;
        var sectionLineAnchors = layoutCase.SectionLineAnchors
            .GroupBy(static anchor => anchor.ViewId)
            .ToDictionary(static group => group.Key, static group => group.First());

        var baseView = reference.Views.FirstOrDefault(view => view.Id == baseViewId);
        if (baseView?.LayoutRect == null)
            return null;

        var planned = reference.Views
            .Where(view => view.Id != baseViewId && view.LayoutRect != null && IsPlannable(view, sectionLineAnchors))
            .ToList();
        var included = new List<DrawingLayoutCandidateView> { baseView };
        included.AddRange(planned);

        // A model anchor says where the model origin lands on the sheet, and the model
        // origin is usually far from the part. Two views line up through it only at
        // one scale; a view drawn larger than the base view (a section kept at 1:5
        // beside an assembly at 1:10) would be thrown off by the difference. Such a
        // view is aligned by frame centers instead, as the current path does.
        var baseScaleViewIds = included
            .Where(view => Math.Abs(view.Scale - baseView.Scale) <= 1e-6 * Math.Max(baseView.Scale, 1.0))
            .Select(static view => view.Id)
            .ToHashSet();
        var modelAnchors = layoutCase.ModelAnchors
            .Where(anchor => baseScaleViewIds.Contains(anchor.ViewId))
            .GroupBy(static anchor => anchor.ViewId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var viewSides = layoutCase.ViewSides
            .GroupBy(static side => side.ViewId)
            .ToDictionary(static group => group.Key, static group => group.First().Side);
        // A section is placed against the view that carries its mark, when the planner
        // lays that view out too; every other view against the base view.
        var hosts = planned.ToDictionary(
            static view => view.Id,
            view => sectionLineAnchors.TryGetValue(view.Id, out var anchor)
                ? included.FirstOrDefault(other => other.Id == anchor.OwnerViewId && other.Id != view.Id) ?? baseView
                : baseView);
        var plannerViews = planned
            .Select(view => ToPlannerView(view, sectionLineAnchors, modelAnchors, viewSides, hosts[view.Id], baseView.Id))
            .ToList();

        var margin = Math.Max(reference.ReservedLayout.Margin, 0.0);
        var plan = ConstraintLayoutPlanner.Plan(new ConstraintLayoutPlannerInput
        {
            Bounds = new ReservedRect(margin, margin, reference.Sheet.Width - margin, reference.Sheet.Height - margin),
            Gap = layoutCase.Gap,
            ReservedAreas = reference.ReservedLayout.Areas,
            BaseView = new ConstraintLayoutView(baseViewId, baseView.LayoutRect.Width, baseView.LayoutRect.Height),
            Views = plannerViews,
            Policy = policy ?? ConstraintLayoutPolicy.Balanced
        });

        var residuals = planned
            .Zip(plannerViews, static (view, input) => (view, input))
            .Where(pair => modelAnchors.ContainsKey(hosts[pair.view.Id].Id) && modelAnchors.ContainsKey(pair.view.Id))
            .Where(static pair => pair.input.PreferredSide != SectionPlacementSide.Unknown)
            .Select(pair => GetAlignmentResidual(hosts[pair.view.Id].LayoutRect!, pair.view.LayoutRect!, pair.input))
            .ToList();

        var result = new ConstraintLayoutShadowResult
        {
            AlignmentCheckValid = layoutCase.ReferenceProjectionMode is "assembly" or "ga" or "single-part",
            AlignmentCheckedViews = residuals.Count,
            AlignmentMatchedViews = residuals.Count(static residual => residual <= AlignmentMatchTolerance),
            AlignmentResidualMax = residuals.Count == 0 ? 0.0 : residuals.Max(),
            Plan = plan,
            IncludedViews = included.Count,
            SkippedViews = reference.Views.Count - included.Count,
            Reference = new DrawingLayoutScorer().Evaluate(new DrawingLayoutCandidate
            {
                Name = reference.Name,
                Source = reference.Source,
                Drawing = reference.Drawing,
                Sheet = reference.Sheet,
                ReservedLayout = reference.ReservedLayout,
                Views = included,
                StackOrderGroups = reference.StackOrderGroups
            })
        };
        if (plan.Candidates.Count == 0)
            return result;

        if (variant > 0)
        {
            if (variant >= plan.Candidates.Count)
                return result;

            result.BestPlan = plan.Candidates[variant];
            result.Best = new DrawingLayoutScorer().Evaluate(ToCandidate(reference, included, result.BestPlan));
            return result;
        }

        // The policy decides first: only the cheapest layouts compete on score.
        var cheapest = plan.Candidates
            .Where(candidate => candidate.RankCost <= plan.Candidates[0].RankCost + 1e-9)
            .ToList();
        var selection = new DrawingLayoutCandidateSelector().SelectBest(
            cheapest.Select(candidate => ToCandidate(reference, included, candidate)));
        result.Best = selection.Selected;
        result.BestPlan = cheapest[selection.Items.First(static item => item.IsSelected).Index];
        return result;
    }

    /// <summary>
    /// What the planner lays out: projections of the base view, and sections. A view
    /// is a section when another view carries its section mark, whatever Tekla type
    /// it has: an end view with section arrows is a section, not a projection.
    /// </summary>
    private static bool IsPlannable(
        DrawingLayoutCandidateView view,
        IReadOnlyDictionary<int, ConstraintLayoutCaseAnchor> sectionLineAnchors)
        => sectionLineAnchors.ContainsKey(view.Id)
            ? !IsKind(view, LayoutViewKind.Detail) && !IsKind(view, LayoutViewKind.Model3D) && !IsKind(view, LayoutViewKind.Other)
            : IsKind(view, LayoutViewKind.MainProjected) || IsKind(view, LayoutViewKind.StandardSection);

    private static bool IsKind(DrawingLayoutCandidateView view, LayoutViewKind kind)
        => string.Equals(view.LayoutViewKind, kind.ToString(), StringComparison.OrdinalIgnoreCase);

    private static ConstraintLayoutViewInput ToPlannerView(
        DrawingLayoutCandidateView view,
        IReadOnlyDictionary<int, ConstraintLayoutCaseAnchor> sectionLineAnchors,
        IReadOnlyDictionary<int, ConstraintLayoutCaseModelAnchor> modelAnchors,
        IReadOnlyDictionary<int, string> viewSides,
        DrawingLayoutCandidateView host,
        int baseViewId)
    {
        var side = ResolvePreferredSide(view, viewSides);
        var isSection = sectionLineAnchors.TryGetValue(view.Id, out var anchor)
            || IsKind(view, LayoutViewKind.StandardSection);
        var hasModelAnchors = modelAnchors.TryGetValue(view.Id, out var own)
            & modelAnchors.TryGetValue(host.Id, out var ofHost);
        var cut = GetCutPoint(anchor, host);

        return new ConstraintLayoutViewInput
        {
            Id = view.Id,
            OwnerId = host.Id == baseViewId ? null : host.Id,
            AlignmentOffsetX = hasModelAnchors ? own!.X - ofHost!.X : 0.0,
            AlignmentOffsetY = hasModelAnchors ? own!.Y - ofHost!.Y : 0.0,
            Width = view.LayoutRect!.Width,
            Height = view.LayoutRect.Height,
            PreferredSide = side,
            // A section may leave its side when the views do not fit; a projection
            // keeps the link the current path gave it, strong unless stated otherwise.
            ProjectionStrength = isSection
                ? ProjectionStrength.Relaxed
                : Enum.TryParse<ProjectionStrength>(view.ProjectionStrength, ignoreCase: true, out var strength)
                    ? strength
                    : ProjectionStrength.Strong,
            IsProjection = !isSection,
            // Sections placed left or right are cut by vertical lines and differ in X;
            // sections placed above or below differ in Y, read top down.
            OrderKey = side is SectionPlacementSide.Top or SectionPlacementSide.Bottom
                ? -(anchor?.Y ?? 0.0)
                : anchor?.X ?? 0.0,
            CutX = cut?.X,
            CutY = cut?.Y
        };
    }

    /// <summary>
    /// The middle of a section's line on the view that carries its mark, from that
    /// view's frame center. Null unless the mark is on the host view.
    /// </summary>
    private static (double X, double Y)? GetCutPoint(
        ConstraintLayoutCaseAnchor? anchor,
        DrawingLayoutCandidateView host)
    {
        if (anchor?.OwnerViewId != host.Id
            || anchor.OwnerLocalX is not { } localX
            || anchor.OwnerLocalY is not { } localY
            || host.Scale <= 0)
        {
            return null;
        }

        var frame = host.LayoutRect!;
        return (
            host.OriginX + (localX / host.Scale) - ((frame.MinX + frame.MaxX) / 2.0),
            host.OriginY + (localY / host.Scale) - ((frame.MinY + frame.MaxY) / 2.0));
    }

    private static double GetAlignmentResidual(
        ReservedRect baseRect,
        ReservedRect viewRect,
        ConstraintLayoutViewInput input)
        => input.PreferredSide is SectionPlacementSide.Top or SectionPlacementSide.Bottom
            ? Math.Abs((((viewRect.MinX + viewRect.MaxX) - (baseRect.MinX + baseRect.MaxX)) / 2.0) - input.AlignmentOffsetX)
            : Math.Abs((((viewRect.MinY + viewRect.MaxY) - (baseRect.MinY + baseRect.MaxY)) / 2.0) - input.AlignmentOffsetY);

    /// <summary>
    /// The side the case supplies, else the side the current path recorded on the
    /// view, else a guess from the view type. The case comes first so the planner
    /// follows its own side rules, not the current path's.
    /// </summary>
    private static SectionPlacementSide ResolvePreferredSide(
        DrawingLayoutCandidateView view,
        IReadOnlyDictionary<int, string> viewSides)
    {
        if (viewSides.TryGetValue(view.Id, out var supplied)
            && Enum.TryParse<SectionPlacementSide>(supplied, ignoreCase: true, out var side)
            && side != SectionPlacementSide.Unknown)
        {
            return side;
        }

        if (Enum.TryParse(view.PreferredPlacementSide, ignoreCase: true, out side)
            && side != SectionPlacementSide.Unknown)
        {
            return side;
        }

        return view.ViewType switch
        {
            "TopView" => SectionPlacementSide.Top,
            "BottomView" => SectionPlacementSide.Bottom,
            "EndView" => SectionPlacementSide.Right,
            _ => SectionPlacementSide.Unknown
        };
    }

    private static DrawingLayoutCandidate ToCandidate(
        DrawingLayoutCandidate reference,
        IReadOnlyList<DrawingLayoutCandidateView> included,
        ConstraintLayoutPlanCandidate candidate)
        => DrawingLayoutCandidateFactory.FromPlannedViews(
            CandidateName,
            reference.Drawing,
            reference.Sheet,
            reference.ReservedLayout,
            included.Select(view => ToPlannedView(view, candidate)).ToList());

    private static DrawingLayoutPlannedView ToPlannedView(
        DrawingLayoutCandidateView view,
        ConstraintLayoutPlanCandidate candidate)
    {
        var rect = candidate.RectsById[view.Id];
        var current = view.LayoutRect!;
        var hasSlot = candidate.Slots.TryGetValue(view.Id, out var slot);
        var actualSide = hasSlot ? slot.Side.ToString() : string.Empty;

        return new DrawingLayoutPlannedView
        {
            Id = view.Id,
            ViewType = view.ViewType,
            SemanticKind = view.SemanticKind,
            LayoutViewKind = view.LayoutViewKind,
            Name = view.Name,
            // The origin keeps its offset from the frame; only the frame moves.
            OriginX = view.OriginX + (((rect.MinX + rect.MaxX) - (current.MinX + current.MaxX)) / 2.0),
            OriginY = view.OriginY + (((rect.MinY + rect.MaxY) - (current.MinY + current.MaxY)) / 2.0),
            Scale = view.Scale,
            Width = rect.Width,
            Height = rect.Height,
            LayoutRect = rect,
            PreferredPlacementSide = view.PreferredPlacementSide,
            ActualPlacementSide = actualSide,
            PlacementFallbackUsed = hasSlot
                && (slot.IsStack
                    || (!string.IsNullOrWhiteSpace(view.PreferredPlacementSide)
                        && !string.Equals(view.PreferredPlacementSide, actualSide, StringComparison.OrdinalIgnoreCase))),
            ProjectionStrength = view.ProjectionStrength,
            ScaleFlexibility = view.ScaleFlexibility,
            ProjectionRole = view.ProjectionRole
        };
    }
}
