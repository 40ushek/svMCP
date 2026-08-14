# Tekla placement and verification

Read this reference immediately before the first create, recreate, move, or
delete in a placement run.

## Coordinate and order facts

- `points[0]` is the true start of a horizontal or vertical chain.
- Tekla normalizes read-back point order for display. JSON order alone does not
  prove the passed start. Recover it only from a single, connected segment path;
  otherwise mark the start unverified.
- `LengthList` is neither the relative nor absolute printed row. Compute the
  intended rows from the planned start and dimension type.

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

A non-error write response proves only that Tekla accepted a request. Re-read
the dimensions, compare the reference line with the planned side and offset,
and verify the intended points and printed rows. Use the returned dimension ID;
edits can renumber it.
