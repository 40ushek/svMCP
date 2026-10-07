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
- The context also retains detached solid data for successfully read parts:
  bbox, vertices, faces/loops and view hull. `GetPartSolidGeometry(modelId)`
  returns a deep copy. Excluded or unread parts retain their separate role and
  diagnostic records. Memory use grows with the captured solid geometry.
- In the new source increment, `get_view_dimension_context` defaults to
  `questions=chain,scale`; allowed questions are `chain`, `chainDetails`,
  `parts`, `scale`, `diagnostics`. Source-point, edge, placement and contact
  questions are rejected in both MCP and bridge before reading geometry.
  Internal context queries and other geometry readers remain available.
- `writeState.RenderedLine` independently compares segment presentation lines:
  `matched`, `mismatch`, or `not verified` with a reason. A mismatch fails the
  existing verified-write protocol before deleting an original. Unsupported
  views/bases or unavailable presentation remain explicitly unverified; they
  do not block an otherwise verified write. This checks the created line, not
  neighbours or drafting sufficiency. Read-back `referenceLine` remains a
  calculation, now labelled `referenceLineSource`, not a presentation observation.
- These changes have automated coverage; new runtime observation and reuse
  still require live validation. See the active roadmap.
## Provisional bolt-chain preview (2026-10-02)

`get_view_dimension_context(questions="boltChains,scale")` returns a separate
`boltChainPreview` from the frozen bolt snapshot. It proposes internal-spacing
rows/columns for axis-aligned, constant-depth `BoltArray` projections with a full
Cartesian pattern. A single row/column and unequal spacings are supported.
Skewed, staggered, incomplete, circular and depth-spanning patterns are refused
with reasons. Coordinates are clustered within 0.01 model/view units, independent
of view scale; merged points retain all original source indices.

`sides` filters axes only (Top/Bottom = X, Left/Right = Y), without duplicating
mirror-side chains or choosing placement. Every proposal retains ordered XYZ,
source indices, adjacent projected distances and a geometric start index.
Outside/unresolved center evidence blocks the whole affected chain instead of
silently shortening it. Related part candidates are reported; shared-group
ownership is not inferred. Read completeness, selection and visibility are separate.

All proposals expose `creationMode="explicitSelectionOnly"`: Candidate proposals can be created by explicit selection; Blocked proposals cannot. `proposalId` is not a structural batch preview key or a writable point
ID. Bolt-plane orientation, final selection, part policy, placement, edge-dimension policy
and group-position chains remain pending. The default structural preview and
its creation path are unchanged; `all` does not include bolt proposals.

The second increment adds `groups[].edgeChains`: separate min/max edge-distance
proposals for each retained row/column and each included related part, including
single-center rows. Every entry carries `partId`, original bolt indices, center,
projected edge point, distance and contour provenance. No part ownership or edge
policy is inferred when a group connects several parts.

The context freezes the existing per-part projected outline; no extra Tekla read
is made. Axis rays meet the nearest crossing of the containing outer contour.
Concave boundaries and chamfers are preserved; holes are not used as outer edges.
Missing/ambiguous contours, centers outside the contour, vertex-aligned rays and
failed restriction evidence return `Blocked` with a reason. Full-solid projection
is explicitly not verified as a depth-clipped section contour. These proposals
are separate from structural preview references; see explicit bolt-chain creation below.

Live read-only checks on M.81 views 6110, 1409 and 2814 produced 60 mm spacing
proposals and a front-view row with six 453.33 mm spacings. The same row in 6110
remained blocked by restriction evidence. Depth-spanning groups were refused.

Live edge-preview checks on M.81 views 6110, 1409 and 2814: Section E (1409)
returned part-specific proposals of 20, 30/30.001, 50 and 126 mm for groups
10783887/10893930. Other proposals remained blocked by restriction or contour
containment evidence. These are full-solid projected candidates, not verified
section edges or created dimensions.

Bolt previews also expose `partChains`: one side-specific chain per included related
part, from its actual outer contour through all distinct projected bolt coordinates
to the opposite contour. Sources retain both model bolt-group ID and position index;
coincident axis coordinates merge across groups. Select the proposal explicitly with
`boltProposal`, `partId` and the matching placement direction. Restriction failures block the whole part chain; `blockedSources` lists each problematic bolt by group ID, position index and reason. Visibility and section clipping remain unverified.

## Explicit bolt-chain creation (2026-10-02)

After reviewing `boltChainPreview`, submit a Candidate proposal independently of
structural part chains, using the same `contextId`:

```json
[{"boltProposal":"bolt-42-X-0","partId":10,"direction":"horizontal-down","distance":80}]
```

`boltProposal` is the exact `proposalId` of an internal, part-edge or combined part-chain proposal.
`partId` explicitly selects an included related part (and must match the edge
proposal's part). `direction` chooses the placement side and must match the
proposal's X/Y axis (and exact placement side for `partChains`). `key` defaults to `boltProposal`. Attributes, dimension type
and offsets retain the existing batch behavior. Bolt entries cannot also supply
`preview` or `pointIds`; separate structural and bolt entries can share a batch.
Unknown/blocked proposals, wrong parts/axes, duplicate selections and expired
contexts stop before writes. Previews are cached per normalized side set for the lifetime of the context. Coordinates are resolved from the unrounded frozen
snapshot, not copied from the displayed JSON or the structural point catalog.

The ordinary verified dimension writer, offset/occupied-side checks and final
read-back are reused. `BoltGeometryVerification` preserves that bolt-plane
orientation, visibility and section clipping remain unverified. The preview exposes `creationMode="explicitSelectionOnly"`. Automatic policy selection, group-position chains and occlusion remain deferred.

Creation has automated coverage and live read-back checks on M.81 Section G (1908) and E (1409). Rendered offsets and chain coordinates were checked; occlusion and depth-clipped section contours remain unverified.

## Prepared-chain batch increment (2026-10-02, not deployed)

Previews return ordered `pointIds`, segments and incompleteness reasons; they
do not prescribe a row. The assistant keeps or removes prepared chains and
submits the retained selection with the same `contextId`.

On an empty side, `create_dimensions_batch` assigns rows 1, 2, 3, ... in submission
order, independently per side. A lone overall uses row 1. The gap is row number
x `DimensionPlacementSettings.DefaultPaperGapMm` (currently 8 paper mm).
The batch ignores the former `row` input. Explicit `paperGapMm` or `distance`
takes precedence; supplying both remains invalid.

A repeated batch without offsets retains a unique existing match by points,
side and requested dimension type at its current distance. Ambiguous matches
require an explicit offset. A new chain on an occupied side also requires an
explicit offset; the entire batch is checked before any write. No free-space
search or text-collision detection is added.

Single `create_dimension` retains its positive integer `row` contract and
explicit-offset precedence. The previous numeric-row release was deployed on
2026-10-02; this prepared-chain increment has automated coverage but still
requires deployment and live placement/performance validation.

## Panel reference placement scope (2026-10-07, source implemented)

With selected reference geometry, pass `ruleSet="panel"` to both
`create_dimensions_batch` and single `create_dimension` calls using `contextId`
and `pointIds`. Explicit steel chains use `ruleSet="steel"`. Omitting the rule set
in such a context is rejected before writing, including with an explicit distance;
point resolution and placement share the same declared scope. Older calls without
selected reference geometry retain their default measured-part scope.

Panel overall dimensions use the combined MinX/MaxX and MinY/MaxY bounding box of
the measured layer and its selected reference components. Actual extreme contour
vertices remain addressable even when they are on the opposite side of the panel.
Disjoint neighbouring reference components do not enlarge this box. Live acceptance
of this placement change is still pending.

## Preview references in batch (2026-10-02, source implemented; not deployed)

Each preview chain now exposes `key`, `direction` and default `attributesFile`.
`create_dimensions_batch` accepts an entry such as `{"preview":"Top-location"}`
instead of repeating ordered point IDs. Pass the same `contextId`, `ruleSet`
(`steel` or `panel`) and `chainView` (`chain` or `chainDetails`) as the preview read.
Section `chain` uses consolidated keys such as `Top-chain`; `chainDetails` uses
split profile/location keys. Only the listed chains are submitted, in list order.

References may specify `key`, `paperGapMm`, `distance`, `attributesFile` and
`dimensionType`. Without an attributes override, overall uses `overall` and other
chains use `standard`. References cannot include `pointIds` or override direction.
Unknown/empty proposals, duplicate references and expired contexts fail before
writes. Explicit `pointIds` entries and legacy bridge arguments remain supported,
including mixed batches. Existing row, occupied-side, retention and read-back
checks run after reference resolution. No per-point removal is implemented.

Deployment and live same-view performance validation remain pending.

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
