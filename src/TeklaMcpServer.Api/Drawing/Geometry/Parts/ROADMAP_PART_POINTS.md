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

The point layer should not be limited to one geometry origin. It should be able
to represent several source families used by drawing workflows:

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

## Point Source Taxonomy

The module should make point provenance explicit, because dimensions and marks
often need not just "a point", but "a point from a specific geometric source".

The intended source taxonomy is:

- `Axis`
  - axis start/end
  - axis midpoint
  - axis-driven directional references
- `Part`
  - bbox corners
  - bbox center
  - left/right/top/bottom points in view-local coordinates
  - extreme points derived from visible part geometry
- `Assembly`
  - main-part-related reference points
  - assembly-level extrema used for overall control dimensions
- `Node`
  - working/reference points representing important local attachment geometry
- `Connection`
  - bolt positions
  - contact-face-related points
  - points derived from connected part relations

This taxonomy should appear in the API either directly in point metadata or as
an explicit source-kind field, so consumers can reason about the origin of each
point without re-deriving it.

## Geometry Rules To Preserve

The module should preserve a few core geometry rules from practical drawing
workflows.

- The canonical output is a reusable point set, not a one-off helper return.
- Point coordinates should be emitted in view-local coordinates.
- The same part may expose several parallel semantic point families.
- The same semantic kind may have multiple instances when geometry requires it.
- Dimension creation should be able to consume an ordered point list directly.
- Connection-aware points must not be collapsed into bbox-only fallbacks when
  real connection geometry is available.
- Contact-like points must remain semantically distinct from generic extreme
  points even if coordinates coincide in some cases.
- Assembly/control dimensions should be able to consume points whose semantics
  are broader than a single part bbox.

## Practical Consumer Scenarios

The roadmap should explicitly support these scenarios.

- Build a dimension from an ordered `PointList` collected from semantic point
  sources rather than from ad hoc geometry reads inside the dimension command.
- Choose between axis-based, part-based, assembly-based and connection-based
  points depending on the requested dimension intent.
- Use the same point vocabulary for mark anchors and future section helpers.
- Support control/diagonal dimensions driven by extreme points of visible
  geometry.
- Support bolt-aware dimensions where the relevant points come from actual
  connection geometry rather than only from part bounds.

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
- every returned point carries enough source semantics to distinguish
  axis-derived and part-derived points
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
- control-dimension scenarios can consume ordered extreme-point candidates
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
- point provenance distinguishes connection-driven geometry from generic part
  geometry
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
- dimension commands can choose point families intentionally:
  `Axis` / `Part` / `Assembly` / `Node` / `Connection`
- mark placement can reuse the same point vocabulary
- point derivation logic is no longer duplicated across consumers

## Validation

Three levels of validation are needed.

### Unit-Level

- kind mapping is stable
- source-kind mapping is stable
- center/bbox/axis point derivation is deterministic
- missing geometry degrades predictably

### Geometry-Level

- returned points stay in view-local coordinates
- left/right/top/bottom semantics are consistent for rotated drawing views
- extreme point selection is stable under repeated reads
- connection-aware points remain distinct from bbox-only fallback points

### Live Tekla Validation

- basic points match visible drawing geometry
- dimension creation from returned points behaves as expected
- axis-based and bolt-aware point sets produce expected dimension inputs
- mark anchors derived from the same points are visually sensible

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Parts` is the single obvious home for part semantic points
- basic part points are available without duplicating geometry readers
- point provenance is explicit enough for consumers to distinguish
  `Axis` / `Part` / `Assembly` / `Node` / `Connection`
- dimensions can consume canonical part points
- marks can consume the same point vocabulary
- future bolt/contact-aware scenarios fit into the same model without redesign

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. add the target types
2. implement basic point extraction over `TeklaDrawingPartGeometryApi`
3. wire one dimension scenario to the new point API

Only after that should bolt/contact-aware point logic be added.
