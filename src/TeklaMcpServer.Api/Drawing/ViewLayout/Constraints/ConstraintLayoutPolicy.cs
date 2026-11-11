namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>
/// What the planner may give up, and at what price, when the views do not fit the
/// way they should. Sheet bounds, overlaps and reserved areas are never negotiable
/// and are not here.
///
/// Prices are in steps of the standard scale series: a price of 1 means "this is
/// as bad as making the drawing one scale step smaller". Until the planner picks
/// the scale itself (roadmap phase 4) prices only rank layouts against each other,
/// and an infinite price forbids the deviation outright.
///
/// A policy narrows what <see cref="ProjectionStrength"/> allows; it never widens
/// it. A view with a strong projection link keeps its side under any policy.
/// </summary>
internal sealed class ConstraintLayoutPolicy
{
    /// <summary>
    /// A view on the side opposite its own, still on the base view's axis: the
    /// projection link holds, the side rule does not.
    /// </summary>
    public double SideDeviationCost { get; set; } = 0.5;

    /// <summary>A view moved into a stack off the base view's axis: the projection link is lost.</summary>
    public double LinkBreakCost { get; set; } = 1.5;

    /// <summary>
    /// For drawings where a readable scale comes first: views go on their side and
    /// in projection when that fits, and sections give those up when it does not.
    /// A wrong side is given up before a projection link.
    /// </summary>
    public static ConstraintLayoutPolicy PreferScale => new();

    /// <summary>
    /// For drawings where projection links and sides come first: no view leaves
    /// its side or its axis. When that does not fit, there is no layout at this scale.
    /// </summary>
    public static ConstraintLayoutPolicy KeepProjection => new()
    {
        SideDeviationCost = double.PositiveInfinity,
        LinkBreakCost = double.PositiveInfinity
    };
}
