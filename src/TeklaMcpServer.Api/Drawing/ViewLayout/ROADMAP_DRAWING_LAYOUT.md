# Roadmap Drawing Layout

## Goal

Make drawing view composition use one lightweight, connected layout context
instead of scattered runtime `View` lists, DTOs, and local dictionaries.

This roadmap is the active source of truth for sheet/view composition:

- `fit_views_to_sheet`
- view scale selection
- view rectangles and reserved areas
- base/projected/section/detail relations
- projection alignment
- layout scoring and before/after layout cases

Algorithm history and already implemented behavior are kept in
`ROADMAP_VIEWS.md`.

## Context Model

### DrawingContext

`DrawingContext` is the coarse sheet-level source for layout.

It should contain:

- drawing identity and type
- sheet width/height and margins
- lightweight layout views
- reserved table/title-block zones
- warnings and diagnostics

It must not contain heavy per-view geometry such as all parts, bolts, solid
vertices, mark geometry, dimension geometry, or part hulls.

### DrawingLayoutViewItem

`DrawingLayoutViewItem` is the lightweight view item used by layout.

It is effectively a layout-facing wrapper around the current coarse view facts:

- view id, type, semantic kind, name
- scale
- origin
- width/height
- frame/bbox rectangle
- frame offset, when known
- base/neighbor/section/detail role
- placement side and fallback diagnostics

It should be cheap to build for every view on the sheet.

### DrawingLayoutWorkspace

`DrawingLayoutWorkspace` is the temporary working area for one drawing-layout
operation.

It is built from `DrawingContext`, enriched with calculated layout facts, and
discarded after the operation. It is not a new source of truth.

It can hold:

- `DrawingLayoutViewItem` list and lookup by view id
- runtime `View` handles needed only for apply/probe operations
- current/candidate frame sizes and frame offsets
- topology and relation lookup
- arranged positions
- diagnostics
- optional `DrawingProjectionContext`

### DrawingProjectionContext

`DrawingProjectionContext` is a lazy add-on used only when projection alignment
needs extra signals.

It may contain:

- GA grid axes as `Guid/Label/Direction/Coordinate`
- assembly/single-part local anchors for selected model ids
- section placement side / alignment axis

It should not force full `DrawingViewContext` construction.

### DrawingViewContext

`DrawingViewContext` stays the detailed per-view geometry context for
dimensions and marks.

It contains heavier facts:

- parts
- bolts
- `PartsBounds`
- `PartsHull`
- grid ids
- detailed view-local warnings

Normal drawing layout must not pay the cost of building it.

## Current State

Already present:

- `DrawingContext`
- `DrawingLayoutContextBuilder`
- `DrawingViewContext`
- `DrawingViewContextBuilder`
- `DrawingLayoutScorer`
- `DrawingCaseCaptureService`
- `DrawingCaseSnapshotWriter`
- `ViewTopologyGraph`
- `ViewPlacementValidator`
- `DrawingProjectionAlignmentService`

Current gap:

- `fit_views_to_sheet` builds/reads `DrawingContext`, but orchestration still
  uses runtime `View` lists plus local dictionaries for semantics, frame sizes,
  offsets, topology, arranged positions, and projection signals.

## Phases

### Phase 1. Define Lightweight Layout Workspace

- Add `DrawingLayoutViewItem`.
- Add `DrawingLayoutWorkspace`.
- Populate them from `DrawingContext` and existing runtime view facts.
- Keep behavior unchanged.
- Add tests for scale, rect, semantic kind, reserved areas, and lookup parity.

### Phase 2. Move Existing Lookup State Into Planning Context

Move these scattered structures behind the planning context:

- `semanticKindById`
- selected frame sizes
- actual frame rects
- frame offsets
- arranged position lookup
- base/neighbor/section/detail relation lookup

No layout policy changes in this phase.

### Phase 3. Make Projection Alignment Context-Aware

- Introduce lazy `DrawingProjectionContext`.
- Reuse planning view rects and frame offsets for collision checks.
- Load GA grid axes only when GA projection alignment needs them.
- Load assembly/single-part anchors only for the model id being aligned.
- Keep full `DrawingViewContext` out of projection alignment.

### Phase 4. Refactor `fit_views_to_sheet` Orchestration

- Make scale selection consume layout context facts.
- Make arrange/diagnostics consume planning context facts.
- Keep runtime `View` objects as apply handles only.
- Preserve public MCP/bridge result shape.

### Phase 5. Analytical Layout Follow-up

After the context migration is stable:

- evaluate analytical scale/layout probing before Tekla `CommitChanges`
- compare candidate layouts with `DrawingLayoutScorer`
- keep before/after `DrawingContext` cases as the canonical dataset format

## Non-Goals

- Do not rewrite layout policy while introducing context.
- Do not load parts/bolts/hulls for normal drawing layout.
- Do not put detailed mark/dimension geometry into `DrawingContext`.
- Do not replace table marker-based reserved-area reading.
- Do not change public tool contracts unless explicitly planned.

## Validation Baseline

Before and during the context migration, keep a small live-validation baseline
for `fit_views_to_sheet` behavior:

- assembly drawings with standard projected neighbors
- sheets with `Top` / `Bottom` sections
- sheets with `Left` / `Right` sections
- drawings with detail views and `DetailMark` anchors
- drawings with detail-like sections and `SectionMark` anchors
- GA drawings that need grid-axis projection alignment
- repeated `fit_views_to_sheet` on the same drawing to check stability
- reserved table/title-block avoidance

## Acceptance Criteria

- There is one clear active roadmap for drawing layout.
- `DrawingContext` remains the sheet-level source.
- `DrawingLayoutWorkspace` is temporary and not a source of truth.
- `DrawingLayoutViewItem` is cheap and layout-specific.
- `DrawingViewContext` remains reserved for dimensions/marks.
- `fit_views_to_sheet` behavior remains stable during context migration.
- Projection alignment gets only the lightweight geometry it needs.
