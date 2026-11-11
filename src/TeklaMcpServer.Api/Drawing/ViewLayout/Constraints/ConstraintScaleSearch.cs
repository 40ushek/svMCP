namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

internal enum ConstraintScaleDecision
{
    /// <summary>The planner has no layout to offer so far; the current planner decides.</summary>
    NoLayout,

    /// <summary>Take the planner's layout at this scale and stop.</summary>
    Select,

    /// <summary>Keep this scale's result in hand and try the next, smaller scale.</summary>
    LookAhead,

    /// <summary>A scale tried earlier was better; go back to it.</summary>
    GoBack
}

/// <summary>
/// Weighs scale against the policy's prices while fit_views_to_sheet walks its
/// scales from the largest down. A layout's total is the number of scale steps
/// taken so far, plus what its deviations cost, plus what it costs for leaving the
/// sheet empty, all in scale steps; the smallest total wins. So a layout that had
/// to break a projection link (1.5) loses to one a single step smaller that breaks
/// nothing (1.0) -- unless the smaller scale leaves the sheet so empty that the
/// step is no longer worth it.
/// </summary>
internal sealed class ConstraintScaleSearch
{
    private const double Epsilon = 1e-9;

    private double _bestTotal = double.PositiveInfinity;

    /// <summary>Attempt number of the best layout so far; -1 when there is none.</summary>
    public int BestIndex { get; private set; } = -1;

    /// <summary>True once a return to the best scale has been ordered; the next offer is that scale again.</summary>
    public bool IsReturning { get; private set; }

    /// <param name="index">Attempt number, 0 for the largest scale tried.</param>
    /// <param name="isLast">No smaller scale is left to try.</param>
    /// <param name="cost">What the deviations of the planner's layout cost at this scale; null when it has no layout.</param>
    /// <param name="emptiness">What that layout costs for leaving the sheet empty.</param>
    public ConstraintScaleDecision Offer(int index, bool isLast, double? cost, double emptiness = 0.0)
    {
        if (IsReturning)
            return cost.HasValue ? ConstraintScaleDecision.Select : ConstraintScaleDecision.NoLayout;

        if (cost.HasValue && index + cost.Value + emptiness < _bestTotal - Epsilon)
        {
            BestIndex = index;
            _bestTotal = index + cost.Value + emptiness;
        }

        if (BestIndex < 0)
            return ConstraintScaleDecision.NoLayout;

        // A smaller scale can only win if one more step, with nothing given up, beats
        // the best so far. Views shrink with the scale, so the sheet will be at least
        // as empty there as it is here.
        if (!isLast && index + 1 + (cost.HasValue ? emptiness : 0.0) < _bestTotal - Epsilon)
            return ConstraintScaleDecision.LookAhead;

        if (BestIndex == index)
            return ConstraintScaleDecision.Select;

        IsReturning = true;
        return ConstraintScaleDecision.GoBack;
    }
}
