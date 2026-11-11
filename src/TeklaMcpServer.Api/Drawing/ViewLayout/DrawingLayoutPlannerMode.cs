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

    /// <summary>Constraint planner; sections may leave their side or axis when the views do not fit otherwise.</summary>
    ConstraintPreferScale
}
