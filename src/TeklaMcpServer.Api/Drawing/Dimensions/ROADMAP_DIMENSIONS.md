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
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`

Current read model is intentionally thin:

- dimension set id
- type
- `StraightDimensionSet.Distance`
- raw segment start/end points

This is enough for manual scripts and control diagonals, but not enough for
dimension arrangement algorithms.

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

## Phase 2: Debug / Inspection Tools

Before writing editing commands, add tools for observing dimensions.

Candidate tools:

- `get_dimension_debug`
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

## Phase 3: Atomic Editing Operations

Add manipulation APIs that do one thing each:

- `set_dimension_distance`
- `move_dimension_text`
- `set_dimension_attributes`
- `set_dimension_position` if Tekla allows direct repositioning
- recreate-helper for unsupported direct edits

Principle:

- prefer explicit atomic operations over one “smart” mutation command

## Phase 4: Arrangement Algorithms

Only after phases 1-3.

Candidate tools:

- `arrange_dimensions`
- `resolve_dimension_overlaps`

Expected first algorithm scope:

- same-view only
- straight dimensions only
- move by distance/offset first
- no cross-view orchestration initially

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

## Implementation Order

1. Expand DTOs for dimensions
2. Spike measured-value access for `StraightDimension` / `StraightDimensionSet`
3. Extend `GetDimensions()` to populate richer geometry/properties
4. Expose richer JSON through bridge/tool layer
5. Add internal `DimensionGroup` model
6. Add debug overlay for dimensions and groups
7. Add atomic edit commands
8. Add `arrange_dimensions`

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
