# Dimensions Roadmap

## Purpose

`Drawing/Dimensions` is the dimension domain module for drawing runtime.

Its long-term goal is to keep the current Tekla integration thin and to rebuild
dimension semantics around the domain model already proven in:

`D:\repos\svMCP\dim`

That legacy `dim` project is the canonical reference for:

- domain vocabulary
- geometry invariants
- grouping semantics
- line-first arrangement logic

This roadmap is the strategic document for the module.

It should answer:

- what the target architecture is
- which invariants must remain true
- what the current baseline already guarantees
- what the next phase should change
- what is intentionally deferred

Operational/current-state notes belong in
[`README.md`](D:\repos\svMCP\src\TeklaMcpServer.Api\Drawing\Dimensions\README.md).

## Architectural Invariants

The following rules are non-negotiable for further work.

### 1. Line-first model

The module must stay line-first.

Core semantics should be derived from:

- `ReferenceLine`
- `Direction`
- `TopDirection`
- `LeadLineMain`
- `LeadLineSecond`
- measured points / chain geometry

Not from:

- bbox summaries
- orientation buckets
- convenience DTO projections

### 2. Domain-centered architecture

The internal center of the module must remain:

- `DimensionItem`
- `DimensionGroup`
- `DimensionOperations`

Public DTOs are transport/read contracts only.
They are not the domain model.

### 3. Grouping is not combining

`DimensionGroup` means a compatible geometric working set.

Grouping exists to support:

- clustering
- reduction
- spacing analysis
- combine-candidate detection
- arrangement/orchestration

Grouping must not imply:

- automatic merge into one Tekla dimension
- loss of original dimensions
- summary bucketing as the main model

### 4. Tekla adaptation must stay separate from domain semantics

The module should keep a clear boundary between:

- raw Tekla reads
- domain normalization
- policy/orchestration decisions
- public/debug projections

### 5. Debug geometry must not dictate domain semantics

Text bounds, fallback text polygons, annotation debug geometry and similar data
remain useful, but they must stay supporting data.

They must not become the primary basis for:

- grouping
- reduction
- domain typing

## Target Architecture

The target internal shape is:

`Tekla runtime snapshot -> domain model -> context/policy/orchestration -> public projection`

### 1. Raw Snapshot Layer

Purpose:

- read Tekla API safely
- normalize runtime data into stable internal snapshots

Current baseline:

- internal snapshot types exist for dimension sets and segments
- query/stable-read paths build snapshots first
- public/read DTOs are projected separately from snapshots and domain state

Typical contents:

- Tekla ids
- view ownership metadata
- raw measured points
- raw `Distance`
- raw Tekla dimension type
- raw segment geometry
- text metadata when available

### 2. Domain Layer

Purpose:

- represent dimensions in `dim` terms

Canonical internal entities:

- `DimensionItem`
- `DimensionGroup`

Core item geometry/state:

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

Core group geometry/state:

- shared/compatible `DimensionType`
- shared/compatible `Direction`
- compatible `TopDirection`
- compatible lead-line geometry
- `MaximumDistance`
- ordered dimension members

### 3. Context Layer

Purpose:

- explain what a dimension measures
- explain how a dimension sits on the drawing sheet

This layer should continue to grow around:

- `DimensionContext`
- source association and point-to-object mapping
- `DimensionGeometryContext`
- `LayoutPolicy`

The context layer exists so future layout decisions are explainable instead of
hard-coded special cases.

### 4. Arrangement Layer

Purpose:

- deterministic post-processing of already-created dimensions
- stack analysis
- spacing planning
- `Distance` adjustment translation

This layer is intentionally conservative.
It is not the place to encode the full future annotation layout engine.

### 5. Orchestration Layer

Purpose:

- combine reduction/policy/context signals into higher-level actions
- keep debug-first explainable packets
- later support agent-facing preview/apply workflows

Current baseline:

- orchestration code lives in its own `Orchestration/` layer
- query/debug plan entry points depend on a single orchestration engine boundary

This layer should stay separate from:

- grouping
- reduction
- raw arrangement planning

### 6. Public API Projection Layer

Purpose:

- expose stable MCP/bridge-facing contracts

Rules:

- public DTOs are projections from the domain model
- DTO shape must not dictate internal architecture
- debug payloads must not become hidden domain substitutes

## Canonical Domain Semantics To Preserve

The following semantics are the main migration target from `dim` and should be
preserved.

- group by domain `DimensionType`
- group by parallel `Direction`
- require compatible `TopDirection`
- use lead-line geometry as core placement geometry
- preserve `LengthList` vs `RealLengthList`
- keep `MaximumDistance` as a real geometric concept
- treat a group as a candidate set for analysis, not as an automatic merge unit
- keep merge decisions separate from grouping decisions
- keep grouping/reduction/combination tolerances policy-driven

## Current Baseline

Current phase status: `v1 complete`.

This means the current baseline is accepted as the stable foundation for the
next phase rather than a temporary experimental branch.

The current baseline already includes:

- explicit internal snapshot layer for dimension sets and segments
- snapshot-native grouping/query/debug paths
- snapshot-native helper flow for measured-point ordering, orientation and
  reference-line reconstruction
- internal `DimensionItem` / `DimensionGroup` modeling
- `DimensionItem` no longer depends on `DrawingDimensionInfo`
- geometry-first grouping and conservative reduction
- line-first `get_drawing_dimensions`
- arrangement planning and `Distance`-based runtime apply
- controlled `combine_dimensions`
- rollback/failure reporting for combine
- local post-combine arrange handoff
- bounded stable reread after mutate
- `DimensionContext`
- source association and point-to-object mapping
- explicit typed source identity via `DimensionSourceReference`
- no remaining flat source-id semantics in domain/context layers
- debug-first `LayoutPolicy`
- orchestration debug packets
- orchestration extracted into a dedicated module/layer
- internal/debug-first action-plan generation surface currently exposed through
  the bridge helper named `get_dimension_ai_orchestration_plan`

What the baseline does not claim:

- full annotation-aware layout
- collision-aware placement against all other annotations
- final public orchestration surface
- complete replacement of operator judgment in drafting edge cases

The current internal action-plan helper is important to interpret correctly:

- it is bridge/internal only
- it is not part of the public MCP tool surface
- it produces a debug-first plan/preview layer
- it does not perform autonomous execution

## Validated Runtime Findings

The following findings are worth preserving because they are already confirmed
on the current implementation and should constrain future work.

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- current public default for `arrange_dimensions` is `10 mm` paper gap
- `arrange_dimensions` has already been live-validated on real drawings:
  - idempotent second run with the same target gap can produce no changes
  - push works when lines are too close
  - pull works when lines are too far apart
- negative `Distance` values occur on real drawings
- sign semantics for negative-distance dimensions remain a risk area for future
  policy/layout work
- line-based grouping and spacing foundation already exists

## Observed Runtime Constraint: Native Dimension Text Position

The following limitation is already confirmed and should not be rediscovered
later by accident.

- native dimension value text can be moved manually in Tekla
- the moved text position is not currently observable through the validated
  Tekla Open API surface checked so far

Checked sources:

- `StraightDimension.GetRelatedObjects()`
- `StraightDimensionSet.GetRelatedObjects()`
- recursive `GetObjects()` traversal where available
- drawing presentation model text primitives
- reflected public/nonpublic members on `StraightDimension`,
  `StraightDimensionSet` and related attributes

Consequence:

- text polygon debug may use runtime text geometry when Tekla exposes it
- otherwise text geometry remains a synthetic fallback path
- this limitation must not distort the main domain redesign

## Next Phase

The foundational architecture cleanup is largely complete.

The next phase should improve naming, semantics and richer layout support on top
of the new baseline.

Priority order:

### 1. Clarify orchestration naming

Current naming still overstates or obscures the deterministic baseline.

Main candidates:

- `DimensionAiAssistedOrchestrator*`
- `get_dimension_ai_orchestration_plan`

Target result:

- names reflect plan projection/recommendation behavior accurately
- deterministic orchestration remains clearly separate from any future
  agent-facing execution path

### 2. Strengthen `DimensionGeometryContext`

Keep growing the geometry-first summary of the annotation itself.

The next useful capabilities are:

- stronger line/band primitives
- clearer text-bounds status
- better support for collision reasoning
- future candidate placement generation

### 3. Add candidate placements and cost-based layout support

This is the first step toward richer annotation-aware layout.

Expected direction:

- keep deterministic baseline
- generate multiple valid placement candidates
- evaluate them by explicit penalties
- remain explainable

### 4. Expand collision-aware layout as a supporting primitive

Later work may add:

- text avoidance
- mark avoidance
- side switching
- richer placement heuristics

This should be built on top of the context layers above, not as more ad hoc
`Distance` translation rules.

### 5. Keep public/debug projections honest

The remaining documentation and code shape should continue to reinforce:

- snapshot/domain/context as the real internal model
- debug/read DTOs as projections only
- no regression back to DTO-first logic

## Deferred / Non-Goals

The following are intentionally not part of the current baseline.

- turning `arrange_dimensions` into a full annotation layout engine immediately
- making text geometry the primary grouping semantic
- exposing all debug/orchestration surfaces as public MCP tools right now
- letting AI bypass the deterministic baseline and operate on raw Tekla DTOs
- re-centering the module around DTOs, bounds or orientation summaries
- transactional unification of combine commit and arrange handoff in the current phase

## Acceptance Criteria

The roadmap is being followed when the following remain true.

1. Internal code centers on `DimensionItem` / `DimensionGroup`, not public DTOs.
2. Query code reads Tekla into internal snapshots or equivalent raw internal structures.
3. Grouping remains line-first and explainable in `dim` terms.
4. Arrangement consumes domain entities, not DTO-shaped hacks.
5. Orchestration stays a separate layer above grouping/reduction/arrangement.
6. Public contracts remain projections from internal model layers.
7. Debug geometry remains supporting data, not the main domain model.

## Guiding Principle

Further work should prefer:

- clearer internal layers
- stronger domain semantics
- explicit policy/orchestration
- explainable deterministic behavior

over:

- adding more special cases directly into transport DTO paths
- extending `Distance` translation to cover unrelated layout problems
- mixing read/debug contracts into the core model
