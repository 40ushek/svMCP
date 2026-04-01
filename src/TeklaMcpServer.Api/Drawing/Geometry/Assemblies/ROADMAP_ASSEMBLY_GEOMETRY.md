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

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. keep assembly geometry aligned with node and connection consumers
2. add richer assembly-local anchors only if a downstream consumer needs them
3. later add contact-aware assembly helpers when contacts are introduced
