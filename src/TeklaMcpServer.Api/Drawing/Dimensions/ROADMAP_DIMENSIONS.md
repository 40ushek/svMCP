# Dimensions Roadmap

## Goal

Turn the current dimension API from a minimal wrapper around `StraightDimensionSet`
into a usable foundation for:

- richer dimension inspection
- controlled editing/manipulation
- future auto-layout and overlap-resolution tools

The implementation should stay inside `Drawing/Dimensions` unless a helper is
clearly reusable by other drawing modules.

## Current State

Implemented today:

- `get_drawing_dimensions`
- `get_dimension_arrangement_debug`
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`
- additive read-model expansion for `get_drawing_dimensions`
- internal `DimensionGroup` model
- internal spacing analysis
- internal arrangement planner
- internal `AxisShift -> DistanceDelta` translator for `horizontal/vertical`

Current read model already includes:

- dimension set id
- type
- `viewId`
- `viewType`
- `StraightDimensionSet.Distance`
- `orientation`
- dimension-set `bounds`
- segment start/end points
- segment `bounds`
- segment `textBounds` (currently `null` until Tekla text geometry is validated)

This is already enough to prototype grouping, overlap detection, spacing analysis
and distance-adjustment planning without extra Tekla reads.

## Placement In Codebase

Keep the whole track in `Drawing/Dimensions`.

Planned structure:

- `Drawing/Dimensions/TeklaDrawingDimensionsApi.cs`
  - may later be split into partials:
  - `TeklaDrawingDimensionsApi.Query.cs`
  - `TeklaDrawingDimensionsApi.Commands.cs`
  - `TeklaDrawingDimensionsApi.Arrangement.cs`
- `Drawing/Dimensions/*Info.cs`
  - DTO/read models
- `Drawing/Dimensions/*Result.cs`
  - command results
- `Drawing/Dimensions/*Geometry.cs`
  - dimension-specific geometry helpers if needed

Do not move this into `Drawing/Geometry` for now.
Dimension geometry is a consumer-oriented layer, not generic sheet geometry.

## Phase 1: Rich Read API

Status: substantially complete.

Extend `get_drawing_dimensions` first.

Target output per dimension set:

- `DimensionId`
- `ViewId`
- `ViewType`
- `Distance`
- dimension-set bbox in sheet/view coordinates
- orientation:
  - horizontal
  - vertical
  - angled
- terminology follow-up:
  - current `angled` is only a temporary catch-all for non-axis-aligned / composite dimension sets
  - rename it to a clearer public label such as `complex` or `composite`
  - avoid implying that these are necessarily true diagonal/sloped dimensions
- direction / normal vector if available
- segment start/end points
- segment bbox
- segment text bbox if available
- attributes/style file name if readable

Primary purpose:

- inspect real dimension geometry
- support debug overlays
- provide enough data for future arrangement algorithms

## Phase 1.5: Internal Grouping Model

Status: complete as internal foundation.

Before arrangement logic, introduce an internal dimension-group layer in
`Drawing/Dimensions`.

Purpose:

- group dimensions by view and orientation
- preserve direction / normal information
- hold a stable member order inside a group
- carry lead/reference line geometry
- expose group-level max distance / bounds for later algorithms

This is an internal model only:

- no new MCP tool
- no public JSON contract yet

Suggested internal shape:

- `DimensionGroup`
  - `ViewId`
  - `Orientation`
  - `Direction`
  - `Bounds`
  - `ReferenceLine`
  - `MaximumDistance`
  - `Members`
- `DimensionGroupMember`
  - dimension set info
  - sort key
  - member bounds

Primary operations:

- sort members in geometric order
- compute group bounds
- compute max distance band
- determine whether groups can align / combine

Implemented now:

- `DimensionGroup`
- `DimensionGroupMember`
- `DimensionReferenceLine`
- `DimensionGroupFactory`
- internal `GetDimensionGroups(int? viewId)` usage-path

## Phase 2: Debug / Inspection Tools

Status: partially started.

Before writing editing commands, add tools for observing dimensions.

Candidate tools:

- `get_dimension_debug`
- `get_dimension_arrangement_debug`
- `draw_dimension_debug_overlay`
- optional tool to show:
  - dimension line bbox
  - text bbox
  - anchor/reference points
  - spacing to neighboring dimensions

Reason:

- overlay is needed to validate the richer read model from Phase 1
- overlay will also make Phase 3 editing APIs much easier to verify
- overlay should also be able to visualize `DimensionGroup` / `ReferenceLine`

Implemented now:

- read-only `get_dimension_arrangement_debug`
- returns:
  - grouped dimensions by `viewId/viewType/orientation`
  - spacing analysis
  - current planner proposals
- useful for validating live drawings before enabling broader runtime moves

## Phase 3: Atomic Editing Operations

Status: partially prepared.

Add manipulation APIs that do one thing each:

- `set_dimension_distance`
- `move_dimension_text`
- `set_dimension_attributes`
- `set_dimension_position` if Tekla allows direct repositioning
- recreate-helper for unsupported direct edits

Principle:

- prefer explicit atomic operations over one “smart” mutation command

Prepared now:

- internal spacing analysis
- internal arrangement planner
- internal `AxisShift -> DistanceDelta` translation layer for `horizontal/vertical`

Still missing before public editing workflow:

- runtime apply-path over real `StraightDimensionSet.Distance`
- public command surface

Current public state:

- `arrange_dimensions` is exposed narrowly
- current runtime scope remains intentionally limited to `horizontal/vertical`
- non-axis-aligned / composite sets are still skipped
- live debug confirms the current assembly-drawing bottleneck is usually not lone `horizontal/vertical` dimensions
- the real unresolved runtime case is overlapping `complex/angled` groups

## Phase 4: Arrangement Algorithms

Status: foundation in progress.

Only after phases 1-3.

Candidate tools:

- `arrange_dimensions`
- `resolve_dimension_overlaps`

Expected first algorithm scope:

- same-view only
- straight dimensions only
- move by distance/offset first
- no cross-view orchestration initially

Implemented now:

- same-group spacing analysis with signed distances
- overlap detection (`Distance < 0`)
- sequential move planning with cumulative shift propagation
- live debug on real drawings shows:
  - lone `horizontal/vertical` groups often produce no proposals
  - the dense overlap problems are concentrated in the current `angled` catch-all bucket

Next step:

- add debug overlay for dimensions / groups
- expand arrangement beyond same-orientation-only groups where useful
- decide how `complex/angled` groups should be moved safely at runtime
- replace temporary `angled` label with clearer public terminology

Defer to a later roadmap item:

- `stack_dimensions`
- `align_dimension_chain`
- drafting-rule specific chain orchestration

## Phase 5: Higher-Level Automation

After the low-level API is stable:

- create dimensions from detected geometry rules
- recreate/repair broken dimension chains
- hybrid workflows:
  - read geometry
  - decide layout
  - create or move dimensions

## Suggested First DTO Expansion

`DrawingDimensionInfo` should likely grow in this direction:

- `Id`
- `Type`
- `ViewId`
- `ViewType`
- `Distance`
- `Orientation`
- `Bounds`
- `Segments`

`DimensionSegmentInfo` should likely grow in this direction:

- `Id`
- `StartX`
- `StartY`
- `EndX`
- `EndY`
- `Bounds`
- `TextBounds`
- `Value` only after a dedicated spike confirms Tekla exposes per-segment measured values reliably

Current spike status:

- direct access path is compile-validated via `StraightDimension.Value.GetUnformattedString()`
- public DTO is still intentionally unchanged until runtime behavior/formatting is validated

## Implementation Order

Done:

1. Expand DTOs for dimensions
2. Spike measured-value access for `StraightDimension`
3. Extend `GetDimensions()` to populate richer geometry/properties
4. Expose richer JSON through bridge/tool layer
5. Add internal `DimensionGroup` model
6. Add spacing analysis / planner / distance-translation foundation

Next:

7. Add debug overlay for dimensions and groups
8. Define safe runtime strategy for current `complex/angled` dimension sets
9. Expand `arrange_dimensions` beyond the first narrow runtime scope
10. Replace temporary `angled` naming with clearer public orientation/classification terminology

## Non-Goals For The First Iteration

- curved/radial dimension arrangement
- full drafting-rule engine
- automatic recreation of every broken dimension type
- cross-sheet/global optimization

## Acceptance Criteria For Phase 1

Phase 1 is complete when:

- `get_drawing_dimensions` returns `ViewId` and orientation
- dimension bbox is available and reliable enough for debug overlays
- segment text bbox is available when Tekla exposes it
- segment geometry is stable enough to compare dimensions spatially
- measured value is either implemented after the spike or explicitly omitted from DTOs for now
- output is sufficient to prototype overlap detection without extra Tekla calls
- internal data is sufficient to build `DimensionGroup` without more Tekla reads

Current assessment:

- all acceptance criteria above are effectively met except runtime-confirmed text geometry
- `measured value` is still intentionally omitted from DTOs, but direct access path is compile-validated
