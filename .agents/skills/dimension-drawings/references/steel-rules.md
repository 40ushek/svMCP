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
| `minDimensionLength` | 1 mm | Local, not Tekla's - its own default is 0. Drops arithmetic noise only: the radius staircase on a rolled flange steps 0.2-2 mm. |
| Close | closed | An interior position never becomes an endpoint: if an extreme one is dropped, the side loses its chain rather than shrinking. |
| Datum | main part start | For a new chain, its zero is the main part's own model `StartPoint`, unless the plant reverses it (`Reversed direction for running dimensions`) - see `SKILL.md`. All twenty-one absolute rows count up from one end, never down from the other - consistent from a 2886 mm column to a 20180 mm girder. **Not checked against any main part's own `StartPoint`** via the model API, so the direction is observed on every sheet, not API-confirmed on any of them. |
| Row type | `RelativeAndAbsolute` | An attributes choice, not a geometry one, and twenty-one of twenty-one main chains read use it. But **no attributes file that produces it through `create_dimension` is known** - `standard` gives plain `Relative`, and nothing else has been tried. A fresh chain cannot be guaranteed this type until one is found; see Stop. Recreating an existing chain keeps its own type regardless and needs no attributes file for this. |
| Side | free side | One side per subject, held for the drawing. |

## Checks

Neither the sheet nor the person is authority, so the answer is arithmetic. The first
four run on what is already read and are part of the gate:

- every welded part is located by some chain on the sheet;
- each chain closes on the main part's ends;
- an absolute row equals the sum of its relative segments;
- no chain's values are contained in another's, the overall excepted.

The fifth - every dimension point lands on real part geometry - needs
`get_dimension_chain_coverage`, which returns an empty error on a live drawing as of
2026-08-22. Run it; if it errors, say in the final response that this check could not run.
It is a code fix, and it does not block placement.

## Not covered

- **Bolts.** `BoltArray` is not in the structural outline, so edge distances, spacing and
  extreme-bolt checks cannot be planned at all. Tekla devotes a whole tab to them, and this
  plant uses it: regularly spaced chains in plan views (`M.73`, `M.81`) sit exactly where a
  bolted flange splice would be. Confirms the gap; does not close it.
- **Sections.** Untested: whether `get_structural_chain_positions` and `create_dimension`
  work on a section view at all. Most sections, on all ten columns, sit in the same small
  coordinate range as the base views - nothing special expected there. One section per
  drawing does not: its coordinates carry a large, drawing-specific offset (`-58369` on
  `M.82`, `+67454` on `M.77`, no two alike), consistent with a plane cut at the column's own
  position along a building axis rather than at the section itself. Untested either way -
  do not assume the large numbers break `create_dimension`, and do not assume they do not.
- **Skew, grouping, work points, centre of gravity** - Tekla settings with no counterpart here.
- Trusses and braced frames as their own assembly (diagonals as part of this assembly's
  own geometry, not bolted on externally through gusset plates) remain unmeasured.

## Stop

- no single resolvable main part, or any `mainPartUnresolvedModelIds`;
- `Internal` unstated, or `Necessary` without a `recognizableDistance`;
- a bolt dimension is required;
- a section must be dimensioned - untested end to end, see Not covered;
- a fresh `RelativeAndAbsolute` chain is needed and no attributes file producing that type
  has been confirmed - ask the operator for the file name, or limit the run to recreating
  chains that already carry the type.

Tekla's own model behind this file: *Dimensioning rule properties*, *Dimensioning
properties (Integrated dimensioning)* - General, Position, Part, Bolt and Grouping tabs -
and `dim_planes_table.txt` in the environment.
