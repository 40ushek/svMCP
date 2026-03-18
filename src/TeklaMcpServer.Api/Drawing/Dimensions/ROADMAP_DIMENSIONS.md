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

Live validation already confirmed on the current assembly drawing:

- `viewScale` is read from the owning view
- a paper gap of `5` becomes a drawing gap of `125` at `viewScale = 25`
- spacing/debug now report both values explicitly

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

Still missing:

- runtime-validated text geometry if Tekla exposes it reliably
- stronger extraction of domain type semantics from Tekla attributes where needed

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
