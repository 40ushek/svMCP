using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

/// <summary>
/// What the planner may give up, and at what price, when the views do not fit the
/// way they should. Sheet bounds, overlaps and reserved areas are never negotiable
/// and are not here.
///
/// Within one scale the prices rank layouts against each other. Across scales
/// the planner compares scale combinations by fill ratio (ConstraintScaleVariants)
/// and the prices only break a tie between sheets about as full. Only the
/// fallback scale loop (ConstraintScaleSearch) still adds them to
/// <see cref="ScaleStepCost"/> for every step of the scale series taken. An
/// infinite price forbids the deviation outright.
///
/// Every figure of the planner a user may want to tune lives here: a settings
/// window and file are planned on top of this object.
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

    /// <summary>
    /// A view in a stack on its own side: off the base view's axis, but where the
    /// reader expects it, beside the base view and under the views that stand in
    /// line there.
    /// </summary>
    public double OwnSideStackCost { get; set; } = 0.4;

    /// <summary>A view moved into a stack off the base view's axis: the projection link is lost.</summary>
    public double LinkBreakCost { get; set; } = 1.5;

    /// <summary>
    /// What making the drawing one scale step smaller costs, in the units of the
    /// prices above. Used by the fallback scale loop only; the planner's own
    /// choice of scales goes by fill ratio. A readable scale comes before views on their axes: on M.179,
    /// M.433 and M.123 the user took the larger scale each time, on M.123 with
    /// three views off the base view's axis (4.5). No drawing so far went the other
    /// way, so the figure has a lower bound from the journal and no upper one.
    /// </summary>
    public double ScaleStepCost { get; set; } = 4.0;

    /// <summary>
    /// The scales the assembly views may be given, as denominators. A setting of
    /// this software, not of the drawing's layout in Tekla (the user's decision).
    /// </summary>
    public IReadOnlyList<double> MainViewScales { get; set; } = StandardScales;

    /// <summary>The scales the sections may be given. A section is never drawn smaller than the assembly.</summary>
    public IReadOnlyList<double> SectionViewScales { get; set; } = StandardScales;

    // Without 1:2 and 1:1, on the user's word (2026-11-11): a section at 1:2 beside
    // an assembly at 1:5 or 1:10 came out too large on M.44, M.433 and M.179.
    private static readonly double[] StandardScales =
        { 5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 75, 80, 100, 125, 150, 175, 200, 250, 300 };

    /// <summary>
    /// How many combinations of view places the planner looks at for one drawing
    /// at one set of scales. A bound on the search, reported as "truncated" when hit.
    /// </summary>
    public int MaxTopologies { get; set; } = 2000;

    /// <summary>How many layouts that fit are kept; these are the numbered variants a caller may pick from.</summary>
    public int MaxCandidates { get; set; } = 16;

    /// <summary>
    /// Layouts are collected in order of what their slots cost, and only then priced
    /// for distance. A layout a little dearer by slots can be the cheapest overall,
    /// so this many times more are collected than are kept.
    /// </summary>
    public int CandidatePoolFactor { get; set; } = 8;

    /// <summary>
    /// Fill ratios of two scale combinations closer than this count as the same;
    /// the larger assembly, then the smaller price of deviations, decides.
    /// </summary>
    public double ScaleFillTie { get; set; } = 0.05;

    /// <summary>How many assembly scales are looked at, from the largest that can fit down.</summary>
    public int MainScaleDepth { get; set; } = 4;

    /// <summary>
    /// How many section scales are looked at for each assembly scale, from the
    /// assembly's own up. It bounds the search, not the drawing: the user ruled out
    /// "a section at most one step larger than the assembly" twice.
    /// </summary>
    public int SectionScaleDepth { get; set; } = 3;

    /// <summary>How many scale combinations are tried on the drawing in one run.</summary>
    public int MaxScaleProbes { get; set; } = 4;

    /// <summary>
    /// How far the gap between views may shrink, as a share of the gap asked for,
    /// to keep a view in a cheaper place.
    /// </summary>
    public double TightGapShare { get; set; } = 0.5;

    /// <summary>Below this, in sheet millimeters, the views are too close to tell apart; the gap is not shrunk past it.</summary>
    public double MinTightGap { get; set; } = 2.0;

    /// <summary>
    /// Fill ratio (view area over the sheet area free of tables) below which the
    /// sheet looks empty. The same threshold ROADMAP_DRAWING_LAYOUT.md names.
    /// </summary>
    public double EmptySheetFill { get; set; } = 0.5;

    /// <summary>
    /// What a completely empty sheet costs. The price falls linearly to nothing as
    /// the fill ratio rises to <see cref="EmptySheetFill"/>. It makes a step to a
    /// smaller scale dearer the emptier the sheet already is, so a half-empty
    /// drawing is not shrunk further just to keep a view on its axis. As much as a
    /// scale step: an empty sheet is a defect, not a matter of taste.
    /// </summary>
    public double EmptySheetCost { get; set; } = 4.0;

    /// <summary>What a layout with this fill ratio costs for leaving the sheet empty.</summary>
    public double GetEmptinessCost(double fillRatio)
    {
        if (EmptySheetFill <= 0 || fillRatio >= EmptySheetFill)
            return 0.0;

        return (EmptySheetFill - System.Math.Max(fillRatio, 0.0)) / EmptySheetFill * EmptySheetCost;
    }

    /// <summary>
    /// The class defaults. Views leave their side or the base view's axis to keep
    /// the scale; the drawing goes a step smaller only when about three of them
    /// would have to, and the smaller scale does not leave the sheet empty.
    /// </summary>
    public static ConstraintLayoutPolicy Balanced => new();

    /// <summary>
    /// For drawings where a readable scale comes first, because dimensions have to
    /// be legible: a view off the base view's axis costs half of what it does under
    /// <see cref="Balanced"/>, so more views leave it before the scale gives way.
    /// A wrong side is still given up before a projection link.
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
        OwnSideStackCost = double.PositiveInfinity,
        LinkBreakCost = double.PositiveInfinity
    };
}
