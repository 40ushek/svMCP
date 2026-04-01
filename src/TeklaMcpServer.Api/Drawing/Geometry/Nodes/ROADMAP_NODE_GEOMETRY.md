# Roadmap Node Geometry

## Goal

Introduce a canonical node/work-point geometry library under
`Drawing/Geometry/Nodes`.

The first goal is to expose practical node geometry without contacts, using
assembly geometry, main-part references and bolt-group geometry already
available in the lower layers.

Current priority order:

- node centers from bolt groups
- fallback assembly node when no bolt groups exist
- reusable node reference points in drawing/view-local coordinates

Not in scope for the current stage:

- MCP transport design
- bridge command design
- contact inference
- exact connection pairing
- dimension or mark behavior

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Nodes`

Reasoning:

- node geometry is not part geometry
- node geometry is not raw bolt geometry
- node geometry sits above parts, bolts and assemblies as a local connection
  layer

Module split should stay explicit:

- `Drawing/Geometry/Parts`
  - raw and derived part geometry
- `Drawing/Geometry/Bolts`
  - raw and derived bolt geometry
- `Drawing/Geometry/Assemblies`
  - raw and derived assembly geometry
- `Drawing/Geometry/Nodes`
  - local node/work-point geometry built from those layers

## Current Base In Code

The module should build on top of existing code, not duplicate it.

Current relevant sources:

- `TeklaDrawingAssemblyGeometryApi`
- `TeklaDrawingAssemblyPointApi`
- `TeklaDrawingBoltPointApi`

This means the first useful node layer is a derived aggregate geometry layer,
not a raw Tekla runtime reader.

## Canonical Domain Direction

The target model should represent nodes in two stages.

### Stage 1: Contact-Free Nodes

The first stage should expose:

- bolt-driven nodes
- assembly fallback node
- node center
- reference line endpoints
- main-part and assembly reference points

### Stage 2: Connection-Aware Nodes

The later stage should expose:

- node geometry paired to concrete part-to-part connections
- work points refined by contact semantics
- later contact-aware node faces or anchors

## Geometry Rules To Preserve

- node coordinates must stay in drawing/view-local coordinates
- node geometry must be built from lower-level geometry APIs
- bolt-group nodes should remain traceable to their source bolt group ids
- fallback nodes should remain explicit and distinguishable from bolt nodes
- contacts stay out of the first stage

## Proposed Types

- `DrawingNodeKind`
- `DrawingNodePointKind`
- `DrawingNodePointInfo`
- `NodeGeometry`
- `GetAssemblyNodesResult`
- `IDrawingNodeGeometryApi`
- `TeklaDrawingNodeGeometryApi`

## Phases

### Phase 1: Folder And Domain Boundary

Status: done in first form.

### Phase 2: Contact-Free Node Geometry

Status: done in first form.

Minimum output:

- bolt-group-backed nodes
- assembly fallback node
- node center
- node bbox
- reference points from bolt, main part and assembly geometry

### Phase 3: Connection-Aware Nodes

Status: planned.

Target additions:

- node-per-connection grouping
- stronger work-point semantics
- later contact-aware refinement

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Nodes` is the obvious home for node/work-point geometry
- one API call can expose reusable node geometry for one assembly in one view
- node geometry reuses assemblies and bolts instead of duplicating their logic
- later contact-aware semantics can be added without redesign
