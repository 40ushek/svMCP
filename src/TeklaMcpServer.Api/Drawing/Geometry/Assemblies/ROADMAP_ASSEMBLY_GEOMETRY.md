# Roadmap Assembly Geometry

## Goal

Introduce a canonical assembly-centric geometry library under
`Drawing/Geometry/Assemblies`.

The goal is to expose raw assembly geometry in drawing/view-local coordinates
by aggregating member part solids and connected bolt groups.

Current priority order:

- main part and secondary part membership
- raw member solids in view-local coordinates
- assembly-level bbox and member list
- connected bolt groups reused from the bolt geometry layer
- derived assembly points and extreme geometry

Not in scope for the current stage:

- MCP transport design
- bridge command design
- contact inference
- node/work-point geometry
- dimension or mark behavior

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Assemblies`

Reasoning:

- assembly geometry is broader than one part and broader than one bolt group
- assembly-centric drawing workflows need a reusable aggregate view of parts
  and bolts
- this should stay separate from `Views` alignment logic and from transport
  layers

Module split should stay explicit:

- `Drawing/Geometry/Parts`
  - raw geometry of one part
- `Drawing/Geometry/Bolts`
  - raw geometry of one bolt group and bolt-to-part relations
- `Drawing/Geometry/Assemblies`
  - aggregate geometry of one assembly in one drawing view

## Current Base In Code

The module should build on top of existing code, not duplicate it.

Current relevant sources:

- `TeklaDrawingPartSolidGeometryApi`
- `TeklaDrawingBoltGeometryApi`
- `Assembly.GetMainObject()` / `GetMainPart()`
- `Assembly.GetSecondaries()`
- `Assembly.GetSubAssemblies()`

This means the first useful assembly layer is an aggregate raw geometry layer,
not a derived point or dimension layer.

Current implemented state in the wider geometry stack:

- `Assemblies`
  - raw and derived assembly geometry are implemented
- `Nodes`
  - assembly geometry is already consumed by contact-free nodes,
    work points and connection-aware nodes

## Canonical Domain Direction

The target model should represent an assembly in two layers.

### Layer 1: Raw Assembly Geometry

The raw layer should expose:

- assembly identifier and type
- main part identifier
- member part list
- optional subassembly identifiers
- member solids reused from the part geometry layer
- connected bolt groups reused from the bolt geometry layer
- assembly-level bbox

### Layer 2: Derived Assembly Geometry

The derived layer should later expose:

- assembly center
- assembly extreme points
- main-part reference points
- later node/work-point and connection-aware helpers

## Geometry Rules To Preserve

- coordinates must stay in drawing/view-local coordinates
- member part geometry should be reused, not re-modeled ad hoc in consumers
- bolt groups should be deduplicated at the assembly level
- assembly-level bbox should be computed from extracted member geometry
- contacts and node semantics stay out of the current stage

## Practical Consumer Scenarios

- read one assembly geometry in one drawing view
- inspect main part and secondary members together
- reuse one assembly bbox for later control geometry
- build later assembly-driven semantic points
- reuse bolt groups in an assembly-centric pipeline

## Proposed Types

- `AssemblyPartGeometry`
- `AssemblyGeometry`
- `AssemblyGeometryInViewResult`
- `IDrawingAssemblyGeometryApi`
- `TeklaDrawingAssemblyGeometryApi`
- `DrawingAssemblyPointKind`
- `DrawingAssemblyPointInfo`
- `GetAssemblyPointsResult`
- `IDrawingAssemblyPointApi`
- `TeklaDrawingAssemblyPointApi`

## Phases

### Phase 1: Folder And Domain Boundary

Status: done in first form.

### Phase 2: Raw Assembly Geometry

Status: done in first form.

Minimum output:

- main part id
- member part list with solids
- subassembly ids
- assembly bbox
- deduped bolt groups

### Phase 3: Derived Assembly Geometry

Status: done in first form.

Target additions:

- assembly center
- farthest-point pair for the whole assembly
- main-part-driven reference points
- member part centers
- bolt-driven points

Done when:

- assembly bbox and aggregate member geometry can be turned into reusable
  assembly points
- main-part and bolt-driven references are available from one stable API
- hull/extreme points can be computed for the whole assembly

### Phase 4: Node And Connection Geometry

Status: addressed by `Drawing/Geometry/Nodes`.

Target additions:

- work points
- node reference geometry
- later contact-aware assembly helpers

Current note:

- node/work-point and connection-aware layers now live in
  `Drawing/Geometry/Nodes`
- assembly-specific contact helpers are still open for a later stage

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Assemblies` is the obvious home for assembly geometry
- one API call can expose a reusable aggregate geometry of the assembly
- member part solids and bolt groups are reused from stable lower-level APIs
- later node or connection logic can be added without redesign

## Assembly outline (2026-08-11)

`get_assembly_outline` returns the real projected polygon of everything a view draws:
outer rings, holes and disconnected components, plus each part's own contour beside the
union. Verified against two real assemblies, including a raked one where a bounding box
and a convex hull both flatten the slope, the steps and the notch.

Two facts it settled, worth having here because assembly geometry is where they land:

- the extremes of the outline are the ends of the outer chains, but only over the
  structural set - on both walls checked, one end ran 10 mm past the chain because an
  outer layer overhangs the frame;
- points where an inner member meets the silhouette are not vertices of the union and
  come only from the individual part contours. On a straight wall this never shows.

## One read of a view - proposed, not decided (2026-08-12)

Raised after the contact layer landed, when it became possible to want the outline and
the contacts of one view at the same time. Written down to be argued with; nothing here
has been implemented.

### What is actually there now

Two readers of the same solid of the same part in the same view, giving differently
shaped answers:

- `TeklaDrawingPartGeometryApi` produces `PartInView` - properties, axes, `SolidVertices`,
  `ViewHull`. The view context uses it.
- `TeklaDrawingPartSolidGeometryApi` produces `PartSolidGeometryInViewResult` - faces,
  loops, vertices. The assembly outline, the contacts and the candidate points use it.

And five consumers, each starting from `DrawingHandler` and walking down to the parts on
its own: the view context, the assembly outline, the structural outline, the contacts,
the per-part candidate points.

Each `GetPartSolidGeometryInView(viewId, modelId)` call opens the drawing, enumerates
every view on the sheet to find the one asked for, switches the transformation plane,
selects the part and reads the solid - per part. Asking one 18-part view for its outline
and its contacts is 36 of those over the same 18 parts.

The cost is an argument from the code, not from a stopwatch: the bridge invocations
measured about 2.2 s each, but that is dominated by process start and connecting to
Tekla, and does not isolate the reading.

### What is proposed

The view read once, and held: the parts as `PartInView` with their contours, the
assembly outline, the contacts. The outline, the structural outline, the contacts and
the candidate layers then become functions over that object rather than readers of
Tekla. They are already pure functions in everything but where they get their input.

**No sixth type called Assembly.** On an assembly drawing the view and the assembly are
the same thing, on a general arrangement they are not, and every result type here is
already view-scoped - `ViewContactsResult`, `ViewAssemblyOutlineResult`,
`ViewContactCandidatePointsResult`. Introducing `AssemblyInView` beside the view context
would be two ways of saying one thing until a general arrangement forces them apart. So
the unit of reading stays the view, the view context becomes that single read, and an
assembly appears as a grouping inside it if and when a general arrangement needs one.

Two conditions, or this is worse than what it replaces:

- **one solid reader, not two.** Otherwise a part answers differently depending on who
  asked, which is the trap tessellation accuracy already set once;
- **`ViewHull` does not come along.** It is a convex hull; its corners fall in empty
  space, and the obligation to drop it at the first combining consumer is already
  recorded in the part-points roadmap.

### What is not settled

- whether the single read is eager for the whole view or lazy per part - the contacts
  need every part, `get_part_geometry_in_view` needs one;
- where the read is cached and for how long. `DrawingReservedAreaReader` caches by
  drawing id in a static field and is invalidated on open and close; the persistent
  bridge means a static cache now outlives a call, which it did not when the bridge was
  started per command;
- whether the contours belong in the same object as the parts or beside them. They are
  derived, not read, and putting derived geometry next to read geometry is how
  `ViewHull` got where it is.

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. settle the "one read of a view" proposal above, or reject it
2. keep assembly geometry aligned with node and connection consumers
3. add richer assembly-local anchors only if a downstream consumer needs them

Contacts are no longer future work - see the part-points roadmap. What assembly geometry
still owes them is the question of whether a junction, as against a single contact,
deserves anything of its own here.
