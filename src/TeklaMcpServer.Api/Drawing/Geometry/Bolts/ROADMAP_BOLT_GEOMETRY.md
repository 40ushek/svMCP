# Roadmap Bolt Geometry

## Goal

Introduce a canonical bolt-aware geometry library under
`Drawing/Geometry/Bolts`.

The goal is to expose raw bolt group geometry in drawing/view-local
coordinates first, and only later derive higher-level connection or contact
semantics from it.

Current priority order:

- raw bolt positions in view-local coordinates
- bolt group relation data to connected parts
- reusable bolt group bbox and anchor points
- derived bolt reference and extreme points

Not in scope for the current stage:

- MCP transport design
- bridge command design
- contact inference
- dimension or mark behavior

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Bolts`

Reasoning:

- `BoltGroup` is its own model entity, not a subtype of `Part`
- bolt geometry may relate to multiple parts at once
- bolt-aware geometry should be reusable by dimensions, marks and later
  connection logic without living inside those modules

Module split should stay explicit:

- `Drawing/Geometry/Parts`
  - raw part solids and part-derived geometry
- `Drawing/Geometry/Bolts`
  - raw bolt groups and bolt-to-part relations
- `Drawing/Geometry`
  - shared geometric helpers

## Current Base In Code

The module should build on top of runtime data already available from Tekla.

Current relevant sources:

- `BoltGroup.BoltPositions`
- `BoltGroup.FirstPosition`
- `BoltGroup.SecondPosition`
- `BoltGroup.PartToBeBolted`
- `BoltGroup.PartToBoltTo`
- `BoltGroup.GetOtherPartsToBolt()`
- `BoltGroup.GetSolid()`
- `Part.GetBolts()`

This means the first useful library layer is a raw bolt group geometry layer,
not a dimension-driven interpretation of bolts.

## Canonical Domain Direction

The target model should represent bolts in two layers.

### Layer 1: Raw Bolt Geometry

The raw layer should expose:

- bolt group identifier
- bolt group shape and basic metadata
- `FirstPosition`
- `SecondPosition`
- individual `BoltPositions`
- optional bolt group solid bbox
- identifiers of related parts

### Layer 2: Derived Bolt Geometry

The derived layer should later expose:

- bolt group axis or reference line
- bolt group center / bbox corners
- bolt-driven anchor points for part geometry
- later connection-aware or contact-aware semantics

## Raw Geometry First

The canonical source of truth must be raw bolt geometry, not pre-baked point
bags.

At the current stage the most important geometry contracts are:

- `BoltPointGeometry`
- `BoltGroupGeometry`
- `BoltGroupGeometryInViewResult`
- `PartBoltGeometryInViewResult`
- `IDrawingBoltGeometryApi`
- `TeklaDrawingBoltGeometryApi`
- `DrawingBoltPointKind`
- `DrawingBoltPointInfo`
- `GetBoltGroupPointsResult`
- `IDrawingBoltPointApi`
- `TeklaDrawingBoltPointApi`

These contracts should be sufficient to:

- inspect real bolt locations in the current drawing view plane
- reuse bolt-to-part relations without re-reading Tekla in every consumer
- later derive bolt-driven geometry for parts and assemblies

## Geometry Rules To Preserve

- the canonical output is reusable geometry data, not one-off dimension inputs
- coordinates should be emitted in view-local coordinates
- bolt-to-part relations should be preserved as explicit IDs
- raw bolt geometry should remain independent from contact inference
- consumers should reuse extracted bolt geometry instead of re-reading Tekla
  runtime objects ad hoc

## Practical Consumer Scenarios

- read one bolt group geometry in drawing/view-local coordinates
- read all connected bolt groups for one part
- build later bolt-driven anchor points for geometry consumers
- reuse the same bolt group geometry for dimensions, marks or contact logic

## Phases

### Phase 1: Folder And Domain Boundary

Status: done in first form.

Done when:

- `Drawing/Geometry/Bolts` exists as the agreed home for bolt geometry
- roadmap and naming make the module boundary explicit

### Phase 2: Raw Bolt Group Geometry

Status: done in first form.

Minimum output:

- `BoltPositions`
- `FirstPosition`
- `SecondPosition`
- `PartToBeBoltedId`
- `PartToBoltToId`
- `OtherPartIds`
- optional bolt group solid bbox

Done when:

- one API call can return canonical raw geometry for a bolt group
- one API call can return connected bolt groups for a part
- all returned coordinates are in view-local coordinates

### Phase 3: Derived Bolt Geometry

Status: done in first form.

Target additions:

- bolt group center
- bolt group axis
- bbox corners
- stable bolt-driven anchor points
- hull and extreme points

Done when:

- one API call can expose reusable derived bolt points for one bolt group
- one API call can expose derived bolt points for all bolt groups of one part
- derived points are computed from the raw bolt geometry layer

### Phase 4: Connection-Aware Geometry

Status: deferred for now.

Target additions:

- bolt-to-part geometric pairing
- later contact-aware connection semantics

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Bolts` is the obvious home for bolt geometry
- raw bolt geometry is available without MCP or bridge transport concerns
- bolt-to-part relations are reusable from one stable library contract
- later bolt-driven or connection-aware geometry can be added without redesign

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. add bolt-group axis/reference-line semantics beyond raw endpoints
2. align bolt-derived points with assembly and node-level consumers
3. later add connection-aware pairing and contact-aware bolt semantics
