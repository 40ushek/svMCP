namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>
/// What the planner may give up, and at what price, when the views do not fit the
/// way they should. Sheet bounds, overlaps and reserved areas are never negotiable
/// and are not here.
///
/// Prices are in steps of the standard scale series: a price of 1 means "this is
/// as bad as making the drawing one scale step smaller". Within one scale they
/// rank layouts against each other; across scales ConstraintScaleSearch adds them
/// to the scale steps taken. An infinite price forbids the deviation outright.
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

    /// <summary>
    /// A section beside the view that carries its section mark, level with its own
    /// section line: off the side its arrows point to, but next to the place where
    /// it is cut, where the reader looks for it first. Cheaper than any other
    /// deviation for that reason.
    /// </summary>
    public double BesideCutCost { get; set; } = 0.25;

    /// <summary>
    /// What every 100 mm of sheet between a section and the middle of its section
    /// line costs. The further a section is from its cut, the longer the reader
    /// looks for it. It ranks the layouts of one scale against each other and is
    /// not carried across scales: a smaller scale shortens every distance on the
    /// sheet, and that is no reason to prefer it.
    /// </summary>
    public double CutDistanceCost { get; set; } = 0.25;

    /// <summary>A view moved into a stack off the base view's axis: the projection link is lost.</summary>
    public double LinkBreakCost { get; set; } = 1.5;

    /// <summary>
    /// Fill ratio (view area over the sheet area free of tables) below which the
    /// sheet looks empty. The same threshold ROADMAP_DRAWING_LAYOUT.md names.
    /// </summary>
    public double EmptySheetFill { get; set; } = 0.5;

    /// <summary>
    /// What a completely empty sheet costs. The price falls linearly to nothing as
    /// the fill ratio rises to <see cref="EmptySheetFill"/>. It makes a step to a
    /// smaller scale dearer the emptier the sheet already is, so a half-empty
    /// drawing is not shrunk further just to keep a view on its axis.
    /// </summary>
    public double EmptySheetCost { get; set; } = 2.0;

    /// <summary>What a layout with this fill ratio costs for leaving the sheet empty, in scale steps.</summary>
    public double GetEmptinessCost(double fillRatio)
    {
        if (EmptySheetFill <= 0 || fillRatio >= EmptySheetFill)
            return 0.0;

        return (EmptySheetFill - System.Math.Max(fillRatio, 0.0)) / EmptySheetFill * EmptySheetCost;
    }

    /// <summary>
    /// The class defaults. A view may change sides to keep the scale, but a
    /// projection link is worth more than one scale step: the drawing goes one step
    /// smaller before a view leaves the base view's axis.
    /// </summary>
    public static ConstraintLayoutPolicy Balanced => new();

    /// <summary>
    /// For drawings where a readable scale comes first, because dimensions have to
    /// be legible: a view leaves its side or the base view's axis whenever that
    /// keeps the drawing one scale step larger. A wrong side is still given up
    /// before a projection link, and two views off the axis cost more than a step.
    /// </summary>
    public static ConstraintLayoutPolicy PreferScale => new() { LinkBreakCost = 0.75 };

    /// <summary>
    /// For drawings where projection links and sides come first: no view leaves
    /// its side or its axis. When that does not fit, there is no layout at this scale.
    /// </summary>
    public static ConstraintLayoutPolicy KeepProjection => new()
    {
        SideDeviationCost = double.PositiveInfinity,
        BesideCutCost = double.PositiveInfinity,
        LinkBreakCost = double.PositiveInfinity
    };
}
