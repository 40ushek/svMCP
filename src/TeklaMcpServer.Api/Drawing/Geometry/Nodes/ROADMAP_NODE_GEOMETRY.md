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

Current implemented state in this module:

- contact-free node geometry is implemented
- work-point semantics are implemented
- connection-aware nodes without contacts are implemented

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
- `DrawingWorkPointKind`
- `DrawingWorkPointInfo`
- `NodeWorkPointSet`
- `GetAssemblyWorkPointsResult`
- `IDrawingNodeWorkPointApi`
- `TeklaDrawingNodeWorkPointApi`
- `DrawingConnectionParticipantRole`
- `ConnectionNodeParticipantInfo`
- `ConnectionNodeGeometry`
- `GetAssemblyConnectionNodesResult`
- `IDrawingConnectionNodeApi`
- `TeklaDrawingConnectionNodeApi`

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

Status: done in first form.

Target additions:

- node-per-connection grouping
- stronger work-point semantics
- later contact-aware refinement

Done when:

- bolt-driven nodes are paired with explicit participant parts
- each connection-aware node exposes primary and secondary participants
- work points and reference line remain attached to the connection node

### Phase 2.5: Work-Point Semantics

Status: done in first form.

Target additions:

- primary work point
- secondary work point
- node reference line with fallback to extreme span
- stable anchors to main part and assembly

### Remaining Major Topics

The next unimplemented layers on top of `Nodes` are:

- contact-aware refinement of node geometry
- exact connection-local side/face semantics
- optional node-local contour helpers if future consumers need them

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Nodes` is the obvious home for node/work-point geometry
- one API call can expose reusable node geometry for one assembly in one view
- node geometry reuses assemblies and bolts instead of duplicating their logic
- later contact-aware semantics can be added without redesign

## Near-Term Next Step

The first implementation step after this roadmap should be:

1. keep refining connection-aware nodes only when a downstream consumer needs
   more participant semantics
2. add contacts as the next major missing layer
3. after contacts, refine work points and connection-local anchors using
   real contact geometry
