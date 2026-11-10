using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>Where one view sits relative to the base view.</summary>
internal readonly struct ViewSlot
{
    public ViewSlot(SectionPlacementSide side, bool isStack)
    {
        Side = side;
        IsStack = isStack;
    }

    public SectionPlacementSide Side { get; }

    /// <summary>
    /// False: in line with the base view on that side, sharing its center line.
    /// True: in a stack beyond the in-line views of that side, not aligned with the base view.
    /// </summary>
    public bool IsStack { get; }

    public override string ToString() => IsStack ? $"stack-{Side}" : $"inline-{Side}";
}

internal sealed class ConstraintLayoutViewInput
{
    public int Id { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public SectionPlacementSide PreferredSide { get; set; }

    public ProjectionStrength ProjectionStrength { get; set; }

    /// <summary>A projection of the base view (top, bottom, side view); sits closer to it than sections do.</summary>
    public bool IsProjection { get; set; }

    /// <summary>
    /// Position of the view's section line on the base view. Ascending is reading
    /// order: left to right in a row, top to bottom in a column.
    /// </summary>
    public double OrderKey { get; set; }
}

internal sealed class ConstraintLayoutPlannerInput
{
    /// <summary>Usable sheet area, margins already taken off.</summary>
    public ReservedRect Bounds { get; set; } = new(0, 0, 0, 0);

    public double Gap { get; set; }

    public IReadOnlyList<ReservedRect> ReservedAreas { get; set; } = Array.Empty<ReservedRect>();

    public ConstraintLayoutView BaseView { get; set; }

    public IReadOnlyList<ConstraintLayoutViewInput> Views { get; set; } = Array.Empty<ConstraintLayoutViewInput>();

    public int MaxTopologies { get; set; } = 2000;

    public int MaxCandidates { get; set; } = 16;
}

internal sealed class ConstraintLayoutPlanCandidate
{
    public IReadOnlyDictionary<int, ViewSlot> Slots { get; set; } = new Dictionary<int, ViewSlot>();

    /// <summary>Frame rectangles in sheet coordinates, base view included.</summary>
    public IReadOnlyDictionary<int, ReservedRect> RectsById { get; set; } = new Dictionary<int, ReservedRect>();

    /// <summary>How many views are not in their preferred slot.</summary>
    public int Deviations { get; set; }
}

internal sealed class ConstraintLayoutPlan
{
    /// <summary>Layouts that fit, fewest deviations first.</summary>
    public List<ConstraintLayoutPlanCandidate> Candidates { get; } = new();

    public int EvaluatedTopologies { get; set; }

    /// <summary>True when <see cref="ConstraintLayoutPlannerInput.MaxTopologies"/> stopped the search early.</summary>
    public bool Truncated { get; set; }

    /// <summary>Sheet width the all-preferred topology needs.</summary>
    public double PreferredRequiredWidth { get; set; }

    /// <summary>Sheet height the all-preferred topology needs.</summary>
    public double PreferredRequiredHeight { get; set; }
}

/// <summary>
/// Enumerates where each view may sit around the base view and solves every
/// combination, starting from the one that keeps all preferred sides.
/// </summary>
internal static class ConstraintLayoutPlanner
{
    private static readonly SectionPlacementSide[] Sides =
    [
        SectionPlacementSide.Left,
        SectionPlacementSide.Right,
        SectionPlacementSide.Top,
        SectionPlacementSide.Bottom
    ];

    private static readonly ViewSlot[] StackSlots =
    [
        new(SectionPlacementSide.Left, isStack: true),
        new(SectionPlacementSide.Right, isStack: true),
        new(SectionPlacementSide.Bottom, isStack: true)
    ];

    public static ConstraintLayoutPlan Plan(ConstraintLayoutPlannerInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var options = input.Views.Select(GetSlotOptions).ToList();
        var plan = new ConstraintLayoutPlan();
        foreach (var choice in EnumerateChoices(options.Select(static o => o.Count).ToArray()))
        {
            if (plan.EvaluatedTopologies >= input.MaxTopologies)
            {
                plan.Truncated = true;
                break;
            }

            plan.EvaluatedTopologies++;
            var slots = new Dictionary<int, ViewSlot>(input.Views.Count);
            for (var i = 0; i < input.Views.Count; i++)
                slots[input.Views[i].Id] = options[i][choice[i]];

            var solution = SolveTopology(input, slots);
            if (plan.EvaluatedTopologies == 1)
            {
                plan.PreferredRequiredWidth = solution.RequiredWidth;
                plan.PreferredRequiredHeight = solution.RequiredHeight;
            }

            var rects = solution.IsFeasible ? AvoidReservedAreas(input, solution.RectsById) : null;
            if (rects == null)
                continue;

            plan.Candidates.Add(new ConstraintLayoutPlanCandidate
            {
                Slots = slots,
                RectsById = rects,
                Deviations = choice.Count(static c => c != 0)
            });
            if (plan.Candidates.Count >= input.MaxCandidates)
                break;
        }

        return plan;
    }

    /// <summary>Slots a view may take; the first one is its preferred slot.</summary>
    private static IReadOnlyList<ViewSlot> GetSlotOptions(ConstraintLayoutViewInput view)
    {
        if (view.PreferredSide == SectionPlacementSide.Unknown
            || view.ProjectionStrength is ProjectionStrength.Off or ProjectionStrength.Weak)
        {
            return StackSlots;
        }

        var preferred = new ViewSlot(view.PreferredSide, isStack: false);
        if (view.ProjectionStrength == ProjectionStrength.Strong)
            return [preferred];

        return [preferred, new ViewSlot(Opposite(view.PreferredSide), isStack: false), .. StackSlots];
    }

    /// <summary>Every combination of option indices, ordered by how many are not the first option.</summary>
    private static IEnumerable<int[]> EnumerateChoices(int[] optionCounts)
    {
        var choice = new int[optionCounts.Length];
        for (var deviations = 0; deviations <= optionCounts.Length; deviations++)
        {
            foreach (var combination in WithDeviations(optionCounts, 0, deviations, choice))
                yield return combination;
        }
    }

    private static IEnumerable<int[]> WithDeviations(int[] optionCounts, int index, int remaining, int[] choice)
    {
        if (index == optionCounts.Length)
        {
            if (remaining == 0)
                yield return (int[])choice.Clone();
            yield break;
        }

        choice[index] = 0;
        foreach (var combination in WithDeviations(optionCounts, index + 1, remaining, choice))
            yield return combination;

        if (remaining == 0)
            yield break;

        for (var option = 1; option < optionCounts[index]; option++)
        {
            choice[index] = option;
            foreach (var combination in WithDeviations(optionCounts, index + 1, remaining - 1, choice))
                yield return combination;
        }
    }

    private static ConstraintLayoutSolution SolveTopology(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ViewSlot> slots)
    {
        var views = new List<ConstraintLayoutView> { input.BaseView };
        views.AddRange(input.Views.Select(static v => new ConstraintLayoutView(v.Id, v.Width, v.Height)));
        var relations = BuildRelations(input, slots);

        // The slots separate views of one side and of opposite sides. Views on adjacent
        // sides (a wide top view and a tall right section) can still meet at the corner;
        // each pass separates the pairs the last solution left touching.
        var solution = Solve(input, views, relations);
        for (var pass = 0; pass < views.Count * views.Count; pass++)
        {
            if (solution.UnseparatedPairs.Count == 0)
                break;

            foreach (var (firstId, secondId) in solution.UnseparatedPairs)
            {
                var relation = TrySeparateAdjacentSides(input, slots, solution.RectsById, firstId, secondId);
                if (relation == null)
                    return solution;

                relations.Add(relation.Value);
            }

            solution = Solve(input, views, relations);
        }

        return solution;
    }

    private static ConstraintLayoutSolution Solve(
        ConstraintLayoutPlannerInput input,
        IReadOnlyList<ConstraintLayoutView> views,
        IReadOnlyList<LayoutRelation> relations)
        => ConstraintLayoutSolver.Solve(
            new ConstraintLayoutProblem(input.Bounds, input.Gap, views, relations, input.BaseView.Id));

    private static List<LayoutRelation> BuildRelations(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ViewSlot> slots)
    {
        var baseId = input.BaseView.Id;
        var relations = new List<LayoutRelation>();
        foreach (var side in Sides)
        {
            var horizontal = IsHorizontal(side);
            var ascendingOutward = side is SectionPlacementSide.Right or SectionPlacementSide.Bottom;
            var onSide = input.Views.Where(v => slots[v.Id].Side == side).ToList();

            // In line: a chain running outward from the base view, all on its center line.
            var outermost = baseId;
            var inline = onSide.Where(v => !slots[v.Id].IsStack).OrderByDescending(static v => v.IsProjection);
            inline = ascendingOutward
                ? inline.ThenBy(static v => v.OrderKey).ThenBy(static v => v.Id)
                : inline.ThenByDescending(static v => v.OrderKey).ThenByDescending(static v => v.Id);
            foreach (var view in inline)
            {
                relations.Add(Outward(side, outermost, view.Id));
                relations.Add(new LayoutRelation(
                    horizontal ? LayoutRelationKind.AlignCenterY : LayoutRelationKind.AlignCenterX, baseId, view.Id));
                outermost = view.Id;
            }

            // Stack: beyond the in-line chain, running across it in reading order.
            var stack = onSide
                .Where(v => slots[v.Id].IsStack)
                .OrderBy(static v => v.OrderKey)
                .ThenBy(static v => v.Id)
                .ToList();
            for (var i = 0; i < stack.Count; i++)
            {
                relations.Add(Outward(side, outermost, stack[i].Id));
                if (i == 0)
                    continue;

                relations.Add(horizontal
                    ? new LayoutRelation(LayoutRelationKind.Below, stack[i].Id, stack[i - 1].Id)
                    : new LayoutRelation(LayoutRelationKind.LeftOf, stack[i - 1].Id, stack[i].Id));
                relations.Add(new LayoutRelation(
                    horizontal ? LayoutRelationKind.AlignCenterX : LayoutRelationKind.AlignCenterY, stack[0].Id, stack[i].Id));
            }
        }

        return relations;
    }

    /// <summary>
    /// Separates a view on a horizontal side from one on a vertical side along the
    /// axis that needs the smaller move. Null when the pair is not such a pair.
    /// </summary>
    private static LayoutRelation? TrySeparateAdjacentSides(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ViewSlot> slots,
        IReadOnlyDictionary<int, ReservedRect> rects,
        int firstId,
        int secondId)
    {
        if (!slots.TryGetValue(firstId, out var first) || !slots.TryGetValue(secondId, out var second))
            return null;
        if (IsHorizontal(first.Side) == IsHorizontal(second.Side))
            return null;

        var sideId = IsHorizontal(first.Side) ? firstId : secondId;
        var verticalId = IsHorizontal(first.Side) ? secondId : firstId;
        var sideRect = rects[sideId];
        var verticalRect = rects[verticalId];
        var clearX = Math.Max(sideRect.MinX - verticalRect.MaxX, verticalRect.MinX - sideRect.MaxX);
        var clearY = Math.Max(sideRect.MinY - verticalRect.MaxY, verticalRect.MinY - sideRect.MaxY);

        return input.Gap - clearX <= input.Gap - clearY
            ? Outward(slots[sideId].Side, verticalId, sideId)
            : Outward(slots[verticalId].Side, sideId, verticalId);
    }

    /// <summary>
    /// Views are solved as one rigid block centered on the sheet. When that position
    /// touches a reserved area, tries the block pushed to each edge and corner.
    /// Null when no position is clear.
    /// </summary>
    private static IReadOnlyDictionary<int, ReservedRect>? AvoidReservedAreas(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ReservedRect> rects)
    {
        if (input.ReservedAreas.Count == 0)
            return rects;

        var blocked = input.ReservedAreas
            .Select(area => new ReservedRect(
                area.MinX - input.Gap, area.MinY - input.Gap, area.MaxX + input.Gap, area.MaxY + input.Gap))
            .ToList();
        double[] shiftsX = [0.0, input.Bounds.MinX - rects.Values.Min(static r => r.MinX), input.Bounds.MaxX - rects.Values.Max(static r => r.MaxX)];
        double[] shiftsY = [0.0, input.Bounds.MinY - rects.Values.Min(static r => r.MinY), input.Bounds.MaxY - rects.Values.Max(static r => r.MaxY)];
        var shifts = shiftsX
            .SelectMany(_ => shiftsY, static (dx, dy) => (dx, dy))
            .OrderBy(static shift => Math.Abs(shift.dx) + Math.Abs(shift.dy));

        foreach (var (dx, dy) in shifts)
        {
            var moved = rects.ToDictionary(
                static item => item.Key,
                item => new ReservedRect(
                    item.Value.MinX + dx, item.Value.MinY + dy, item.Value.MaxX + dx, item.Value.MaxY + dy));
            if (!moved.Values.Any(rect => ViewPlacementValidator.IntersectsAny(rect, blocked)))
                return moved;
        }

        return null;
    }

    /// <summary>The relation that puts <paramref name="outerId"/> further out on <paramref name="side"/> than <paramref name="innerId"/>.</summary>
    private static LayoutRelation Outward(SectionPlacementSide side, int innerId, int outerId)
        => side switch
        {
            SectionPlacementSide.Right => new LayoutRelation(LayoutRelationKind.LeftOf, innerId, outerId),
            SectionPlacementSide.Left => new LayoutRelation(LayoutRelationKind.LeftOf, outerId, innerId),
            SectionPlacementSide.Top => new LayoutRelation(LayoutRelationKind.Below, innerId, outerId),
            SectionPlacementSide.Bottom => new LayoutRelation(LayoutRelationKind.Below, outerId, innerId),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "A slot needs a known side.")
        };

    private static bool IsHorizontal(SectionPlacementSide side)
        => side is SectionPlacementSide.Left or SectionPlacementSide.Right;

    private static SectionPlacementSide Opposite(SectionPlacementSide side)
        => side switch
        {
            SectionPlacementSide.Left => SectionPlacementSide.Right,
            SectionPlacementSide.Right => SectionPlacementSide.Left,
            SectionPlacementSide.Top => SectionPlacementSide.Bottom,
            SectionPlacementSide.Bottom => SectionPlacementSide.Top,
            _ => side
        };
}
