# Dimensions Roadmap

## Goal

Rebuild `Drawing/Dimensions` around the geometry model proven in
`D:\repos\svMCP\dim`:

- `DimensionType`
- `Direction`
- `TopDirection`
- `ReferenceLine`
- `LeadLineMain`
- `LeadLineSecond`

The arrangement track should be line-first, not orientation-first and not
bbox-first.

## Current State

Publicly supported today:

- `get_drawing_dimensions`
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`

Temporarily withdrawn during redesign:

- `arrange_dimensions`
- `get_dimension_arrangement_debug`

Current read model already exposes:

- dimension set id and Tekla object type
- Tekla `dimensionType`
- `viewId` / `viewType`
- `viewScale`
- `distance`
- cheap summary `orientation`
- set `bounds`
- set `direction`
- set `topDirection`
- set `referenceLine`
- segment start/end points
- segment `bounds`
- segment `textBounds` (still conservative: `null`)
- segment `dimensionLine`
- segment `leadLineMain`
- segment `leadLineSecond`

Experimental validation helpers now exist:

- `draw_dimension_text_boxes`
- `get_dimension_text_placement_debug`

These are debug-only tools for live drawing validation and are not yet the
public `textBounds` contract.

Live validation already confirmed on the current assembly drawing:

- `viewScale` is read from the owning view
- a paper gap of `5` becomes a drawing gap of `125` at `viewScale = 25`
- spacing/debug now report both values explicitly

Live validation also confirmed an important current limitation:

- native dimension value text can be moved manually in Tekla
- the moved text position is not currently observable through the accessible
  Tekla Open API surface we have validated so far
- checked sources:
  - `StraightDimension.GetRelatedObjects()`
  - `StraightDimensionSet.GetRelatedObjects()`
  - recursive `GetObjects()` traversal where available
  - drawing presentation model text primitives
  - reflected public/nonpublic members on `StraightDimension`,
    `StraightDimensionSet` and related attributes
- consequence:
  - debug text polygons can only use runtime text objects when Tekla exposes
    them
  - otherwise they must fall back to synthetic placement

## Design Principles

- Do not invent public pseudo-types like `mixed_with_diagonals`.
- Do not use `angled` as a catch-all domain bucket.
- Treat `orientation` only as a cheap summary, not as the main grouping key.
- Build groups from:
  - same view
  - same Tekla `dimensionType`
  - parallel `direction`
  - compatible `topDirection`
  - compatible `referenceLine` / lead-line geometry
- Prefer line geometry over bbox for spacing and overlap reasoning.
- Target arrangement gaps in paper units first, then convert to drawing units via
  `viewScale`.
- Do not compare dimension text/line spacing directly in raw drawing units across
  different view scales.

## Phases

### Phase 1: Read Model Aligned with `dim`

Status: in progress.

Done:

- read API split into `Query / Commands / Arrangement`
- `get_drawing_dimensions` returns richer set-level and segment-level geometry
- compile-validated measured-value spike exists through `StraightDimension.Value.GetUnformattedString()`
- debug overlay path exists for text-polygon validation on live drawings
- synthetic text-polygon placement already imports several `dim` ideas:
  - frame size coefficients
  - format-aware measured text
  - side selection from `TopDirection` and placing direction
  - tag-line offsets
  - `dim`-style along-line fallback anchor

Still missing:

- runtime-validated text geometry for native dimension value text
- stronger extraction of domain type semantics from Tekla attributes where needed

Current finding:

- for ordinary native dimensions, the remaining mismatch is not just a formula
  issue
- the main blocker is that manual along-line drag of native dimension value
  text is not currently exposed by the validated Tekla API surface

### Phase 2: Internal Line-Based Grouping

Status: in progress.

`DimensionGroup` should represent a family of dimensions sharing:

- `ViewId`
- `ViewType`
- Tekla `DimensionType`
- parallel `Direction`
- compatible `TopDirection`
- overlapping / related `ReferenceLine` extents

`DimensionGroupMember` should carry:

- dimension id
- original distance
- reference line
- lead lines
- bounds as fallback only

### Phase 3: Line-Based Spacing / Debug

Status: foundation in progress, public debug withdrawn.

Spacing must be computed from:

- distance between parallel reference lines
- ordering of items along the dimension direction
- lead-line relations where needed
- requested paper gap multiplied by `viewScale` for the owning view

Debug output, when reintroduced, should explain:

- why dimensions were grouped together
- which line geometry drove spacing
- what move is considered safe
- when text geometry comes from runtime-observed text and when it comes from
  synthetic fallback

Not acceptable for public debug:

- internal invented classification labels
- bbox-only overlap heuristics presented as dimension semantics

### Phase 4: Safe Arrange Engine

Status: deferred until phases 1-3 are stable.

First release of the redesigned arrange engine should support only:

- parallel non-diagonal groups
- confirmed `Distance <-> move` mapping
- same-view operation
- paper-gap contract:
  - input gap is specified in paper units
  - effective drawing gap = `paperGap * viewScale`

Out of scope for first release:

- diagonal control dimensions
- broad catch-all handling of every remaining set
- cross-view/global orchestration

### Phase 5: Return Public Arrange Tools

Return `arrange_dimensions` and `get_dimension_arrangement_debug` only when:

- grouping is line-based
- spacing is line-based
- runtime move mapping is validated on live drawings
- debug output uses understandable drafting terminology

## Acceptance Criteria

The redesign is ready to expose again when:

- `get_drawing_dimensions` contains the geometry needed to reproduce the
  `dim` concepts without extra Tekla reads
- grouping no longer depends primarily on `orientation`
- spacing no longer depends primarily on bbox intervals
- target gap semantics are consistent with drawing text readability:
  paper gap in, drawing gap via `viewScale`
- current assembly drawings produce intuitive horizontal / vertical /
  diagonal-control cases without invented public labels
- line-based grouping and spacing are covered by unit tests and verified on a
  live drawing
- text geometry status is explicit:
  - runtime-observed when Tekla exposes native text objects/position
  - synthetic fallback when Tekla does not expose them
