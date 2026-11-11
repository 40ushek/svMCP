namespace TeklaMcpServer.Api.Drawing.ViewLayout;

/// <summary>
/// Which planner lays out the views in fit_views_to_sheet. The constraint planner
/// (ROADMAP_CONSTRAINT_LAYOUT.md) is experimental and off unless asked for; when
/// it has no layout at a scale, the current planner's result is used instead.
/// </summary>
public enum DrawingLayoutPlannerMode
{
    /// <summary>The current planner alone. The constraint planner only runs in shadow.</summary>
    Current,

    /// <summary>Constraint planner; no view leaves its side or the base view's axis.</summary>
    ConstraintKeepProjection,

    /// <summary>
    /// Constraint planner; a section may change sides to keep the scale, but leaves
    /// the base view's axis only when the drawing would otherwise lose two scale steps.
    /// </summary>
    ConstraintBalanced,

    /// <summary>Constraint planner; a section leaves its side or axis whenever that keeps a larger scale.</summary>
    ConstraintPreferScale
}
