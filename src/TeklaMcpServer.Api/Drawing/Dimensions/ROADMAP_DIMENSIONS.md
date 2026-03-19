# Dimensions Roadmap

## Goal

Rebuild `Drawing/Dimensions` around the domain model already proven in
`D:\repos\svMCP\dim`.

For `Dimensions`, the legacy `dim` project is the canonical source for:

- domain vocabulary
- geometry invariants
- grouping semantics
- line-first arrangement logic

The current `src` implementation should adapt Tekla Open API data into that
model, not invent a parallel model around API DTOs.

## Canonical Domain Model

The target internal model should follow `dim` and be centered on:

- `DimensionItem`
- `DimensionGroup`
- `DimensionOperations`

The core geometry/state of a dimension item is:

- `DimensionType`
- `Direction`
- `TopDirection`
- `LeadLineMain`
- `LeadLineSecond`
- `PointList`
- `StartPoint`
- `EndPoint`
- `CenterPoint`
- `LengthList`
- `RealLengthList`

The core geometry/state of a dimension group is:

- `DimensionType`
- shared `Direction`
- shared/compatible `TopDirection`
- compatible lead-line geometry
- `MaximumDistance`
- ordered `DimensionList`

This is the model to preserve and extend.

## Architectural Decision

`DrawingDimensionInfo` and related read DTOs are public transport contracts.
They are not the domain model.

Target layering:

`Tekla Open API snapshot -> DimensionItem/DimensionGroup domain -> arrangement/move/debug -> public DTOs/tools`

Not acceptable as the long-term center of the module:

- DTO-first modeling
- bbox-first grouping
- orientation-first grouping
- public semantics derived mainly from convenience summaries

## Current State

Publicly supported today:

- `get_drawing_dimensions`
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`

Debug/validation helpers currently available:

- `draw_dimension_text_boxes`
- `get_dimension_text_placement_debug`

These helpers are for live validation only. They do not define the long-term
domain model.

## Confirmed Findings

Confirmed on the current implementation and live drawings:

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- line-based grouping/spacing foundation already exists

Confirmed limitation for native dimension value text:

- native dimension text can be moved manually in Tekla
- the moved text point is not currently observable through the validated Tekla
  Open API surface we have checked so far
- checked sources:
  - `StraightDimension.GetRelatedObjects()`
  - `StraightDimensionSet.GetRelatedObjects()`
  - recursive `GetObjects()` traversal where available
  - drawing presentation model text primitives
  - reflected public/nonpublic members on `StraightDimension`,
    `StraightDimensionSet` and related attributes

Consequence:

- text polygon debug may use runtime text geometry when Tekla exposes it
- otherwise text geometry remains synthetic fallback
- this limitation must not distort the main domain redesign

## Design Principles

- `dim` is the canonical domain reference.
- Prefer porting domain logic from `dim` over inventing new abstractions.
- Keep Tekla API adaptation separate from domain semantics.
- Keep debug geometry separate from arrangement semantics.
- Prefer line geometry over bbox for grouping and spacing.
- Treat `orientation` as a summary only, never as the main grouping key.
- Keep Tekla raw type and domain type separate when they diverge.

## Group Semantics

`DimensionGroup` should mean a compatible geometric family or cluster of
dimensions, not a filter and not an automatic merge target.

The intent of grouping is to make it possible to:

- cluster similar dimensions into one geometric working set
- analyze similar/neighboring dimensions together
- detect when some dimensions are redundant and may be rejected
- detect when dimensions are compatible candidates for controlled combination
- drive spacing, arrangement and conflict analysis from shared geometry

Grouping must not imply:

- immediate merge into one Tekla dimension set
- loss of the original individual dimensions
- summary bucketing by `Horizontal` / `Vertical` / `Free` as the main domain
  model

In other words:

- cluster first into a geometric family for analysis and operations
- combine only when a separate rule explicitly allows it
- keep the `dim` meaning of a group as a geometric working set

## Target Internal Types

### 1. Raw Snapshot Layer

Purpose:

- read Tekla API safely
- normalize runtime data into stable internal snapshots

Examples:

- `TeklaDimensionSetSnapshot`
- `TeklaDimensionSegmentSnapshot`

This layer may contain:

- Tekla ids
- view metadata
- raw measured points
- raw distance
- raw Tekla dimension type
- raw text metadata if available

### 2. Domain Layer

Purpose:

- represent dimensions the way `dim` does

Canonical internal entities:

- `DimensionItem`
- `DimensionGroup`

Rules:

- `DimensionItem` is the main logical unit
- a logical item may represent one segment or a chained dimension sequence
- grouping must operate on `DimensionItem`, not directly on transport DTOs

### 3. Operations Layer

Purpose:

- pure domain operations over items/groups

Canonical direction:

- port/translate `DimensionOperations` ideas from `dim`

Examples:

- grouping
- alignment
- combination
- play adjustment
- diagonal/control selection

### 4. Public API Layer

Purpose:

- expose stable MCP-facing read/write contracts

Rules:

- DTOs are projections from the domain model
- DTO structure must not dictate domain structure

## Domain Semantics To Preserve From `dim`

The following semantics are the main migration target:

- group by `DimensionType`
- group by parallel `Direction`
- require compatible `TopDirection`
- use `LeadLineMain` / `LeadLineSecond` as core placement geometry
- preserve `LengthList` and `RealLengthList` distinction
- support center-point based behavior where needed
- keep `MaximumDistance` as a real geometric concept, not just a display metric
- treat a group as a candidate set for analysis, elimination and optional
  controlled combination

## Explicit Non-Goals

Do not center the redesign on:

- `Bounds`
- `TextBounds`
- `Orientation`
- `Absolute` / `Relative` / `RelativeAndAbsolute` as the main domain taxonomy

Those may remain useful, but only as:

- Tekla metadata
- summaries
- fallbacks
- debug aids

## Phases

### Phase 1: Make `dim` the Explicit Canonical Model

Status: next priority.

Done when:

- roadmap and code comments clearly state that `dim` is the canonical domain
  reference
- target internal entities are explicitly defined around `DimensionItem` /
  `DimensionGroup`
- current DTO-first compromises are documented as temporary

### Phase 2: Introduce a Real `DimensionItem`

Status: not started.

Implement an internal `DimensionItem` modeled after `dim`.

It should carry at least:

- ids
- `DimensionType`
- `Direction`
- `TopDirection`
- `LeadLineMain`
- `LeadLineSecond`
- `PointList`
- `LengthList`
- `RealLengthList`
- `CenterPoint`
- source snapshot reference if needed

Done when:

- grouping no longer depends on `DimensionGroupMember` as the primary internal
  concept
- Tekla snapshot data can be projected into `DimensionItem`

### Phase 3: Rebuild Grouping Around `DimensionItem`

Status: not started.

Replace DTO/member-first grouping with `dim`-style grouping semantics.

Grouping must be based on:

- same view
- compatible domain `DimensionType`
- parallel `Direction`
- compatible `TopDirection`
- compatible lead-line geometry
- value compatibility where required by the scenario

Done when:

- `DimensionGroupFactory` effectively becomes a `DimensionItem -> DimensionGroup`
  builder
- grouping logic is explainable in `dim` terms

### Phase 4: Port `DimensionOperations` Concepts

Status: partially present, needs redesign.

The current arrangement/spacings code should be aligned with `dim` operations,
especially for:

- align
- combine
- play-aware adjustment
- same-line/near-line grouping transitions

Done when:

- arrangement logic talks in terms of items/groups, not transport DTO hacks
- line-first spacing is expressed with domain entities

### Phase 5: Reproject Public Read API

Status: deferred until phases 2-4 stabilize.

`get_drawing_dimensions` should be generated from the domain model.

The public response may still expose:

- Tekla dimension type
- orientation
- bounds
- text debug info

But those are projections, not the model itself.

### Phase 6: Text Geometry As A Separate Track

Status: secondary.

Text geometry remains important, but must stay outside the core redesign.

Rules:

- native runtime text geometry is used when Tekla exposes it
- otherwise text geometry remains synthetic
- text geometry must not define grouping semantics

## Acceptance Criteria

The redesign is on track when:

- internal code centers on `DimensionItem` and `DimensionGroup`
- current DTO-first grouping layer is reduced or removed
- grouping is explainable directly from `dim`
- arrangement logic consumes domain entities, not raw API DTOs
- `orientation` is only a summary
- bbox logic is only fallback/debug
- public APIs are projections from the domain model

The redesign is ready to expose further arrange functionality when:

- grouping is line-first and `dim`-aligned
- spacing is line-first and `dim`-aligned
- move mapping is validated on live drawings
- text geometry status is explicit:
  - runtime-observed when Tekla exposes native text objects/position
  - synthetic fallback when Tekla does not expose them
