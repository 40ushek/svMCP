using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>
/// Where one view sits relative to its host: the view that carries its section
/// mark, or the base view. A stack is always relative to the base view.
/// </summary>
internal readonly struct ViewSlot
{
    public ViewSlot(SectionPlacementSide side, bool isStack, bool isAtCut = false)
    {
        Side = side;
        IsStack = isStack;
        IsAtCut = isAtCut;
    }

    public SectionPlacementSide Side { get; }

    /// <summary>
    /// False: in line with the base view on that side, sharing its center line.
    /// True: in a stack beyond the in-line views of that side, not aligned with the base view.
    /// </summary>
    public bool IsStack { get; }

    /// <summary>
    /// True: a section beside the base view on that side, level with its own section
    /// line, so the reader finds it next to the place where it is cut. It is off the
    /// side its arrows point to, and not on the base view's center line.
    /// </summary>
    public bool IsAtCut { get; }

    public override string ToString() => IsAtCut ? $"cut-{Side}" : IsStack ? $"stack-{Side}" : $"inline-{Side}";
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
    /// Where this view's frame center sits relative to the base view's frame center
    /// when the two are projection-aligned: X for a view above or below the base
    /// view, Y for a view beside it. Zero aligns the frame centers.
    /// </summary>
    public double AlignmentOffsetX { get; set; }

    /// <inheritdoc cref="AlignmentOffsetX"/>
    public double AlignmentOffsetY { get; set; }

    /// <summary>
    /// Position of the view's section line on the base view. Ascending is reading
    /// order: left to right in a row, top to bottom in a column.
    /// </summary>
    public double OrderKey { get; set; }

    /// <summary>
    /// The view this one is placed against: the one that carries its section mark,
    /// when that is another view of the plan. Null: the base view.
    /// </summary>
    public int? OwnerId { get; set; }

    /// <summary>
    /// The middle of the view's section line, measured from the frame center of the
    /// view that carries the mark. Null when the view has no section line there; it
    /// then has no slot at the cut and pays nothing for distance.
    /// </summary>
    public double? CutX { get; set; }

    /// <inheritdoc cref="CutX"/>
    public double? CutY { get; set; }
}

internal sealed class ConstraintLayoutPlannerInput
{
    /// <summary>Usable sheet area, margins already taken off.</summary>
    public ReservedRect Bounds { get; set; } = new(0, 0, 0, 0);

    public double Gap { get; set; }

    public IReadOnlyList<ReservedRect> ReservedAreas { get; set; } = Array.Empty<ReservedRect>();

    public ConstraintLayoutView BaseView { get; set; }

    public IReadOnlyList<ConstraintLayoutViewInput> Views { get; set; } = Array.Empty<ConstraintLayoutViewInput>();

    public ConstraintLayoutPolicy Policy { get; set; } = ConstraintLayoutPolicy.Balanced;

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

    /// <summary>What those deviations cost under the policy, in scale steps.</summary>
    public double Cost { get; set; }

    /// <summary>
    /// What the sections cost for standing away from their section lines. Candidates
    /// are ordered by this and <see cref="Cost"/> together.
    /// </summary>
    public double CutDistanceCost { get; set; }

    public double RankCost => Cost + CutDistanceCost;
}

internal sealed class ConstraintLayoutPlan
{
    /// <summary>Layouts that fit, cheapest under the policy first.</summary>
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

        var options = input.Views.Select(view => GetSlotOptions(view, input.Policy)).ToList();
        var plan = new ConstraintLayoutPlan();

        // Take the combinations with the fewest deviations, up to the limit, then try
        // the cheapest under the policy first. The sort is stable, so the all-preferred
        // combination stays in front.
        var choices = EnumerateChoices(options.Select(static o => o.Count).ToArray())
            .Take(input.MaxTopologies + 1)
            .ToList();
        plan.Truncated = choices.Count > input.MaxTopologies;
        var ranked = choices
            .Take(input.MaxTopologies)
            .Select(choice => (choice, cost: choice.Select((option, i) => options[i][option].Cost).Sum()))
            .OrderBy(static item => item.cost);

        foreach (var (choice, cost) in ranked)
        {
            plan.EvaluatedTopologies++;
            var slots = new Dictionary<int, ViewSlot>(input.Views.Count);
            for (var i = 0; i < input.Views.Count; i++)
                slots[input.Views[i].Id] = options[i][choice[i]].Slot;

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
                Deviations = choice.Count(static c => c != 0),
                Cost = cost,
                CutDistanceCost = GetCutDistanceCost(input, rects)
            });
            if (plan.Candidates.Count >= input.MaxCandidates)
                break;
        }

        // The distance prices are known only once a layout is solved; put the
        // candidates back in order of price, equal ones as they came.
        var byCost = plan.Candidates.OrderBy(static candidate => candidate.RankCost).ToList();
        plan.Candidates.Clear();
        plan.Candidates.AddRange(byCost);
        return plan;
    }

    /// <summary>What the sections cost for standing away from the middles of their section lines.</summary>
    private static double GetCutDistanceCost(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ReservedRect> rects)
    {
        var distance = 0.0;
        foreach (var view in input.Views)
        {
            if (view.CutX is not { } cutX || view.CutY is not { } cutY)
                continue;

            var host = rects[view.OwnerId ?? input.BaseView.Id];
            var x = ((host.MinX + host.MaxX) / 2.0) + cutX;
            var y = ((host.MinY + host.MaxY) / 2.0) + cutY;
            var rect = rects[view.Id];
            var dx = Math.Max(Math.Max(rect.MinX - x, x - rect.MaxX), 0.0);
            var dy = Math.Max(Math.Max(rect.MinY - y, y - rect.MaxY), 0.0);
            distance += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return distance / 100.0 * input.Policy.CutDistanceCost;
    }

    /// <summary>
    /// Slots a view may take with what each costs under the policy; the first one is
    /// its preferred slot and costs nothing. A slot the policy prices at infinity is
    /// left out.
    /// </summary>
    private static IReadOnlyList<(ViewSlot Slot, double Cost)> GetSlotOptions(
        ConstraintLayoutViewInput view,
        ConstraintLayoutPolicy policy)
    {
        // A view with no side or no projection link has nothing to lose in a stack.
        if (view.PreferredSide == SectionPlacementSide.Unknown
            || view.ProjectionStrength is ProjectionStrength.Off or ProjectionStrength.Weak)
        {
            return StackSlots.Select(static slot => (slot, 0.0)).ToList();
        }

        var options = new List<(ViewSlot, double)> { (new ViewSlot(view.PreferredSide, isStack: false), 0.0) };
        if (view.ProjectionStrength == ProjectionStrength.Strong)
            return options;

        if (view.CutX != null && view.CutY != null && !double.IsPositiveInfinity(policy.BesideCutCost))
        {
            options.AddRange(Sides
                .Where(side => IsHorizontal(side) != IsHorizontal(view.PreferredSide))
                .Select(side => (new ViewSlot(side, isStack: false, isAtCut: true), policy.BesideCutCost)));
        }

        if (!double.IsPositiveInfinity(policy.SideDeviationCost))
            options.Add((new ViewSlot(Opposite(view.PreferredSide), isStack: false), policy.SideDeviationCost));
        if (!double.IsPositiveInfinity(policy.LinkBreakCost))
            options.AddRange(StackSlots.Select(slot => (slot, policy.LinkBreakCost)));

        return options;
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
        // each pass separates one pair the last solution left touching.
        var solution = Solve(input, views, relations);
        for (var pass = 0; pass < views.Count * views.Count; pass++)
        {
            if (solution.UnseparatedPairs.Count == 0)
                break;

            var (firstId, secondId) = solution.UnseparatedPairs[0];
            var separated = SeparateAdjacentSides(input, slots, solution.RectsById, firstId, secondId)
                .Select(relation => Solve(input, views, relations.Append(relation).ToList()))
                .Zip(
                    SeparateAdjacentSides(input, slots, solution.RectsById, firstId, secondId),
                    static (attempt, relation) => (attempt, relation))
                .FirstOrDefault(static item =>
                    item.attempt.HorizontalFailure == AxisFailure.None
                    && item.attempt.VerticalFailure == AxisFailure.None);
            if (separated.attempt == null)
                return solution;

            relations.Add(separated.relation);
            solution = separated.attempt;
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
        var hostIds = new[] { baseId }.Concat(input.Views.Select(v => v.OwnerId ?? baseId)).Distinct();
        foreach (var hostId in hostIds)
        foreach (var side in Sides)
        {
            // A stack is off every axis, so it gathers around the base view whatever
            // view carries the section mark.
            var onSide = input.Views
                .Where(v => slots[v.Id].Side == side
                    && (slots[v.Id].IsStack ? baseId : v.OwnerId ?? baseId) == hostId)
                .ToList();
            AddSideRelations(input, slots, relations, hostId, side, onSide);
        }

        return relations;
    }

    /// <summary>The relations of the views that stand on one side of one host view.</summary>
    private static void AddSideRelations(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ViewSlot> slots,
        List<LayoutRelation> relations,
        int hostId,
        SectionPlacementSide side,
        IReadOnlyList<ConstraintLayoutViewInput> onSide)
    {
        var horizontal = IsHorizontal(side);
        var ascendingOutward = side is SectionPlacementSide.Right or SectionPlacementSide.Bottom;

        // In line: a chain running outward from the host view, all on its center line.
        var outermost = hostId;
        var inline = onSide
            .Where(v => !slots[v.Id].IsStack && !slots[v.Id].IsAtCut)
            .OrderByDescending(static v => v.IsProjection);
        inline = ascendingOutward
            ? inline.ThenBy(static v => v.OrderKey).ThenBy(static v => v.Id)
            : inline.ThenByDescending(static v => v.OrderKey).ThenByDescending(static v => v.Id);
        foreach (var view in inline)
        {
            relations.Add(Outward(side, outermost, view.Id));
            relations.Add(horizontal
                ? new LayoutRelation(LayoutRelationKind.AlignCenterY, hostId, view.Id, view.AlignmentOffsetY)
                : new LayoutRelation(LayoutRelationKind.AlignCenterX, hostId, view.Id, view.AlignmentOffsetX));
            outermost = view.Id;
        }

        // At the cut: beyond the in-line chain, each view level with its own section
        // line. Views whose lines are far enough apart share one column.
        var outer = new List<int> { outermost };
        foreach (var column in ArrangeAtCutColumns(onSide.Where(v => slots[v.Id].IsAtCut), horizontal, input.Gap))
        {
            foreach (var view in column)
            {
                relations.AddRange(outer.Select(inner => Outward(side, inner, view.Id)));
                relations.Add(horizontal
                    ? new LayoutRelation(LayoutRelationKind.AlignCenterY, hostId, view.Id, view.CutY ?? 0.0)
                    : new LayoutRelation(LayoutRelationKind.AlignCenterX, hostId, view.Id, view.CutX ?? 0.0));
            }

            outer = column.Select(static v => v.Id).ToList();
        }

        // Stack: beyond all of those, running across the side in reading order.
        var stack = onSide
            .Where(v => slots[v.Id].IsStack)
            .OrderBy(static v => v.OrderKey)
            .ThenBy(static v => v.Id)
            .ToList();
        for (var i = 0; i < stack.Count; i++)
        {
            relations.AddRange(outer.Select(inner => Outward(side, inner, stack[i].Id)));
            if (i == 0)
                continue;

            relations.Add(horizontal
                ? new LayoutRelation(LayoutRelationKind.Below, stack[i].Id, stack[i - 1].Id)
                : new LayoutRelation(LayoutRelationKind.LeftOf, stack[i - 1].Id, stack[i].Id));
            relations.Add(new LayoutRelation(
                horizontal ? LayoutRelationKind.AlignCenterX : LayoutRelationKind.AlignCenterY, stack[0].Id, stack[i].Id));
        }
    }

    /// <summary>
    /// Groups the views placed at their cuts on one side into columns running outward
    /// from the host view (rows, on a side above or below it). A view joins the first
    /// column where it stays a gap clear of the views already there.
    /// </summary>
    private static List<List<ConstraintLayoutViewInput>> ArrangeAtCutColumns(
        IEnumerable<ConstraintLayoutViewInput> views,
        bool horizontal,
        double gap)
    {
        double Along(ConstraintLayoutViewInput view) => (horizontal ? view.CutY : view.CutX) ?? 0.0;

        bool AreClear(ConstraintLayoutViewInput first, ConstraintLayoutViewInput second)
        {
            var extent = horizontal ? first.Height + second.Height : first.Width + second.Width;
            return Math.Abs(Along(first) - Along(second)) >= (extent / 2.0) + gap;
        }

        var columns = new List<List<ConstraintLayoutViewInput>>();
        foreach (var view in views.OrderBy(Along).ThenBy(static v => v.Id))
        {
            var column = columns.FirstOrDefault(placed => placed.All(other => AreClear(other, view)));
            if (column == null)
                columns.Add(column = new List<ConstraintLayoutViewInput>());

            column.Add(view);
        }

        return columns;
    }

    /// <summary>
    /// The two ways to separate a view on a horizontal side from one on a vertical
    /// side, the one that needs the smaller move first. The other is there because
    /// the first can contradict the rest: the two views may stand against different
    /// host views. Empty when the pair is not such a pair.
    /// </summary>
    private static LayoutRelation[] SeparateAdjacentSides(
        ConstraintLayoutPlannerInput input,
        IReadOnlyDictionary<int, ViewSlot> slots,
        IReadOnlyDictionary<int, ReservedRect> rects,
        int firstId,
        int secondId)
    {
        if (!slots.TryGetValue(firstId, out var first) || !slots.TryGetValue(secondId, out var second))
            return [];
        if (IsHorizontal(first.Side) == IsHorizontal(second.Side))
            return [];

        var sideId = IsHorizontal(first.Side) ? firstId : secondId;
        var verticalId = IsHorizontal(first.Side) ? secondId : firstId;
        var sideRect = rects[sideId];
        var verticalRect = rects[verticalId];
        var clearX = Math.Max(sideRect.MinX - verticalRect.MaxX, verticalRect.MinX - sideRect.MaxX);
        var clearY = Math.Max(sideRect.MinY - verticalRect.MaxY, verticalRect.MinY - sideRect.MaxY);
        var alongX = Outward(slots[sideId].Side, verticalId, sideId);
        var alongY = Outward(slots[verticalId].Side, sideId, verticalId);

        return input.Gap - clearX <= input.Gap - clearY ? [alongX, alongY] : [alongY, alongX];
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
