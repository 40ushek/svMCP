namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Which planner lays out the views in fit_views_to_sheet. The constraint planner
/// (ROADMAP_CONSTRAINT_LAYOUT.md) is the main one: a caller that names no planner
/// gets <see cref="ConstraintBalanced"/> on an assembly drawing. When it has no
/// layout at a scale, the legacy planner's result is used instead.
/// </summary>
public enum DrawingLayoutPlannerMode
{
    /// <summary>
    /// Deprecated: the planner fit_views_to_sheet had before the constraint planner
    /// (a chain of packing steps). Kept as the fallback and for the drawing kinds
    /// the constraint planner has not been tried on (GA, single-part); not
    /// developed further and removed once the constraint planner covers those.
    /// The constraint planner only runs in shadow under it.
    /// </summary>
    Legacy,

    /// <summary>Constraint planner; no view leaves its side or the base view's axis.</summary>
    ConstraintKeepProjection,

    /// <summary>
    /// Constraint planner, the default; a section may change sides to keep the scale, but leaves
    /// the base view's axis only when the drawing would otherwise lose two scale steps.
    /// </summary>
    ConstraintBalanced,

    /// <summary>Constraint planner; a section leaves its side or axis whenever that keeps a larger scale.</summary>
    ConstraintPreferScale
}
