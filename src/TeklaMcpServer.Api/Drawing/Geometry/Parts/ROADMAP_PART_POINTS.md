# Roadmap Part Geometry

## Goal

Introduce a canonical geometry library for drawing parts under
`Drawing/Geometry/Parts`.

The primary goal is to expose raw part geometry in drawing/view-local
coordinates first, and only then derive semantic points from it.

Current priority order:

- raw solid geometry of a part in the drawing view
- face / loop / vertex topology in view-local coordinates
- reusable derived points built on top of that geometry

Not in scope for the current stage:

- MCP transport design
- bridge command design
- contact / connection geometry
- dimension or mark behavior

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Parts`

Reasoning:

- this is geometry extraction and derivation, not `Parts` query/info transport
- raw geometry should exist independently from downstream point or dimension
  consumers
- this keeps low-level geometry separate from higher-level semantic point logic

Module split should stay explicit:

- `Drawing/Parts`
  - part query/info/read contracts
- `Drawing/Geometry`
  - raw geometry and shared geometric helpers
- `Drawing/Geometry/Parts`
  - part-specific geometry and derived geometry
- `Drawing/Geometry/Bolts`
  - bolt-specific geometry and bolt-to-part relations

## Current Base In Code

The module should build on top of existing code, not duplicate it.

Current sources already available:

- `TeklaDrawingPartGeometryApi`
  - raw part geometry in drawing/view-local coordinates
  - axis start/end
  - coordinate system origin
  - bbox min/max
- `TeklaDrawingPartSolidGeometryApi`
  - raw solid geometry in drawing/view-local coordinates
  - faces
  - loops
  - vertices
- shared geometry helpers
  - convex hull
  - farthest-point pair
- `Drawing/Parts`
  - part identity and user-facing read DTOs

This means `PartPoints` is no longer the first layer. The first layer is raw
geometry, and points are a derived layer built on top of it.

## Canonical Domain Direction

The target model should represent a part in two explicit layers:

### Layer 1: Raw Geometry

The raw geometry layer should expose:

- part axis / coordinate system
- solid bbox
- solid vertices
- face list
- loop list inside each face
- stable vertex indexing for topology traversal

### Layer 2: Derived Geometry

The derived layer should expose:

- semantic points
- hull / outline candidates
- exact projected outline later
- extreme points
- later contact-related or connection-related geometry

The point layer should not be limited to one geometry origin. It should still
be able to represent several source families used by drawing workflows:

- `Axis`
- `Part`
- `Assembly`
- `Node`
- `Connection`

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

## Raw Geometry First

The canonical source of truth must be raw geometry, not pre-baked point bags.

At the current stage the most important geometry contracts are:

- `PartSolidGeometry`
- `PartFaceGeometry`
- `PartLoopGeometry`
- `PartVertexGeometry`

These contracts should be sufficient to:

- inspect the real solid shape of a part in the current view plane
- derive hull/extreme points without re-reading Tekla runtime objects
- later build projected contour/outline helpers
- later compute contacts from already extracted geometry

## Point Source Taxonomy

Semantic point provenance still matters, but it is secondary to the raw
geometry layer.

The intended source taxonomy for the derived layer is:

- `Axis`
- `Part`
- later `Assembly`
- later `Node`
- later `Connection`

This taxonomy belongs to derived geometry, not the raw solid contracts.

## Geometry Rules To Preserve

The module should preserve a few core geometry rules from practical drawing
workflows.

- The canonical output is reusable geometry data, not a one-off helper return.
- Point coordinates should be emitted in view-local coordinates.
- Solid topology should be reusable without touching transport layers.
- Derived points must be computed from extracted geometry, not directly from
  Tekla runtime calls in every consumer.
- Contacts are deferred, but when added later they must be computed from
  already extracted geometry.
- Derived points must not replace the raw solid geometry layer.

## Practical Consumer Scenarios

The roadmap should explicitly support these scenarios.

- Read a part solid in drawing/view-local coordinates.
- Traverse faces and loops without re-querying Tekla per downstream scenario.
- Reuse the same extracted geometry to compute hull and extreme points.
- Later project face loops into 2D and build an exact outline from polygon union.
- Build a semantic point layer later on top of the same geometry.

## Design Principles

- Keep view-local coordinates canonical.
- Reuse `TeklaDrawingPartGeometryApi` as the raw source of truth.
- Separate raw geometry from derived semantic points.
- Keep the current stage independent from MCP and bridge transport concerns.
- Prefer stable topology contracts over ad hoc lists of doubles.
- Prefer explicit geometry models over anonymous point bags.

## Explicit Non-Goals

This module should not become:

- a replacement for `Drawing/Parts`
- a dimension arrangement module
- a mark placement module
- an MCP tool surface at the current stage

Those modules may consume part geometry later, but the current stage should
stay focused on extraction and normalization.

## Proposed Types

Current raw geometry surface:

- `PartSolidGeometry`
- `PartFaceGeometry`
- `PartLoopGeometry`
- `PartVertexGeometry`
- `PartSolidGeometryInViewResult`
- `IDrawingPartSolidGeometryApi`
- `TeklaDrawingPartSolidGeometryApi`

Derived geometry surface after that:

- `DrawingPartPointKind`
- `DrawingPartPointInfo`
- `GetPartPointsResult`
- `IDrawingPartPointApi`
- `TeklaDrawingPartPointApi`

Expected responsibilities:

- `PartVertexGeometry`
  - one indexed solid vertex in view-local coordinates
- `PartLoopGeometry`
  - one face loop as an ordered list of vertex indexes
- `PartFaceGeometry`
  - one solid face with normal and loops
- `PartSolidGeometry`
  - full part solid geometry in one view context
- `PartSolidGeometryInViewResult`
  - read contract for one part in one view
- `IDrawingPartSolidGeometryApi`
  - stable raw-geometry boundary
- `TeklaDrawingPartSolidGeometryApi`
  - Tekla-backed raw solid extraction

Derived layer responsibilities:

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

Status: done.

Done when:

- `Drawing/Geometry/Parts` exists as the agreed home for this work
- roadmap and naming make the module boundary explicit
- consumers can reference a stable intended location for future work

### Phase 2: Raw Part Geometry In View

Status: done in first form.

Implement the first reusable raw geometry API in view-local coordinates.

Minimum output:

- `StartPoint`
- `EndPoint`
- `CoordinateSystemOrigin`
- `AxisX`
- `AxisY`
- `AxisStart`
- `AxisEnd`
- `BboxMin`
- `BboxMax`
- `SolidVertices`

Done when:

- one API call can return canonical raw geometry for a drawing part
- all returned coordinates are in view-local coordinates
- no duplication of raw geometry reading logic is introduced

### Phase 3: Raw Solid Topology

Status: done in first form.

Add reusable solid topology contracts on top of the same view-local solid read.

Target additions:

- `PartSolidGeometry`
- `PartFaceGeometry`
- `PartLoopGeometry`
- `PartVertexGeometry`

Done when:

- faces and loops can be traversed without new transport concerns
- topology is stable enough for later outline/hull/contact logic

### Phase 4: Derived Point Layer

Status: planned.

Introduce semantic points as a derived layer over raw geometry.

Target additions:

- `AxisMidpoint`
- bbox corners
- side midpoints
- hull vertices
- extreme points

Done when:

- derived points are computed from already extracted geometry
- downstream consumers no longer need ad hoc hull/extreme calculations

### Phase 5: Outline And Contour Geometry

Status: deferred for now.

The exact contour of a part should not be modeled as a plain convex hull.

Target direction:

- project face loops into the drawing/view plane
- build projected face polygons
- run polygon union over those projected polygons
- expose outer contour and inner holes separately

Important rule:

- `convex hull` is acceptable as a coarse helper or fallback
- `convex hull` is not the target implementation for exact part contour

Done when:

- the library can expose a projected outer contour of the part
- inner holes/loops can be represented separately when needed
- outline logic is built from raw solid topology rather than bbox shortcuts

### Phase 6: Contacts And Connection Geometry

Status: planned.

Contacts stay explicitly out of the current stage.

Future target additions:

- `ContactFace`
- later assembly/node-aware geometry

Done when:

- contacts are computed from extracted part geometry rather than from ad hoc
  runtime lookups
- connection-aware geometry is clearly separated from raw solid topology

## Validation

Three levels of validation are needed.

### Unit-Level

- face/loop/vertex indexing is stable
- bbox/axis/solid extraction is deterministic
- missing geometry degrades predictably

### Geometry-Level

- returned geometry stays in view-local coordinates
- loop vertex references are valid
- hull/extreme derivation is stable under repeated reads

### Live Tekla Validation

- raw solid faces and loops match visible part geometry
- solid vertices are reusable for later derived-point calculations

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Parts` is the single obvious home for part geometry
- raw part geometry is available without duplicating geometry readers
- raw solid topology is available as a library contract
- exact outline can later be added without redesigning the raw geometry layer
- derived points can be built on top of the same geometry model
- future contact geometry fits into the same model without redesign

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. keep extending raw geometry contracts where needed
2. add hull helpers on top of raw solid geometry
3. later add projected outline helpers via polygon union
4. only after that continue the derived point layer

Contacts stay after those steps.
