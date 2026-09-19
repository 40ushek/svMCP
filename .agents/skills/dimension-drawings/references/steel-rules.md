# Steel rules for AssemblyDrawing dimensions

For a steel assembly: one main member with plates, gussets and stiffeners fixed to it.
Read this instead of `plant-rules.md`, never both.

This is **one** coherent logic, not the only correct one. Neither a drawing nor a
draftsman is ground truth - both vary, both err. What the skill owes is one logic held to
the end, the same on every drawing, plus the checks below.

## The logic

The main part lies on the welding table and everything is welded to it. So:

- **the reference body is the main part** - `mainPartModelIds`, read from the assembly.
  Closure and datum follow the shared rule in `SKILL.md`;
- one chain runs **along** it, carrying a point where each welded part lands, whichever
  face of the member it sits on;
- the assembly's **overall** is its own chain, two positions only;
- the **ends of the main part** are dimensioned where they are cut or raked;
- a secondary part's **own size is printed when it is the base for the next measurement** -
  e.g. `615.5 · 390 · 1005.5 · 69 · 1074.5`: without the plate's 390 the 69 to the next
  part cannot be measured.

**What a dimension on an assembly drawing is for.** Standard drafting practice splits every
dimension into a **size dimension** ("how big is this feature") or a **location dimension**
("where is it relative to something else") - an assembly drawing's job is the second, not
the first. A part's own size already lives on its own single-part drawing; repeating it here
with nothing tying it to another part is a size dimension standing where a location
dimension belongs, however correct the number is on its own.

**On an `EndView`** (looking down the main part's own length, at a plate or cap welded to
its end): the same principle applies across the profile instead of along it. Dimension where
a secondary part's edge sits relative to the main part's own profile edge - the overhang past
it, or the inset short of it - never the secondary part's own bare width/height. Worked out on
`M.505`: view `1709` (base plate `260×340` on a `HEA160`) needs a 4-point chain per side,
`50 / 160 / 50` and `94 / 152 / 94` - the plate's overhang past the flange on each side, with
the beam's own known profile width folded in between rather than stated on its own. View
`1599` (a narrower `160×136` cap plate on the same beam) needs only one chain, `8 / 136 / 8`
- the plate sits flush with the beam in the other axis, so nothing gets dimensioned there.
**This was analysis, not a placement record** - as of 2026-08-23 neither view has these
dimensions on the sheet (both read empty); re-derive from `get_structural_chain_positions`
before placing rather than trusting these numbers as already-placed.

Coordinates come from `get_structural_chain_positions` and from nowhere else.

Read on twenty-one drawings across three member types: ten HEB160 columns,
`M.73`–`M.82`; eight IPE220 beams, `M.30`/`M.31`/`M.33`/`M.34`/`M.37`/`M.38`/`M.39`/`M.70`;
three HEB800 girders, `M.47`/`M.48`/`M.355` (six more `Binder` drawings selected but not read
in detail). **What this checked, and what it did not.** It is a reading exercise: each
sheet's existing dimensions and its parts list were compared by eye, part type against
point-list coordinate ranges. It confirmed the logic is *plausible* on all three member
types - every part type present has some point that could be it, no chain contradicts
closing on the main part's own ends, and the row type is consistent. It did **not** run
`get_structural_chain_positions` on any of these twenty-one, and did not link one specific
part's `modelId` to one specific `dimensionId` and coordinate. That per-part audit is what
the Checks below require of an actual placement run, and none of these twenty-one were a
placement run - the sheets were already dimensioned, by the plant, before this skill looked
at them.

Two things this reading did establish concretely, because they need no per-part linkage to
see: One beam, `M.38`, has no end plate at either end and its chain still closes on the
beam's own bare ends - closure follows the main part, not what is welded to it. The girders
carry no regular spacing at all - their node gussets sit wherever a diagonal brace actually
connects, and `M.355` has four more of them mid-span than its otherwise identical twins
`M.47`/`M.48`, which is evidence against inventing a position from a pattern, whatever the
per-part linkage.

## Settings

Where reasonable people differ. Each has a policy - a default, or a question, or a stop
until something is confirmed. State in the final response which applied.

| Setting | Default | Notes |
|---|---|---|
| Anchor | faces | An axis is allowed when the sheet already uses one. Compute it only from a qualifying pair - same `modelId`, both `kind=AxisAlignedEdge`, non-null `partExtentAlongChain`, the two coordinates differing by exactly that extent within `partSpanMatchToleranceMm`. Otherwise faces. |
| `Internal` | ask | `None` no secondary-part internal dimensions; `Necessary` keeps what the shape cannot tell; `All` keeps every admissible one. Never blend: `Necessary` under `All` deletes what the plant wants, anything under `None` adds what it does not. |
| `recognizableDistance` | ask | The asymmetry below which a fitter cannot orient a part. Required by `Necessary`, by nothing else; without it `Necessary` stops. Tekla's dialogs use tens of mm. |
| `minDimensionLength` | 3 mm | Local, not Tekla's - its own default is 0. The filter drops anything *shorter* than this, so it must sit above the noise it targets: the radius staircase on a rolled flange steps 0.2-2 mm, and 3 mm clears all of it with headroom. Still below the smallest genuine feature seen - a 5 mm segment at a raked corner on `M.78`. |
| Chain-break test | different reference | A point only earns its own segment when it marks a transition to a *different* reference - another part's edge, or a distinct functional feature (hole, cut). A corner or small radius along one part's own contour is not such a transition, even past `minDimensionLength` - both neighbours already bound the same span, and nobody measures to the midpoint of a rounded corner. Collapse it to the span's two real endpoints. Measured on M.16, SectionView `1641`: a plate's own ~10 mm corner radius between the column's edge and the plate's tip produced a spurious `10 / 70` split of one real `80` mm overhang - the corner point (`viewHull` drops it; only the raw vertex list carries it) sits on the same part as both neighbours and marks nothing a fitter would check independently. |
| Close | closed | An interior position never becomes an endpoint: if an extreme one is dropped, the side loses its chain rather than shrinking. |
| Datum | main part start | For a new chain, its zero is the main part's own model `StartPoint`, unless the plant reverses it (`Reversed direction for running dimensions`) - see `SKILL.md`. All twenty-one absolute rows count up from one end, never down from the other - consistent from a 2886 mm column to a 20180 mm girder. **Not checked against any main part's own `StartPoint`** via the model API, so the direction is observed on every sheet, not API-confirmed on any of them. |
| Row type | `RelativeAndAbsolute` | An attributes choice, not a geometry one, and twenty-one of twenty-one main chains read use it. But **no attributes file that produces it through `create_dimension` is known** - `standard` gives plain `Relative`, and nothing else has been tried. A fresh chain cannot be guaranteed this type until one is found; see Stop. Recreating an existing chain keeps its own type regardless and needs no attributes file for this. |
| Side | free side | One side per subject, held for the drawing. |

## Checks

Neither the sheet nor the person is authority, so the answer is arithmetic. The first
four run on what is already read and are part of the gate:

- every welded part is located by some chain on the sheet;
- longitudinal location chains close on the main part's ends; transverse plate
  chains use their explicitly planned closure (including plate edges around the
  main-profile edges). Reference body and closure are not interchangeable;
- an absolute row equals the sum of its relative segments;
- no chain's values are contained in another's, the overall excepted.

The fifth - every dimension point lands on real part geometry - starts with the
selected support and the dimension read-back already available. Request extra
geometry only for a specific unresolved anchor or visible warning, not once per
dimension by default. `get_dimension_chain_coverage` is an optional targeted
diagnostic. Its reliability is unresolved, not confirmed broken: it
returned an empty error on M.16 (SectionViews `2152` and `1641`) and M.505 (FrontView `2724`)
on 2026-08-23, but was reported working - checking all four points of a created dimension -
on M.505's EndView `1709` earlier the same day, on a drawing that has since had those
dimensions replaced. If used and it errors, report that diagnostic failure and
do not repeat it unchanged for every chain. Reuse actual geometry already read,
or read the suspect part with `get_part_geometry_in_view` (the whole view only
when several parts genuinely need it). A hull/bbox alone does not prove a point
on a profiled or curved contour; keep a remaining anchor doubt explicit.
The failed diagnostic alone does not block placement, but an unresolved required
anchor does. Geometry verification cannot supply new chain coordinates.

## Not covered

- **Bolts.** `BoltArray` is not in the structural outline, so edge distances, spacing and
  extreme-bolt checks cannot be planned at all. Tekla devotes a whole tab to them, and this
  plant uses it: regularly spaced chains in plan views (`M.73`, `M.81`) sit exactly where a
  bolted flange splice would be. Confirms the gap; does not close it.
- **Sections and end views.** Three separate findings now, not one guess.
  1. On the ten measured columns, most section views sit in the same small coordinate
     range as the base views; one per drawing carries a large, drawing-specific offset
     (`-58369` on `M.82`, `+67454` on `M.77`, no two alike), consistent with a plane cut at
     the column's own position along a building axis. Whether `create_dimension` accepts
     those large numbers is still untested.
  2. **The old blanket failure is no longer the current routing rule.** Before depth-aware
     selection, three distinct M.505 section/end views (A-A, B-B, C-C) returned
     byte-identical coordinates while every read began with all nine assembly parts.
     `get_structural_chain_positions 1709` now reads the two depth-selected parts and was
     visually checked against that end view. This is evidence for the narrow exception
     below, not proof that every section cut is safe on its own.
  3. The Left/Right coordinates it does return step in 0.2-2 mm increments near the
     web-to-flange transition (51 to 68 mm, dozens of points) - the rolled profile's own
     fillet radius, polygonized. Not a real feature to dimension even once the routing bug
     above is fixed. This is exactly why the `minDimensionLength` setting above is set to 3 mm and
     not 1: a 1 mm cutoff only drops steps *shorter* than 1 mm and lets the 1-2 mm end of
     this same staircase straight through.
- **Skew, grouping, work points, centre of gravity** - Tekla settings with no counterpart here.
- Trusses and braced frames as their own assembly (diagonals as part of this assembly's
  own geometry, not bolted on externally through gusset plates) remain unmeasured.

## Section/end-view exception

A `SectionView` or `EndView` may be dimensioned only when all of the following are true:

1. `get_structural_chain_positions <viewId>` returns `isComplete=true` with no issues.
2. Every retained candidate on each side being created/recreated agrees visually
   with geometry in a current image of that exact drawing/view. Use an available
   current view image or request one if needed. `draw_structural_chain_positions`
   is optional, not the gate itself: it creates drawing objects. Use it only when
   the image is insufficient and temporary drawing marks are authorized; remove
   only the overlay objects created by this run after the check. No usable visual
   evidence means this gate remains unresolved, not automatically passed.
3. The plan does not require bolt dimensions. Bolt arrays are still outside the structural
   outline contract.
4. The final response says that this was the section/end-view exception, names the view
   type, and records the visual verification.

This is a deliberately narrow manual gate, and it carries a known, accepted risk: the code
selects the right parts by depth, but projects their full solids without a 3D clip, and does
not flag a selected solid that extends past the view depth (see
`ROADMAP_SECTION_CROSS_SECTIONS.md`, "Open gap: no signal when an included solid outgrows
the window" - deliberately not built). On a `SectionView` specifically, a continuous member
cut mid-span is the ordinary case, not the exception, so this risk is live on every such view,
not a corner case. Step 2's visual check is what stands in for the missing code check - treat
it as load-bearing, not a formality, and stop rather than guess when it cannot confirm a
retained point.

## Stop

- no single resolvable main part, or any `mainPartUnresolvedModelIds`;
- `Internal` unstated, or `Necessary` without a `recognizableDistance`;
- a bolt dimension is required;
- a `SectionView` or `EndView` must be dimensioned but does not meet every condition in
  **Section/end-view exception**;
- a fresh `RelativeAndAbsolute` chain is needed and no attributes file producing that type
  has been confirmed - ask the operator for the file name, or limit the run to recreating
  chains that already carry the type.

Tekla's own model behind this file: *Dimensioning rule properties*, *Dimensioning
properties (Integrated dimensioning)* - General, Position, Part, Bolt and Grouping tabs -
and `dim_planes_table.txt` in the environment.
