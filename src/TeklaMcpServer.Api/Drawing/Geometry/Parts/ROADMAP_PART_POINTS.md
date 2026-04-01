# Roadmap Part Points

## Goal

Introduce a canonical semantic point layer for drawing parts under
`Drawing/Geometry/Parts`.

This layer should answer not only "what raw geometry does the part have in the
view", but also "which semantic points of the part are useful for dimensions,
marks and section-related logic".

Target use cases:

- stable point sources for dimension creation
- reusable anchors for mark placement
- canonical extreme/reference points for diagnostics and future section logic

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Parts`

Reasoning:

- this is geometry extraction and derivation, not `Parts` query/info transport
- the result is broader than `Dimensions`; it should be reusable by
  `Dimensions`, `Marks` and later `Sections`
- this keeps raw part metadata separated from derived geometric semantics

Module split should stay explicit:

- `Drawing/Parts`
  - part query/info/read contracts
- `Drawing/Geometry`
  - raw geometry and shared geometric helpers
- `Drawing/Geometry/Parts`
  - part-specific semantic point extraction

## Current Base In Code

The module should build on top of existing code, not duplicate it.

Current sources already available:

- `TeklaDrawingPartGeometryApi`
  - raw part geometry in drawing/view-local coordinates
  - axis start/end
  - coordinate system origin
  - bbox min/max
- `TeklaDrawingDimensionsApi`
  - `PointList`-driven dimension workflows
  - convex hull / farthest-point selection patterns
- `Drawing/Parts`
  - part identity and user-facing read DTOs

This means the first version of `PartPoints` should be an adapter/derivation
layer over existing geometry, not a competing geometry API.

## Canonical Domain Direction

The target model should represent a part as a set of semantic point sources.

First-class point kinds should include:

- `AxisStart`
- `AxisEnd`
- `Origin`
- `Center`
- `BboxMin`
- `BboxMax`
- `Left`
- `Right`
- `Top`
- `Bottom`

The next semantic tier should include:

- `ExtremePoints`
- `ContactFace`
- `BoltPoints`
- `MainPartReference`

Not every kind has to be available in the first implementation, but the model
should be designed so these kinds fit naturally without redesign.

## Design Principles

- Keep view-local coordinates canonical.
- Reuse `TeklaDrawingPartGeometryApi` as the raw source of truth.
- Separate raw geometry from derived semantic points.
- Keep point extraction independent from dimension placement policy.
- Keep point extraction independent from mark placement policy.
- Prefer explicit point kinds over anonymous point bags.
- Prefer stable and explainable derivation rules over heuristic-only output.

## Explicit Non-Goals

This module should not become:

- a replacement for `Drawing/Parts`
- a general-purpose solid analysis engine
- a dimension arrangement module
- a mark placement module

Those modules may consume part points, but the point layer itself should stay
focused on extraction and normalization.

## Proposed Types

Initial target surface:

- `DrawingPartPointKind`
- `DrawingPartPointInfo`
- `GetPartPointsResult`
- `IDrawingPartPointApi`
- `TeklaDrawingPartPointApi`

Expected responsibilities:

- `DrawingPartPointKind`
  - domain vocabulary for semantic point types
- `DrawingPartPointInfo`
  - one point with kind, coordinates and traceable origin/metadata
- `GetPartPointsResult`
  - grouped result for one part in one drawing view context
- `IDrawingPartPointApi`
  - stable API boundary for consumers
- `TeklaDrawingPartPointApi`
  - Tekla-backed implementation using current geometry APIs

## Phases

### Phase 1: Folder And Domain Boundary

Status: planned.

Done when:

- `Drawing/Geometry/Parts` exists as the agreed home for this work
- roadmap and naming make the module boundary explicit
- consumers can reference a stable intended location for future work

### Phase 2: Raw Semantic Points From Existing Geometry

Status: planned.

Implement the first usable point API on top of existing geometry.

Minimum output:

- `AxisStart`
- `AxisEnd`
- `Origin`
- `Center`
- `BboxMin`
- `BboxMax`

Done when:

- one API call can return the canonical basic points for a drawing part
- all returned coordinates are in view-local coordinates
- no duplication of raw geometry reading logic is introduced

### Phase 3: Derived Edge And Extreme Points

Status: planned.

Extend the point layer with direction-aware or bbox-derived semantic points.

Target additions:

- `Left`
- `Right`
- `Top`
- `Bottom`
- `ExtremePoints`

Done when:

- dimension tools can consume canonical directional/extreme points without
  recomputing them ad hoc
- derivation rules are documented and predictable

### Phase 4: Connection-Aware Points

Status: planned.

Introduce connection-related point sources where Tekla drawing/runtime data
allows it.

Target additions:

- `BoltPoints`
- `ContactFace`
- possible assembly/main-part reference points

Done when:

- the module can support bolt-aware dimension scenarios
- connection-aware points are exposed without mixing them into unrelated DTOs

### Phase 5: Consumer Integration

Status: planned.

Adopt the new point layer in modules that currently derive points ad hoc.

Priority order:

- `Dimensions`
- `Marks`
- later `Views` / section-related helpers where justified

Done when:

- dimension creation can consume canonical part point sources
- mark placement can reuse the same point vocabulary
- point derivation logic is no longer duplicated across consumers

## Validation

Three levels of validation are needed.

### Unit-Level

- kind mapping is stable
- center/bbox/axis point derivation is deterministic
- missing geometry degrades predictably

### Geometry-Level

- returned points stay in view-local coordinates
- left/right/top/bottom semantics are consistent for rotated drawing views
- extreme point selection is stable under repeated reads

### Live Tekla Validation

- basic points match visible drawing geometry
- dimension creation from returned points behaves as expected
- mark anchors derived from the same points are visually sensible

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Parts` is the single obvious home for part semantic points
- basic part points are available without duplicating geometry readers
- dimensions can consume canonical part points
- marks can consume the same point vocabulary
- future bolt/contact-aware scenarios fit into the same model without redesign

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. add the target types
2. implement basic point extraction over `TeklaDrawingPartGeometryApi`
3. wire one dimension scenario to the new point API

Only after that should bolt/contact-aware point logic be added.
