# Drawing Dimensions

## Verified writes (2026-09-19)

New work beyond the historical v1 baseline below:

- `create_dimension` and `recreate_dimension` now use a shared verified-write
  protocol. A replacement is committed and read back before the original is
  deleted. `CommitChanges()`, `Modify()` and `Delete()` false results stop the operation. A failed
  named attributes load stops before creation; unreadable original attributes
  are never silently replaced by defaults.
- Read-back checks owning view, unique XY points/segment count (0.01 mm match),
  direction/side, row type and offset. Running rows additionally require a unique
  connected segment path proving the requested first point. This is not visual
  collision checking or an exhaustive comparison of all style properties.
- `writeState` preserves the new ID, failed stage, observed offset and deletion
  status. Failure before original deletion triggers cleanup of the new set;
  cleanup is itself committed and verified. Failed cleanup or failure after
  original deletion starts may leave uncertain state. Inspect both IDs before
  retrying; the protocol is not atomic.
- Automatic `create_dimension` calculates a line 8 paper mm beyond the complete
  structural outline. `paperGapMm` overrides the gap; explicit `distance`
  retains manual view units without building geometry. The response includes
  `distanceUsed` and calculated `placement`.
- The persistent bridge shares a frozen `ViewDimensionContext` between
  `get_structural_chain_positions`, `get_view_dimension_context` and creation.
  Pass identical `excludePrefixes`/`excludeMaterials` for identical scope.
  Different normalized filters have separate snapshots. Dimensions do not
  invalidate them; external geometry/view edits require `refresh=true` on a
  query. Drawing/view switches and bridge restart discard the active run.
- `get_view_dimension_context` accepts `questions=points,edges,parts,scale`
  (or `all`) and `sides=Top,Bottom,Left,Right` (or `all`). Optional
  `placement` additionally needs flat XYZ `points`, `direction` and optional
  `paperGapMm`; it uses the creation calculator without writing.
- `writeState.RenderedLine` independently compares segment presentation lines:
  `matched`, `mismatch`, or `not verified` with a reason. A mismatch fails the
  existing verified-write protocol before deleting an original. Unsupported
  views/bases or unavailable presentation remain explicitly unverified; they
  do not block an otherwise verified write. This checks the created line, not
  neighbours or drafting sufficiency. Read-back `referenceLine` remains a
  calculation, now labelled `referenceLineSource`, not a presentation observation.
- These changes have automated coverage; new runtime observation and reuse
  still require live validation. See the active roadmap.
## Purpose

`Drawing/Dimensions` is the line-first dimension module for drawing runtime.

## Current Phase Status

Historical foundation status: `v1 complete`. The verified-write increment
above is later work and remains pending live validation.

This means:

- the current `Drawing/Dimensions` baseline is considered stable enough to stop
  expanding in this cycle
- the following v1 inventory is not a readiness claim for later increments
- new work follows the active roadmap gates rather than reopening completed v1 milestones

The current `v1` baseline already includes:

- internal `DimensionContext`
- internal `DrawingViewContext`
- internal `DimensionDecisionContext`
- internal `DimensionViewPlacementInfo`
- source association and point-to-object mapping
- explicit typed source identity via `DimensionSourceReference`
- debug-first `LayoutPolicy`
- `RecommendedAction`
- deterministic layout-policy evaluation through `DimensionDecisionContext`
- validated `combine` success path
- validated rollback/failure reporting path
- local post-combine arrange handoff
- arrangement planning using `DimensionDecisionContext` for view-scale-aware
  gap translation
- `PartsBounds` / `GridIds` in `DrawingViewContext`
- per-dimension `PartsBounds` placement classification and exact placement
  metrics
- narrow deterministic consumption of `PartsBounds` gap-policy signals during
  arrangement planning
- validated view-local part-geometry contract for both `SolidVertices` and
  `BboxMin` / `BboxMax`
- validated `ViewCoordinateSystem` as the accepted work-plane contract for the
  dimension / `PartsBounds` geometry path
- `DisplayCoordinateSystem` is rejected for this path because it risks mixing
  coordinate spaces between part geometry, dimensions, and debug overlays
- stable reread after mutate
- internal orchestration debug packets
- internal orchestration plan/preview packets

It is responsible for:

- reading existing `StraightDimensionSet` objects from the active drawing
- projecting Tekla runtime data into the internal dimension domain model
- grouping and reducing dimensions for analysis
- planning and applying spacing/offset adjustments
- creating, moving, deleting and combining dimensions
- control-diagonal placement
- text/debug support for dimension inspection

The canonical legacy reference for domain semantics is `D:\repos\svMCP\dim`.
That project is a reference for vocabulary and heuristics, not for direct
architectural copying.

## Current Module Shape

Facade/API files kept at the root of this folder:

- `TeklaDrawingDimensionsApi.cs`
- `TeklaDrawingDimensionsApi.Query.cs`
- `TeklaDrawingDimensionsApi.Commands.cs`
- `TeklaDrawingDimensionsApi.Arrangement.cs`
- `IDrawingDimensionsApi.cs`

Shared public DTO/value files kept at the root:

- `DrawingDimensionInfo.cs`
- `DimensionGeometryKind.cs`
- `DimensionSourceKind.cs`
- `DimensionType.cs`
- `CreateDimensionResult.cs`
- `PlaceControlDiagonalsResult.cs`

Internal layers:

- `Grouping/`
  - `DimensionItem`
  - `DimensionGroup`
  - `DimensionGroupFactory`
  - `DimensionOperations`
  - grouping/reduction policies and debug DTOs
- `Arrangement/`
  - spacing analysis
  - arrangement planning
  - distance-adjustment translation
  - arrangement debug/apply result types
- `Context/`
  - `DimensionContext`
  - `DrawingViewContext`
  - `DimensionDecisionContext`
  - geometry, placement, source-association, and layout-policy helpers
- `Orchestration/`
  - orchestration debug/result builders
  - internal action-plan / preview generation
- `Placement/`
  - projection helpers
  - placement heuristics
  - text placement and fallback polygon helpers
  - create-dimension placement helpers
  - control-diagonal placement helpers
  - text value formatting and text attribute mapping helpers

## Processing Flow

The current internal flow is:

`Tekla runtime snapshot -> domain model -> context/policy -> arrangement/orchestration -> placement/apply/debug`

In practical terms:

1. `Query` reads Tekla dimensions into internal snapshots and domain items.
2. `Grouping` turns runtime data into `DimensionItem` / `DimensionGroup`.
3. `Context` builds `DimensionContext`, `DrawingViewContext`, and
   `DimensionDecisionContext`.
4. `LayoutPolicy` and orchestration/debug paths evaluate explainable decisions
   from that shared context.
5. `Arrangement` analyzes stacks/gaps and plans `Distance` changes, including
   the current narrow `PartsBounds`-gap correction path.
6. `Placement` owns geometry/text placement helpers for create/debug support.
7. `Commands` and debug endpoints expose runtime actions and debug projections.

## Public MCP Tools Today

Exposed through `TeklaMcpServer/Tools`:

- `get_drawing_dimensions`
- `arrange_dimensions`
- `combine_dimensions`
- `move_dimension`
- `move_angle_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`
- `draw_dimension_text_boxes`

These are the supported public tool-surface operations for dimensions.
`combine_dimensions` is a separate controlled merge action layered on top of
the existing combine-candidate analysis; it is intentionally not part of
`arrange_dimensions`.
Its runtime result now also reports rollback status for partial-failure cases:
`rollbackAttempted`, `rollbackSucceeded`, and `rollbackReason`.
On successful non-preview merge it also performs a best-effort local
post-combine arrange handoff limited to the created dimension's local
stack/group in the same view/orientation. Handoff is reported separately via:
`arrangeHandoffAttempted`, `arrangeHandoffSucceeded`,
`arrangeHandoffReason`, and `arrangeHandoffAppliedDimensionIds`.
Combine success is not rolled back if the handoff is skipped or fails.
The merge commit and the handoff commit are intentionally independent:
handoff is not transactional with combine, and a handoff rollback failure may
still leave the drawing in a partially rearranged post-merge state.

Current default for `arrange_dimensions`:

- `targetGap = 8 mm` on paper (`DimensionPlacementSettings.DefaultPaperGapMm`)

Current arrangement semantics in practice:

- only parallel stack members are considered together
- the first surviving dimension in a stack acts as the fixed anchor
- later dimensions are moved relative to that anchor
- arrangement planning now uses a narrow `PartsBounds`-based first-chain anchor
  when the stack has consistent evaluated side/gap evidence
- the nearest chain on that side is anchored to the target gap from
  `PartsBounds`
- later chains in the same stack are then arranged sequentially from that
  anchored chain
- if the gap is smaller than target, the later dimension is pushed outward
- if the gap is larger than target, the later dimension may be pulled inward
- single-dimension stacks are left unchanged
- runtime apply still changes only `StraightDimensionSet.Distance`

Current `move_angle_dimension` limitation:

- `AngleTypes.AngleAtVertex` and `AngleTypes.AngleAtVertexGradian` are reported
  as not moved, with a reason.
- The Tekla-confirmed cause, the dead ends already checked and why `Origin`
  movement is not used as a fallback are recorded in
  [DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md).

## Internal / Bridge-Only Debug Surface

Available in `TeklaBridge`, but not currently surfaced as public MCP tools:

- `get_dimension_text_placement_debug`
- `get_dimension_source_debug`
- `get_dimension_groups_debug`
- `get_dimension_orchestration_debug`
- `get_dimension_action_plan` (alias: `get_dimension_ai_orchestration_plan`)
- `get_dimension_arrangement_debug`

Arrangement apply logic is publicly surfaced as `arrange_dimensions`, and
controlled dimension merging is publicly surfaced as `combine_dimensions`,
while arrangement debug remains bridge/internal only.

`get_dimension_action_plan` should be read narrowly:

- it is implemented today
- it is bridge/internal only
- it is not part of the public MCP tool surface
- it builds a debug-first action/recommendation plan
- it does not perform autonomous execution

Dimension reads/debug now use a bounded best-effort consistency retry after
runtime mutations. This is internal only: public payloads are unchanged, but
immediate rereads after `combine`, `create`, or `delete` should more reliably
see the fresh dimension state without requiring a separate sheet-debug path.

No additional runtime orchestration is part of the current phase:

- `RecommendedAction` stays debug-only
- orchestration packets stay debug-only
- the deterministic action plan stays debug-only
- there is no auto-apply based on policy recommendations

## Validated Findings

The following runtime facts are already confirmed and should be treated as
working constraints rather than open questions.

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- current public default for `arrange_dimensions` is `8 mm` paper gap
- `arrange_dimensions` has already been live-validated on real drawings:
  - a second run with the same target gap can be idempotent
  - push works when lines are too close
  - pull works when lines are too far apart
- `place_control_diagonals` has been live-validated on a real view using
  `SolidVertices`-driven hull/extreme-point selection
- `DrawingViewContext.PartsBounds` has been live-validated against debug
  overlay on a real view after restoring the view-local bbox contract
- `arrange_dimensions` has been live-validated with the `PartsBounds` anchor
  path enabled, including outward shifts relative to the overall parts box
- `arrange_dimensions` now treats `PartsBounds` as an exact-gap anchor for the
  nearest chain in the validated deterministic path
- for the validated part-geometry pipeline, `ViewCoordinateSystem` is the
  accepted runtime contract; `DisplayCoordinateSystem` should not be reused
  there
- negative `Distance` values occur on real drawings
- sign semantics for negative-distance dimensions remain a known risk area for
  future policy/layout work
- line-based grouping and spacing foundation already exists

## Coordinate-space contract

The source geometry reader in
[`TeklaDrawingPartGeometryApi.cs`](../Geometry/Parts/TeklaDrawingPartGeometryApi.cs)
sets the model work plane to the owning view's `ViewCoordinateSystem` before
reading the model solid. Therefore
`SolidVertices`, `BboxMin`, `BboxMax`, and the points exposed through
[`TeklaDrawingPartPointApi.cs`](../Geometry/Parts/TeklaDrawingPartPointApi.cs) are
already view-coordinate data. Dimension/source
matching must consume that path directly and must not apply a second
world-to-view transform.

The conversion from view-local coordinates to sheet coordinates is a separate
presentation operation (`view.Origin + local / scale`). It belongs to sheet
overlays and layout, not to associativity matching. Persisted dimension cases
must record the coordinate space and its source so that coordinates from
different spaces are never compared silently.

The drawing on which this contract was observed to fail, with the measured
axis spans, is recorded in
[DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md).

## Observed Constraint: Native Dimension Text Position

A manually moved dimension text position is not observable through the
validated Tekla API surface, so text geometry stays supporting/debug data. The
constraint, the list of API surfaces already checked and the consequences are
recorded once in
[DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md).

## Document Split

Use this file as the operational description of the module:

- current structure
- current responsibilities
- what is public vs internal

Use [ROADMAP_DIMENSIONS.md](ROADMAP_DIMENSIONS.md)
as the strategic document:

- what is still being aligned to `dim`
- what remains intentionally deferred
- what future functionality should be exposed later
