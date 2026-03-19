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
- `get_dimension_groups_debug`

These helpers are for live validation only. They do not define the long-term
domain model.

Implemented in the current `src` code:

- internal `DimensionItem` / `DimensionGroup` model exists
- `get_drawing_dimensions` returns the real line-based groups, not summary
  buckets
- grouping is geometry-first and line-first
- first `DimensionOperations`-style reduction step exists:
  - simple redundant items can be rejected when a more informative item in the
    same group already covers the same span
- exact duplicate reduction for simple items exists
- packet-based representative selection exists for nearby items inside one group
- reduction debug now explains what happened to each item:
  - raw group
  - reduced group
  - per-item decision and reason
  - representative packets
- packet debug now also exposes conservative combine-candidate analysis:
  - packet members
  - whether the packet is a potential combination candidate
  - blocking reasons when it is not
- actual Tekla dimension merging is still deferred

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
- Keep grouping, elimination and future merge rules configurable through
  explicit policies instead of hard-coding one permanent formula.

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
- elimination / rejection of redundant items
- alignment
- combination
- play adjustment
- diagonal/control selection

### 4. Policy Layer

Purpose:

- make grouping and reduction rules tunable without rewriting the domain model

Canonical direction:

- add explicit policies for grouping, elimination and later combination

Examples:

- `DimensionGroupingPolicy`
- `DimensionReductionPolicy`
- later `DimensionMergePolicy`

### 5. Public API Layer

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
- keep merge decisions separate from grouping decisions
- allow grouping and elimination tolerances to be policy-driven

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

Status: done.

Done when:

- roadmap and code comments clearly state that `dim` is the canonical domain
  reference
- target internal entities are explicitly defined around `DimensionItem` /
  `DimensionGroup`
- current DTO-first compromises are documented as temporary

### Phase 2: Introduce a Real `DimensionItem`

Status: done in first form.

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
- current status:
  - `DimensionItem` is now the main internal unit
  - some legacy helpers still exist around it and can be reduced later

### Phase 3: Rebuild Grouping Around `DimensionItem`

Status: done in first form, still tunable.

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
- current status:
  - public API now exposes the real line-based groups
  - earlier summary bucketing has been removed from the main read path
  - future work is about policy tuning, not reintroducing a second grouping model

### Phase 4: Port `DimensionOperations` Concepts

Status: in progress.

The current arrangement/spacings code should be aligned with `dim` operations,
especially for:

- eliminate / reject redundant dimensions
- align
- combine
- play-aware adjustment
- same-line/near-line grouping transitions

Current status:

- first elimination step is present
- current elimination is intentionally conservative:
  - simple items may be rejected when a more informative item in the same group
    already covers the same span
- exact duplicate elimination for simple items is present
- first representative-selection step is present:
  - nearby packets inside a group can now keep one representative item
  - current selection is still intentionally simple and policy-driven
- reduction debug now exposes:
  - raw vs reduced groups
  - per-item rejection reasons such as `covered`, `equivalent_simple` and
    `representative_packet`
  - representative packet structure and selection data
- conservative combine-candidate analysis is present at packet level:
  - candidate packets are detected
  - blocking reasons are exposed
  - no actual Tekla merge is performed yet
- controlled combination from `dim` is still pending as a real action layer

Done when:

- arrangement logic talks in terms of items/groups, not transport DTO hacks
- line-first spacing is expressed with domain entities
- elimination, representative selection and combination rules are separated into
  explicit operations

### Phase 5: Introduce Configurable Policies

Status: done in first form.

Add explicit policy objects so grouping and reduction remain flexible.

Initial direction:

- `DimensionGroupingPolicy`
- `DimensionReductionPolicy`

These policies should control things like:

- line-band tolerance
- collinearity tolerance
- extent overlap tolerance
- strict vs soft `TopDirection` matching
- shared-point requirements
- how aggressively similar dimensions are reduced inside a group

Done when:

- grouping and elimination no longer depend on magic constants only
- policy changes do not require redesigning the domain model
- different drawing scenarios can tune grouping/reduction behavior explicitly

Current status:

- `DimensionGroupingPolicy` is introduced and used by `DimensionGroupFactory`
- `DimensionReductionPolicy` is introduced and used by `DimensionOperations`
- representative selection mode is already policy-driven
- next work is not introducing policies, but tuning them and porting more exact
  `dim` rules on top of them

### Phase 6: Reproject Public Read API

Status: partially done, continue refining.

`get_drawing_dimensions` should be generated from the domain model.

The public response may still expose:

- Tekla dimension type
- orientation
- bounds
- text debug info
- raw vs reduced counts

But those are projections, not the model itself.

Current status:

- `get_drawing_dimensions` is already projected from the domain model
- the public response exposes raw vs reduced counts so reduction does not hide
  how many dimensions and items existed before analysis
- deep reduction transparency stays in `get_dimension_groups_debug`, not in the
  main read path

### Phase 7: Text Geometry As A Separate Track

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
- grouping remains the only real grouping model
- arrangement logic consumes domain entities, not raw API DTOs
- `orientation` is only a summary
- bbox logic is only fallback/debug
- public APIs are projections from the domain model
- elimination is a separate operation on top of groups
- merge stays a separate operation on top of reduced groups
- grouping and elimination rules are policy-driven rather than hard-coded
- debug can explain why an item was kept, rejected or selected as a packet
  representative
- packet-level combine-candidate analysis is visible before any real merge
  action exists

The redesign is ready to expose further arrange functionality when:

- grouping is line-first and `dim`-aligned
- spacing is line-first and `dim`-aligned
- move mapping is validated on live drawings
- text geometry status is explicit:
  - runtime-observed when Tekla exposes native text objects/position
  - synthetic fallback when Tekla does not expose them
