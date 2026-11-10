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
}

/// <summary>
/// Runs the constraint planner next to the current layout path for comparison.
/// It reads an already built candidate and produces scores; nothing here can be
/// selected for apply.
/// </summary>
internal static class ConstraintLayoutShadow
{
    public const string CandidateName = "constraint-layout:shadow";

    /// <returns>Null when the base view is not among the case's views or has no frame rectangle.</returns>
    public static ConstraintLayoutShadowResult? Evaluate(ConstraintLayoutCase layoutCase)
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
            .Where(view => view.Id != baseViewId && view.LayoutRect != null && IsSkeletonView(view))
            .ToList();
        var included = new List<DrawingLayoutCandidateView> { baseView };
        included.AddRange(planned);

        var margin = Math.Max(reference.ReservedLayout.Margin, 0.0);
        var plan = ConstraintLayoutPlanner.Plan(new ConstraintLayoutPlannerInput
        {
            Bounds = new ReservedRect(margin, margin, reference.Sheet.Width - margin, reference.Sheet.Height - margin),
            Gap = layoutCase.Gap,
            ReservedAreas = reference.ReservedLayout.Areas,
            BaseView = new ConstraintLayoutView(baseViewId, baseView.LayoutRect.Width, baseView.LayoutRect.Height),
            Views = planned.Select(view => ToPlannerView(view, sectionLineAnchors)).ToList()
        });

        var result = new ConstraintLayoutShadowResult
        {
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

        var selection = new DrawingLayoutCandidateSelector().SelectBest(
            plan.Candidates.Select(candidate => ToCandidate(reference, included, candidate)));
        result.Best = selection.Selected;
        result.BestPlan = plan.Candidates[selection.Items.First(static item => item.IsSelected).Index];
        return result;
    }

    private static bool IsSkeletonView(DrawingLayoutCandidateView view)
        => string.Equals(view.LayoutViewKind, nameof(LayoutViewKind.MainProjected), StringComparison.OrdinalIgnoreCase)
            || string.Equals(view.LayoutViewKind, nameof(LayoutViewKind.StandardSection), StringComparison.OrdinalIgnoreCase);

    private static ConstraintLayoutViewInput ToPlannerView(
        DrawingLayoutCandidateView view,
        IReadOnlyDictionary<int, ConstraintLayoutCaseAnchor> sectionLineAnchors)
    {
        var side = ResolvePreferredSide(view);
        sectionLineAnchors.TryGetValue(view.Id, out var anchor);

        return new ConstraintLayoutViewInput
        {
            Id = view.Id,
            Width = view.LayoutRect!.Width,
            Height = view.LayoutRect.Height,
            PreferredSide = side,
            ProjectionStrength = Enum.TryParse<ProjectionStrength>(view.ProjectionStrength, ignoreCase: true, out var strength)
                ? strength
                : ProjectionStrength.Relaxed,
            IsProjection = string.Equals(view.LayoutViewKind, nameof(LayoutViewKind.MainProjected), StringComparison.OrdinalIgnoreCase),
            // Sections placed left or right are cut by vertical lines and differ in X;
            // sections placed above or below differ in Y, read top down.
            OrderKey = side is SectionPlacementSide.Top or SectionPlacementSide.Bottom
                ? -(anchor?.Y ?? 0.0)
                : anchor?.X ?? 0.0
        };
    }

    private static SectionPlacementSide ResolvePreferredSide(DrawingLayoutCandidateView view)
    {
        if (Enum.TryParse<SectionPlacementSide>(view.PreferredPlacementSide, ignoreCase: true, out var side)
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
