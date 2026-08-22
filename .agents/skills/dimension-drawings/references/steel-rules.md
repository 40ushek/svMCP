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

Measured once, on `M.80`: this logic reproduced that sheet's vertical chain point for
point, six of six, without consulting any of the settings below.

## Settings

Where reasonable people differ. Each has a default; state in the final response which
values a run used.

| Setting | Default | Notes |
|---|---|---|
| Anchor | faces | An axis is allowed when the sheet already uses one. Compute it only from a qualifying pair - same `modelId`, both `kind=AxisAlignedEdge`, non-null `partExtentAlongChain`, the two coordinates differing by exactly that extent within `partSpanMatchToleranceMm`. Otherwise faces. |
| `Internal` | ask | `None` no secondary-part internal dimensions; `Necessary` keeps what the shape cannot tell; `All` keeps every admissible one. Never blend: `Necessary` under `All` deletes what the plant wants, anything under `None` adds what it does not. |
| `recognizableDistance` | ask | The asymmetry below which a fitter cannot orient a part. Required by `Necessary`, by nothing else; without it `Necessary` stops. Tekla's dialogs use tens of mm. |
| `minDimensionLength` | 1 mm | Local, not Tekla's - its own default is 0. Drops arithmetic noise only: the radius staircase on a rolled flange steps 0.2-2 mm. |
| Close | closed | An interior position never becomes an endpoint: if an extreme one is dropped, the side loses its chain rather than shrinking. |
| Datum | main part start | For a new chain, its zero is the main part's own model `StartPoint`, unless the plant reverses it (`Reversed direction for running dimensions`) - see `SKILL.md`. On `M.80`, sheet `615.5 / 1005.5 / 1074.5` = read `640.4 / 1030.4 / 1099.5` minus 25, consistent with counting from the top of the base plate - **not checked against the column's own `StartPoint`**, so treat the offset as observed, not confirmed, until it is. |
| Row type | match the sheet, else ask | An attributes choice, not a geometry one. One data point exists and it does not favour a default: `M.80`'s own chain is `RelativeAndAbsolute`, while `create_dimension` with `standard` gives `Relative`. Recreating a chain keeps its type; a fresh chain with no sheet convention to match is asked. |
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
  extreme-bolt checks cannot be planned at all. Tekla devotes a whole tab to them.
- **Sections.** Dimensions read from section `I` on `M.80` sit near `x = -43369` while the
  view occupies `x = 50...172`. Settle that coordinate system before writing into a section.
- **Skew, grouping, work points, centre of gravity** - Tekla settings with no counterpart here.
- Trusses, braced frames, welded plate girders: one column is the whole evidence.

## Stop

- no single resolvable main part, or any `mainPartUnresolvedModelIds`;
- `Internal` unstated, or `Necessary` without a `recognizableDistance`;
- a bolt dimension is required;
- a section must be dimensioned and its coordinate system is not yet settled.

Tekla's own model behind this file: *Dimensioning rule properties*, *Dimensioning
properties (Integrated dimensioning)* - General, Position, Part, Bolt and Grouping tabs -
and `dim_planes_table.txt` in the environment.
