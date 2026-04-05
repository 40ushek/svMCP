# Drawing Dimensions

## Purpose

`Drawing/Dimensions` is the line-first dimension module for drawing runtime.

## Current Phase Status

Current phase status: `v1 complete`.

This means:

- the current `Drawing/Dimensions` baseline is considered stable enough to stop
  expanding in this cycle
- no new behavior changes are intended inside the current phase
- remaining improvements move to the next phase as backlog items

The current `v1` baseline already includes:

- internal `DimensionContext`
- internal `DimensionViewContext`
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
- `PartsBounds` / `PartsHull` / `GridIds` in `DimensionViewContext`
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
  - `DimensionViewContext`
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
3. `Context` builds `DimensionContext`, `DimensionViewContext`, and
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

- `targetGap = 10 mm` on paper

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

## Internal / Bridge-Only Debug Surface

Available in `TeklaBridge`, but not currently surfaced as public MCP tools:

- `get_dimension_text_placement_debug`
- `get_dimension_source_debug`
- `get_dimension_groups_debug`
- `get_dimension_orchestration_debug`
- `get_dimension_ai_orchestration_plan`
- `get_dimension_arrangement_debug`

Arrangement apply logic is publicly surfaced as `arrange_dimensions`, and
controlled dimension merging is publicly surfaced as `combine_dimensions`,
while arrangement debug remains bridge/internal only.

`get_dimension_ai_orchestration_plan` should be read narrowly:

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
- AI-assisted orchestration plan stays debug-only
- there is no auto-apply based on policy recommendations

## Validated Findings

The following runtime facts are already confirmed and should be treated as
working constraints rather than open questions.

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- current public default for `arrange_dimensions` is `10 mm` paper gap
- `arrange_dimensions` has already been live-validated on real drawings:
  - a second run with the same target gap can be idempotent
  - push works when lines are too close
  - pull works when lines are too far apart
- `place_control_diagonals` has been live-validated on a real view using
  `SolidVertices`-driven hull/extreme-point selection
- `DimensionViewContext.PartsBounds` has been live-validated against debug
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

## Observed Constraint: Native Dimension Text Position

The following limitation is already confirmed on the validated Tekla API
surface currently available to this module.

- native dimension value text can be moved manually in Tekla
- the moved text position is not currently observable through the checked Tekla
  Open API surface

Checked sources so far:

- `StraightDimension.GetRelatedObjects()`
- `StraightDimensionSet.GetRelatedObjects()`
- recursive `GetObjects()` traversal where available
- drawing presentation model text primitives
- reflected public/nonpublic members on `StraightDimension`,
  `StraightDimensionSet` and related attributes

Consequence:

- text polygon debug may use runtime text geometry when Tekla exposes it
- otherwise text geometry remains synthetic fallback
- this constraint should not distort the core domain redesign

## Document Split

Use this file as the operational description of the module:

- current structure
- current responsibilities
- what is public vs internal

Use [ROADMAP_DIMENSIONS.md](D:\repos\svMCP\src\TeklaMcpServer.Api\Drawing\Dimensions\ROADMAP_DIMENSIONS.md)
as the strategic document:

- what is still being aligned to `dim`
- what remains intentionally deferred
- what future functionality should be exposed later
