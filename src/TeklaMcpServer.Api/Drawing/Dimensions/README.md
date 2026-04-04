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
- source association and point-to-object mapping
- debug-first `LayoutPolicy`
- `RecommendedAction`
- validated `combine` success path
- validated rollback/failure reporting path
- local post-combine arrange handoff
- stable reread after mutate
- internal orchestration debug packets

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
- `Placement/`
  - projection helpers
  - placement heuristics
  - text placement and fallback polygon helpers
  - create-dimension placement helpers
  - control-diagonal placement helpers
  - text value formatting and text attribute mapping helpers

## Processing Flow

The current internal flow is:

`Tekla runtime snapshot -> grouping/reduction -> arrangement planning -> placement math -> commands/debug`

In practical terms:

1. `Query` reads dimensions and view ownership from Tekla.
2. `Grouping` turns runtime data into `DimensionItem` / `DimensionGroup`.
3. `Arrangement` analyzes stacks/gaps and plans `Distance` changes.
4. `Placement` owns geometry/text placement helpers.
5. `Commands` and debug endpoints expose runtime actions.

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

Dimension reads/debug now use a bounded best-effort consistency retry after
runtime mutations. This is internal only: public payloads are unchanged, but
immediate rereads after `combine`, `create`, or `delete` should more reliably
see the fresh dimension state without requiring a separate sheet-debug path.

No additional runtime orchestration is part of the current phase:

- `RecommendedAction` stays debug-only
- orchestration packets stay debug-only
- AI-assisted orchestration plan stays debug-only
- there is no auto-apply based on policy recommendations

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
