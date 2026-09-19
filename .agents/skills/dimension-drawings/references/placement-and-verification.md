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

  Confirmed on EW.8 and EW.18: a bottom chain needs `horizontal-down`, a chain
  left of the panel needs `vertical-left`. Do not infer the keyword from the
  word "vertical" alone — the bare form goes right.
- Before replacing a chain, read its `topDirection` and reference-line position;
  select the matching direction keyword.
- `move_dimension` takes a delta, not an absolute target.
- Dimension creation measures the offset from its points, not from a part edge.
- Creating or recreating a chain may reflow other chains. Re-read them.

## Verification

One `get_drawing_dimensions <viewId>` read after each potentially reflowing write
serves the point, row, reference-line and neighbour checks together. Do not add
a full solid read or coverage call when these checks expose no specific doubt.
Reuse the unchanged structural snapshot; refresh dimension state after writes.

On bridge versions exposing `writeState`, inspect it as well as the returned ID.
The verified writer checks its new chain, not all neighbouring chains or drafting
sufficiency. A failed call can leave the replacement present and the original
retained, deleted or uncertain depending on the stage. Re-read both IDs before
any retry/cleanup; do not assume atomic rollback. No speculative delete/recreate
loop to discover a coordinate or style.

Without an explicit verified state, a non-error response proves only that Tekla
accepted a request. In either case, re-read
the dimensions, compare the reference line with the planned side and offset,
and verify the intended points and printed rows. Use the returned dimension ID;
edits can renumber it.
