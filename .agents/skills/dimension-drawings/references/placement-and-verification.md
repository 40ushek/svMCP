# Tekla placement and verification

Read this reference immediately before the first create, recreate, move, or
delete in a placement run.

## Coordinate and order facts

- **`create_dimension`'s points are view coordinates in millimetres, Z always
  `0` for a base view — the same system `get_structural_chain_positions`,
  `get_drawing_dimensions`, and every other read in this loop already use.**
  Feed the coordinates you just read straight back in; do not test this with a
  throwaway call or search the codebase for an example first. Measured on
  EWA.5: not stating this plainly cost six minutes of grepping `HISTORY.md` and
  the test suite for a coordinate-format example, when the numbers needed were
  already sitting in the same `get_structural_chain_positions` response the
  plan was built from.
- `points[0]` is the true start of a horizontal or vertical chain.
- Tekla normalizes read-back point order for display. JSON order alone does not
  prove the passed start. Recover it only from a single, connected segment path;
  otherwise mark the start unverified.
- `LengthList` is neither the relative nor absolute printed row. Compute the
  intended rows from the planned start and dimension type.
- **When a part touches another part, its own real corner exists on both
  faces - the face it shares with the other part, and its own outer/visible
  face. Use the outer face, the one actually drawn in the view, not the
  touching one.** Both are real, so this is not the floating-point bug above -
  picking the touching face still lands on a real vertex, but not the one a
  person reading the sheet would measure to. Measured on M.505, FrontView
  `2724`: a 20 mm end plate spans X=-20 (its own outer face) to X=0 (where it
  meets the beam, X=0 is also the beam's own start). A vertical dimension
  built with the plate's points at X=0 read cleanly and passed every other
  check, but its witness lines started at the hidden, touching face instead
  of the plate's drawn outline; the fix was moving the plate's two points to
  X=-20, its true outer face, matching the horizontal chain on the same view
  that already used it.
- **Do not reuse one edge coordinate across two points at different positions
  along the chain.** Each point needs its own real boundary, read from that
  same position's own support in `get_structural_chain_positions` - not copied
  from another point because the footprint looked rectangular. Measured on
  M.505: an overall chain used Y=-170 (the near end plate's own edge) at both
  the near AND the far end, but the far end plate is narrower - its real edge
  there is only Y=-68. The far leader had no edge within 100 mm to land on and
  hung in open space; a person looking at the sheet caught it, the write and
  read-back both reported success. Read each point's own Y (or X, for a
  vertical chain) from its own position in the response before creating.
- **This applies to the cross-axis coordinate too, not just the chain axis.**
  A `StraightDimensionSet`'s points do **not** need to share the cross-axis
  coordinate (Y, for a horizontal chain) with each other - each point only
  needs its own coordinate pair to be a real vertex of its own part. Do not
  default the cross-axis coordinate to 0 or to a centreline just because it
  is a convenient constant shared by every point; read it per point, the same
  as the chain axis. Measured on M.16, SectionView `2152`: a horizontal chain
  (230/130/-120/-130 in X) held Y=0 for all four points; X=130 at Y=0 falls
  between an I-profile's flanges, where there is no material at all, not on a
  line - Tekla flagged it. The fix used each part's own real corner Y (plate
  top/bottom Y=±140, column top/bottom Y=±130) - confirmed again on the same
  drawing's views `1641` and `1408`, where a correct horizontal chain's four
  points carry four different Y values, one per real vertex.
- **A small real step between two parts' edges makes the connecting segment
  hug the part's own corner - that is correct, not an overlap bug.** When
  adjacent chain points come from different parts whose edges differ by only
  a few mm (one part overhangs the other slightly), the dimension jogs there,
  and the jog sits right next to the real corner - it can look like the
  dimension is drawn on top of the part outline. Before treating this as
  wrong, confirm both points against `get_all_parts_geometry_in_view`'s
  `viewHull`/`solidVertices` (ground truth, not the derived chain). Measured
  on M.16, SectionView `2152`: column (HEB260) edge at X=-130, base plate
  (BLE30×280) edge at X=-120 - a real 10 mm overhang, not a coordinate error.
  `get_dimension_chain_coverage`'s reliability is unresolved (see
  `steel-rules.md`, Checks) - do not depend on it; confirm against
  `get_all_parts_geometry_in_view` instead.
- **For a profiled beam (I/H-section etc.), `viewHull`/the structural-chain
  extent is the part's bounding box, not its true cross-section polygon.**
  Fillets between web and flange, and any other curve inside the box, are not
  modelled - a "corner" from this data is a box corner, not necessarily a
  point on the real drawn contour. Tekla's own pink "unresolved point"
  warning can still appear on a box-corner point that is otherwise correctly
  read, when the true section has a fillet near that spot. This is a tool
  gap, not a wrong point choice - do not try to "fix" it by guessing a
  different coordinate. Measured on M.16, SectionView `2152` (HEB260 web
  fillet near the flange tip).

## Placement facts

- Direction selects the reference-line side. Distance is a magnitude; a negative
  value does not put a line on the opposite side.
- `horizontal` and `horizontal-down` are fixed signs, not aliases for an existing
  line's side. For vertical chains, use the corresponding vertical direction.

  | keyword | line goes | read back as |
  |---|---|---|
  | `horizontal` | above the points | `topDirection = 1` |
  | `horizontal-down` | below the points | `topDirection = -1` |
  | `vertical-left` | left of the points | `topDirection = 1` |
  | `vertical` | right of the points | `topDirection = -1` |

  The official TS2025 XML (`Tekla.Structures.Drawing.xml`, properties and
  `CreateDimensionSet`) defines `UpDirection` as the direction from dimension
  points to the dimension line. The explicit vector selects the side; point
  order and the sign of `Distance` do not.
  Confirmed on EW.8 and EW.18: a bottom chain needs `horizontal-down`, a chain
  left of the panel needs `vertical-left`. Do not infer the keyword from the
  word "vertical" alone — the bare form goes right.
- Before replacing a chain, read its `topDirection` and reference-line position;
  select the matching direction keyword.
- `move_dimension` takes a delta, not an absolute target.
- **`distance` is measured from a BASE point of the chain, along the direction
  vector.** TS2025's official XML documents `StraightDimensionSet.Distance` as
  paper millimeters from the first dimension point, but live measurements disagree:
  on tested 1:5 and 1:10 views it behaved as view units (paper gap × view scale).
  Measured: 220 at 1:10 gave a 22 mm paper offset (M.48 section E, left chain),
  460.255 gave 46 mm (its top chain), 80 gave 8 mm (M.48 `Hinten`, top chain); the
  three 1:5 dimensions scaled the same way. No 1:3 dimension was available.
  Treat this as measured TS2025 behavior, not a universal API rule. The base is
  the first point
  *after Tekla orders the chain along its axis*: the LEFTMOST point of a horizontal
  chain, the LOWEST point of a vertical one, NOT necessarily the point you passed
  first and NOT the outermost point. (Measured on M.48 view 4732: an overall chain
  passed right-point-first got `InitialDistance` 949.95 = 389.95 + 560, i.e. Tekla
  counted from the leftmost point; the line landed at y~230, inside the girder, and
  a screenshot confirmed it.) The side comes from the direction keyword, never from
  the sign of `distance` or from point order; point order only decides where the
  chain starts counting.
  To put the line a `gap` beyond the outermost measured point pass
  `distance = gap + (offset-side extreme coordinate - base point coordinate)`
  along the offset direction, where the base point is the leftmost (horizontal) or
  lowest (vertical) point. The order in which you pass the points does not change
  the base (it only sets where the chain starts counting), so compute the base
  yourself from the coordinates.
  Example (M.48 section E, top chain, base y=86.8, outermost y=427.05, gap 120):
  `distance = 120 + 427.05 - 86.8 = 460.25`. Passing 120 put the line at y~207,
  inside the end plate. The 120 (12 mm on paper at 1:10) is a measured example,
  not the standard gap; the structured-plan default paper gap is 8 mm (see the roadmap).
  A gap in paper mm becomes `paperGap x viewScale`.
  In the 55-dimension probe across 1:5 and 1:10, all 36 comparable cases matched
  the leftmost/lowest-point base; the other 19 apparent mismatches were explained
  by view breaks (vertical dimensions past a cut-out part of a long view), not by
  a different base rule. Only 16 of the 36 tell the base from the outermost point
  apart (in the other 20 they coincide), and only 3 dimensions were 1:5.
  Measured values from a chain the user placed by hand on M.48 section C: about 68
  view units above the flange and about 86 left of the plate (7-9 mm on paper at
  1:10); use as a starting point, not as a rule.
- **Do not trust the read-back `referenceLine` for the offset side.** As of this
  writing `get_drawing_dimensions` (and the verified writer's "correction") assume the
  extreme point, so `referenceLine` can show a line that is not where Tekla draws it,
  and `writeState.Verified=true` does not prove the line is outside the assembly.
  Compute the expected line yourself (`base point coordinate + distance`, base =
  leftmost/lowest point) and state it. The rendered line can be read per
  `StraightDimension` segment with `GetObjectPresentation(segmentId)` (Host probe
  `--dimension-presentation-probe`); the set id itself returns null. Otherwise the
  user's screenshot settles it.
- Dimension creation measures the offset from its points, not from a part edge.
- Creating or recreating a chain may reflow other chains. Re-read them.

## Verification

One `get_drawing_dimensions <viewId>` read after each potentially reflowing write
serves the point, row, reference-line and neighbour checks together. Do not add
a full solid read or coverage call when these checks expose no specific doubt.
Reuse the unchanged structural snapshot; refresh dimension state after writes.

On bridge versions exposing `writeState`, inspect it as well as the returned ID.
The verified writer checks its new chain, not all neighbouring chains or drafting
sufficiency. A failure before the original is touched removes the new set
(`newDimensionRemoved`); a failed cleanup or a failure after the original delete
started can leave the replacement present and the original retained, deleted or
uncertain depending on the stage. Re-read both IDs before any retry/cleanup; the
protocol is not atomic. No speculative delete/recreate loop to discover a
coordinate or style.

Without an explicit verified state, a non-error response proves only that Tekla
accepted a request. In either case, re-read
the dimensions, compare the planned side and offset with the expected line
(base point + distance; the read-back `referenceLine` is not independent, see
Placement facts), and verify the intended points and printed rows. Use the
returned dimension ID; edits can renumber it.
