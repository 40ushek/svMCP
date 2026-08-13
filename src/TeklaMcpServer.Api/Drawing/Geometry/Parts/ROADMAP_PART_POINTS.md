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

The source taxonomy of the derived layer, as it stands:

- `Axis` - `AxisStart`, `AxisEnd`
- `Part` - `SolidVertex`, `FaceBoundaryMidpoint`, `PartContour`, and the legacy
  `HullVertex` and `BoundingBoxCorner`
- `Assembly` - `AssemblyContour`, which has no owning part
- `Connection` - `Contact`, which has two
- later `Node`

This taxonomy belongs to derived geometry, not the raw solid contracts.

## Geometry Rules To Preserve

The module should preserve a few core geometry rules from practical drawing
workflows.

- The canonical output is reusable geometry data, not a one-off helper return.
- Point coordinates should be emitted in view-local coordinates.
- Solid topology should be reusable without touching transport layers.
- Derived points must be computed from extracted geometry, not directly from
  Tekla runtime calls in every consumer.
- Contacts are computed from already extracted geometry, never from a second
  reader of their own.
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

## Contour candidates, then contacts (2026-08-12)

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

The second source, **contact candidates**, now exists as a separate fact layer -
see "Contacts as candidates" below. It is not merged with the contour layer, and after
the experiment recorded below it is not going to be.

What that consumer will run into first is not the merging but the reading. There are two
readers of the same part solid - `TeklaDrawingPartGeometryApi` for the view context and
`TeklaDrawingPartSolidGeometryApi` for the outline, the contacts and the candidates - and
five separate walks from `DrawingHandler` down to the parts. See "One read of a view" in
the assembly-geometry roadmap; it is proposed there, not decided.

Neither becomes a second candidate type. `ViewHull` must not be used for either: it is a
convex hull, so it bridges openings and its corners fall in empty space - the phantom
anchor the defect detector already names. The parts hull was removed from the view
context on the same grounds.

What blocks the selection that follows is not geometry but the part's role. Frame,
filling or fixing decides which candidates can bound an overall, and MATERIAL_TYPE
cannot say: insulation reports 5, the same as timber.

## Contacts as candidates (implemented 2026-08-12)

Where two parts of a view touch, as places a dimension could be taken to. Its own layer,
not merged with the contour one: a contour corner says where a part ends, a contact point
says where two parts meet, and those are different claims about the drawing even when
they land on the same millimetre.

Three steps, each its own thing, and the middle one is the whole reason the layer works.

**A contact has a name.** `Contact.Id` in `SolidContacts`, built from the participants,
the kind, and where and how large the contact is in three dimensions. Never from a
projection - two contacts at different depths land on the same place in a view. It is a
key, not a proof of distinctness, and it is stable against re-tessellation, which broke
the first attempt: an outline fingerprint changed whenever a solid was read at a
different accuracy, which happens routinely between beams and everything else.

**A contact region is flattened onto the sheet.** `RegionFlattener` drops the depth and
then the vertices that only described it - corners that coincide once flat, and corners
in the middle of a straight run, which a subdivided edge leaves behind. What comes back
is a polygon, a segment, or a single place, and the kind is the finding: a patch the view
looks along has every corner on one line and is a segment with two ends, not a rectangle
with four.

The tolerance for calling two places one place is **0.001 mm**, not the 1 mm the contact
search and the outline use. Those answer a different question - which gaps are too narrow
to be real. A millimetre here would erase rebates, chamfers and sheet thicknesses, which
are that size. This one absorbs the arithmetic of projecting and nothing else. A negative
or non-numeric tolerance is refused rather than obeyed: either silently disables the
merging and reports every duplicate as a corner.

Canonicalisation moved into `PlanarRing` and the contour builder now uses it too. One
problem - a closed run of points with no natural first vertex and no natural direction -
and two answers to it would have drifted apart. Direction is discarded rather than
chosen: for a projected region it says which way the surface faced the viewer, which is
the plane's business.

**The flattened shape becomes candidates.** `DrawingContactCandidatePointBuilder`:
polygon gives its corners, segment its two ends, point itself; `ModelObjectIds` is
`[A, B]` and never one of them; `Source = Contact`, `Confidence = DerivedGeometry`;
the anchor is `ContactId + ShapeId + pointIndex`.

Contracts a consumer cannot discover from the shape of the data:

- **the shape id names the shape on the sheet, not the region that cast it.** Two regions
  of one contact are coplanar but can sit apart in depth; seen along that plane they land
  on the same line and share the id. That is right for a drawing - the two really are one
  place there - so two shapes of a contact carrying the same id is a finding, not a fault;
- **places are deduplicated, shapes are not.** The builder drops a repeated `Anchor.Key`
  so one location is not offered as two pieces of evidence. Nothing is lost: colliding
  shapes belong to the same contact and name the same two parts. Two patches of a plate
  at different studs are different contacts and both survive;
- **an anchor here has no owning part.** `Anchor.ModelObjectId` is nullable and the key
  leaves the part off - naming one of the two would make the other's face a coincidence,
  naming zero would invent a part. The id already says which contact and which shape;
- **a shape whose two parts are not both named gives no candidate** and is handed back in
  `Unresolved`. The geometry is real and worth looking at, but "these two parts meet here"
  is a sentence that cannot be finished;
- **three ways a place can be missing stay apart** - `Unread`, `Unflattened`,
  `Unresolved` - because each is answered differently. Rolled into one count they would
  read as "something went wrong somewhere".

### What a real drawing says about it

Checked on the front view of a raked timber wall, through
`get_contact_candidate_points viewId draw`, which also paints the result into the drawing
- shapes in green, candidates as red crosses. The shapes are drawn as well as the points
because two crosses on a line say nothing about whether the line was there.

56 contacts, 144 candidates, complete read. **39 of the 56 come back as segments** - the
ordinary case on an elevation, where the view looks along most junctions. Taken as raw
patches those 39 would have offered 78 corners for 39 places.

Of the 25 points the drawing's seven existing chains actually dimension, measured along
each chain's own direction - X for a horizontal chain, Y for a vertical one, both for a
diagonal - **22 land on a contact candidate within a millimetre**. The three that do not
are the two top corners of the wall, which are outline and not contact; the structural
outline of the same view holds them exactly, at `(2090, 1311.96)` and `(210, 1728.76)`.

That is the argument for the combined set, and it is also the argument for having built
the layers apart: each source misses precisely where the other answers.

### Two one-off readings, neither reproduced

Recorded because they happened, not because either is understood. No fix has been
attempted for either: there is nothing yet to fix, only something to watch.

- the first invocation on a view reported 143 candidates; every one since reported 144,
  with `isComplete` true both times. Possibly a contact sitting on the 1 mm gap
  tolerance;
- in one run of the overlay group sequence the numbers were wrong in a way that did not
  survive a clean repeat: the opening clear reported nothing while 343 objects were on
  the sheet, and the closing clear took 676 where 1288 were expected. Re-run from a
  clean sheet the same sequence gives 343, 1288, 0 exactly. Deleting inside a live Tekla
  enumerator was ruled out separately - 1288 objects clear in one pass and a second pass
  finds none.

The one thing they share is worth writing down and nothing more: both were the first
bridge invocation after new binaries were deployed. That is a correlation across two
observations, not a mechanism, and inventing a fix from it would be inventing the fault
as well.

## The combined set, tried and rejected (2026-08-12)

Built read-only as an experiment, measured on one view, and reverted. No combined layer
and no shared read-model exists, and the code that produced these numbers is gone: it
implemented exactly the thing the numbers argued against, and keeping it would have left
an API surface, a doubled read of the view, and an invitation to build policy on a basis
already known to be wrong.

The question was which sources are needed together. Front view of a raked timber wall,
18 parts, against the 25 points its seven existing chains actually dimension. Every
source read separately, `HullVertex` discarded on the way in, nothing selected between
them, coincident candidates grouped at 1 mm only to be counted.

| source | places | of them on a chain | covers |
|---|---|---|---|
| `PartContour` | 66 | 41 | 25/25 |
| `PartContour` + `AssemblyContour` | 88 | 47 | 25/25 |
| both contours + `Contact` | 95 | 52 | 25/25 |
| `SolidVertex` | 94 | 52 | 25/25 |
| everything kept | 274 | 115 | 25/25 |

1188 candidates collapsed to 274 places, so more than three quarters of them repeat
something. 163 places were found by exactly one source and 138 of those were
`FaceBoundaryMidpoint`.

**`PartContour` alone reaches all 25 points, with the fewest places of anything that
does.** Adding sources to it added places and no coverage.

What that is and is not: it is full coverage at a smaller number of places, measured on
coordinates. It is not a statement about geometric accuracy - a contour corner is still
`DerivedGeometry`, computed by a union that runs at a tolerance which moves boundaries.
And the falling share of on-chain places when sources are added does **not** show the
added sources are useless. Coverage of coordinates is not the criterion a contact is for;
a contact carries "two parts meet here", which no contour corner says, and that has to be
judged against a question about meaning rather than about position.

### What follows, and what does not

- the combined set and the shared read-model are **rejected for now**. See "One read of a
  view" in the assembly-geometry roadmap, which is deferred on this evidence: nothing yet
  needs the sources read together, so the cost of reading a view five times has no
  benefit to weigh against;
- `PartContour` is the base for positions;
- contacts stay a separate semantic layer, not a source of positions;
- `FaceBoundaryMidpoint`, the axis sources and `BoundingBoxCorner` are **not** removed on
  the strength of one view - but none of them goes into new policy without evidence of
  its own.

### The follow-up test, and why it could not be answered here

The useful question is not whether contacts add positions but whether the fact of a
contact changes a decision: a contour corner that is a free end against the same corner
where two parts join. Measured on the same view, it does not - 21% of contour corners
with a contact land exactly on a dimension point, against 20% of those without.

That result proves nothing, because the split is almost empty: **56 of 66 contour corners
have a contact.** On a timber wall a part corner meeting nothing is rare, so the test has
no power here.

The ten corners without a contact all turned out to be on the outer envelope, two of them
exactly the chain ends the contacts cannot reach and the rest 5 to 10 mm outside the
chains - the overhang of an outer layer over the frame that the assembly-geometry roadmap
already records.

**Do not turn that into a rule.** "Contact means interior, no contact means envelope" is
an observation of one timber wall, and the envelope is already answered more reliably by
the role layer, `Defining` against `Attached`, which is what the structural outline uses.
The follow-up belongs on geometry where free ends are ordinary - a steel member, or a
single-part drawing - not on a wall.

## A dimension attaches to an edge, not to a point (2026-08-12)

**A measurement, with nothing implementing it.** No code tests this predicate, and the
candidate layers still emit corners only - so the three points below that lie on an edge
without being at a corner remain unreachable by anything that exists. A first attempt to
wire the contour corners into the coverage join was reverted for exactly that reason: it
added more of a kind of point already there, could not reach those three, read every solid
of the view a second time, and would have read as though the finding were applied.

Measured on the same view as the experiment above, against the 17 distinct points its
seven chains dimension. This is the finding that experiment was looking for and missed,
because it compared points with points.

| what the point sits on | within 1 mm |
|---|---|
| **an edge of a part contour** | **17 of 17** |
| a corner of a part contour | 14 of 17 |
| an edge of a contact | 13 of 17 |
| a corner of a contact | 13 of 17 |

Every point a person used lies on the projected boundary of a part. A corner is only the
special case where two edges meet, and treating corners as the unit is what made three
points look unreachable: two of them are a horizontal dimension taken to the side faces of
two studs, where the person clicked at no particular height, and the third is on a stud
side 17.9 mm below the corner.

### Perpendicular to the chain is the operative part

An edge alone does not give a position. An edge running along the chain spans a range and
fixes nothing; only an edge across it says "here".

The orthogonal chains put 21 points on the drawing. Tested strictly - within 10 degrees of
square to the chain - **17 of the 21** lie on such an edge. The remaining four sit on the
raked members, whose edges are 12.5 degrees off, the rake of this wall.

Widening the tolerance to swallow that is the obvious fix and it is wrong. Measured: at 13
degrees the X positions stay at 15, but the Y positions jump from 10 to 23. A tilted edge
has no single coordinate across the chain - its Y changes along it - so counting one is
taking a midpoint of something that has no middle, and thirteen of those twenty-three are
that artefact.

The four points are corners, and were among the 14 the corner test already found. So the
rule has two cases and they do not collapse into one:

- an edge **square to the chain** gives a position by itself: every point along it has the
  same coordinate across the chain, and that coordinate is the position;
- a **tilted edge gives no position on its own**. It contributes one only where it meets
  another edge, and then the position comes from the meeting - which is what a corner is.

That is why corners keep 14 of 17 while edges reach all 17: the corner is not a weaker
version of the edge rule, it is the other half of it.

Diagonal chains are outside both halves. Their points sit on edges at 49 to 71 degrees,
and correctly so: a control diagonal is a quick check of an assembly's geometry during
fabrication, corner to corner, not a position taken to a face. `place_control_diagonals`
already serves them and this rule does not apply.

### What the rule is worth

Of the 80 contour edges in this view, 44 are square to a horizontal chain and give **15
distinct X positions**; 23 are square to a vertical chain and give **10 Y positions**. Both
counts are at the strict test - a tilted edge is deliberately not counted, for the reason
above. Raked-member corners are then joined with those coordinate sets and coalesced; the
final combined count has not been measured yet, because a corner may agree with a position
already supplied by a square edge. The drawing's bottom chain uses 5 of the 15 and its top
chain uses 5.

That is the size of the real problem: not 1188 candidates or 274 places, but fifteen
positions across and ten up. Choosing among fifteen is a question about rules; producing
274 places was a question about geometry, and it was the wrong question.

### What implementing this looks like, and what it does not

A predicate, not a source: **does this point lie on a contour edge perpendicular to this
chain**. It is asked of a place that already exists - a dimension point being checked, or a
position being proposed - and answered against the edges of the contours, which are
already read.

What it must not become is a fourth candidate source that samples edges into points. An
edge is a continuum; turning each into candidates puts back the hundreds of places this
finding just removed, and the position it defines is one number - the coordinate across
the chain - not a scatter of points along it.

### One correction, recorded because it was made out loud

While chasing the stud point at 17.9 mm below its corner, a contact candidate was found
0.43 mm away and reported as the first case of a contact supplying a position no contour
had. That was wrong: the point lies exactly on a contour edge, at 0.00 mm. Contacts still
add no position here, and the earlier conclusion stands unchanged.

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

Status: done for contacts (2026-08-12). Node-aware geometry is still open.

Contacts are computed by `SolidContacts` from the solids already read for the view,
never from ad hoc runtime lookups, and they arrive in the view's own coordinate system,
which is the system the drawing's dimensions live in. Connection-aware geometry stays
separate from raw solid topology: nothing in `Drawing/Geometry/Parts` knows about
contacts, and the contact layer reads solids through the same reader everything else
does.

Still open:

- assembly/node-aware geometry
- whether a junction, as opposed to a single contact, is worth a candidate of its own

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
4. contact-aware geometry on the same base - done

The combined view-level set was tried and rejected - see below. The sources stay apart
and stay separately readable.
