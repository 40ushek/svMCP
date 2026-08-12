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
- `TeklaDrawingPartPointApi`
  - derived characteristic points in drawing/view-local coordinates
  - bbox corners and side midpoints
  - hull vertices
  - farthest-point pair / extreme points
- shared geometry helpers
  - convex hull
  - farthest-point pair
- `Drawing/Parts`
  - part identity and user-facing read DTOs

This means `PartPoints` is no longer the first layer. The first layer is raw
geometry, and points are a derived layer built on top of it.

Current implemented state in the wider geometry stack:

- `Parts`
  - raw geometry and derived part points are implemented
- `Bolts`
  - raw and derived bolt geometry are implemented
- `Assemblies`
  - raw and derived assembly geometry are implemented
- `Nodes`
  - contact-free nodes, work points and connection-aware nodes are implemented

## Canonical Domain Direction

The target model should represent a part in three explicit layers:

### Layer 1: Bounds

The lightweight layer should expose:

- part axis / coordinate system
- solid bbox

### Layer 2: View Geometry

The view-local derived layer should expose:

- canonical solid vertices in the view coordinate system
- `ViewHull` as a coarse 2D projection
- semantic and characteristic points derived from that snapshot

`ViewHull` is not an exact outline. It may bridge concavities and cut-outs, so
it is weaker evidence than a face/contour candidate and must not create a native
face or vertex anchor.

### Layer 3: Solid Topology

The topology layer should expose:

- solid vertices
- face list
- loop list inside each face
- stable vertex indexing for topology traversal

Exact projected outlines, holes, and contact geometry are derived from this
topology layer later.

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

## Contour candidates done; contacts next (2026-08-12)

`get_assembly_outline` returns the real projected contour of the assembly and of every
part in the view. On two real assemblies every position the chains use is among them -
all of them on a straight wall, and on a raked one the two the assembly outline misses
are exact in the part contours, because a point where an inner member meets the
silhouette is not a vertex of the union.

`DrawingPartCandidatePointBuilder.BuildFromContours` turns those already-read contours
into an intentionally separate fact layer. It is not yet merged with the older per-part
candidate list and it has no contact or dimension policy:

- a `PartContour` corner has its one factual owner, `DerivedGeometry`, and a
  `ContourVertex` anchor;
- an `AssemblyContour` corner has no owner and `AnchorKind.None`: polygon union can
  create a corner that belongs to no part;
- every corner keeps `ring`, `isHole`, `depth`, and `indexInRing` in its reason, so an
  opening cannot later be mistaken for the outer boundary;
- contour anchors are canonical under Clipper's arbitrary start vertex and walk
  direction. Both are pinned, not just the start: a ring is rotated to its
  lexicographically smallest corner *and* the smaller of its two walks is chosen, since
  walking backwards leaves that corner in place while every other index shifts. The ring
  itself is named by a fingerprint over all its corners at round-trip precision, because
  naming it by one rounded corner lets two rings collide. Keys follow geometry, not a
  native Tekla feature: changing the contour legitimately changes the key;
- an `AssemblyContour` candidate has no anchor and therefore **no key at all** -
  `Anchor.Key` is empty, not `0:None:`. Consumers that group or compare points by key
  must treat an empty key as "not comparable" rather than as an identity these points
  share; there is nothing to anchor a merged boundary to.

The next source is **contact candidates**, also as a separate fact layer. Before it can
emit any point, the contact result must expose a stable identity for one contact. Do not
manufacture that identity from projected coordinates. A contact candidate will carry
both participants, `Source = Contact`, `DerivedGeometry`, and an anchor made from that
contact identity plus its boundary-point index. It emits actual boundary points or
endpoints of a contact interval, never a convenient centroid. A caller must retain the
`Unread` and `IsComplete` contract from `ViewContactsResult`: an absent contact from an
incomplete read is not evidence that nothing meets there.

Only after the source layers exist should a caller decide whether it needs one combined
view-level collection. That is a consumer decision, not a reason to blur source facts
or introduce dimension policy here.

Neither becomes a second candidate type. `ViewHull` must not be used for either: it is a
convex hull, so it bridges openings and its corners fall in empty space - the phantom
anchor the defect detector already names. The parts hull was removed from the view
context on the same grounds.

What blocks the selection that follows is not geometry but the part's role. Frame,
filling or fixing decides which candidates can bound an overall, and MATERIAL_TYPE
cannot say: insulation reports 5, the same as timber.

## Provenance is a list, and it can be empty (implemented 2026-08-11)

A candidate carries the parts it came from as `ModelObjectIds`, from none to
several. The single `ModelObjectId` is gone; it was not kept alongside as a convenience.
A deprecated field next to a list outlives
everyone who remembers why, and the day someone reaches for the shorter one a
contact starts claiming it belongs to the stud alone. Three readers needed the
conversion - `DimensionChainCoverageBuilder` twice and the bridge's serializer once -
so replacing it was cheap before contacts arrived.

Three cases, and the third is the one that needs writing down:

- a point from a part's own geometry carries that one part;
- a contact carries both participants. One point, two owners - where a stud
  meets its plate the point belongs to each of them, and splitting it into two
  records with the same coordinate would invent a second point that is not there;
- **a point created by the assembly-contour union carries none.** `Source` is
  `AssemblyContour` and `Confidence` is `DerivedGeometry`, and the empty list is
  the answer, not a gap in it.

That last one is a contract, not an omission. Unioning part contours merges
boundaries, and the merge runs at a tolerance that moves them: contours are
widened by half the gap tolerance and narrowed again, so a vertex of the assembly
outline need not be a vertex of any part, and intersections appear that no part
ever had. Matching such a point back to the nearest part vertex would invent an
owner - the distance-based association this whole area exists to avoid.

So: no reverse match by coordinate, ever, and no tolerance to make one work. If
a caller needs the part behind a contour point, it should ask the part contours
directly, where provenance is a fact rather than a guess.

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
- `ViewHull` as a coarse derived projection, not an exact outline

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

Transport boundary:

- `PartSolidGeometry` is exposed through the bridge-only command
  `get_part_solid_geometry_in_view <viewId> <modelId>`; it intentionally has no
  MCP wrapper because 2a reads the heavy topology outside the MCP timeout path.
- The command returns `solidGeometryComplete` from the DTO. It is authoritative
  only when the top-level `success` is `true`; on a failed call the empty solid
  block and `false` flag are a failure result, not a partial topology snapshot.
- `PartFaceGeometry.Normal` is nullable and is serialized as JSON `null` when
  Tekla does not provide a face normal.

### Phase 4: Derived Point Layer

Status: done in first form.

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

#### Candidate anchors for dimensions (2a)

The bridge-only command `get_part_candidate_points_in_view <viewId> <modelId>`
builds a separate candidate layer from the one strict topology read. It does not
create or choose dimensions.

- every non-degenerate face edge yields a midpoint candidate. Its anchor includes
  face, loop and the two canonical vertex indexes, so it identifies the actual
  point rather than only the face. A polygon centroid is intentionally not used:
  it can land in a hole or a concavity and would not prove that the point is on
  the part;
- solid vertices and axis ends are separate alternatives, with their own anchors;
- `inPlaneNormal` is the normalized XY projection of a face normal. It is null
  for front/back faces and for sources with no normal, so a placement rule cannot
  mistake an out-of-plane normal for a left/right side;
- hull and bbox candidates remain explicitly lower-confidence. The placement
  builder must gate them by source rather than by a duplicated boolean;
- every candidate carries `modelObjectId + anchor kind + anchor id` and a
  structured reason. Face and vertex index reproducibility must still be checked
  against two reads of the same unchanged drawing before using the strong
  comparison form in a placement plan. Malformed DTOs with duplicate vertex
  indexes omit ambiguous vertex/face-edge candidates rather than throwing.

Live validation 2026-08-01 confirmed three identical reads of a stud and two of
a raked member, including a read after other bridge commands. Face-edge and
vertex indexes remained stable both within one view and across a top-view read:
their keys identify model topology, not its projection. Hull-vertex keys are
different by design across views because their ids contain view-local XY
coordinates; a cross-view consumer must not report that as an anchor mismatch.

The validation covered simple eight-vertex solids only. Cut-outs, holes,
post-restart reads and reads after an edited model remain unverified.

### Phase 5: Outline And Contour Geometry

Status: deferred for now.

The exact contour of a part should not be modeled as a plain convex hull.

Target direction:

- project face loops into the drawing/view plane
- build projected face polygons
- run polygon union over those projected polygons
- expose outer contour and inner holes separately

Important rule:

- `convex hull` is acceptable only as a coarse diagnostic or geometric helper
- `convex hull` is not the target implementation for an exact part contour
- it creates no candidate in the contour/contact route and no native face or vertex
  anchor. The older per-part reader still emits its `HullVertex` legacy source; a
  future combined view-level consumer must discard it until its separate removal.

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
2. add projected outline helpers via polygon union
3. align part points with assembly and bolt point taxonomies where useful
4. later add contact-aware geometry on top of the same base

Contacts stay after those steps.
