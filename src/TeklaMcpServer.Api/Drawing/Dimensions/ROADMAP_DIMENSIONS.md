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
- `DrawingViewContext`
- `DimensionDecisionContext`
- `DimensionViewPlacementInfo`
- source association and point-to-object mapping
- `DimensionGeometryContext`
- `LayoutPolicy`

The context layer exists so future layout decisions are explainable instead of
hard-coded special cases.

The proposed persisted context contract for LLM-assisted dimension creation and
repair is described in [`DIMENSION_CONTEXT_SCHEMA.md`](DIMENSION_CONTEXT_SCHEMA.md).
It is not yet the active runtime or case-storage contract. The canonical
semantic unit is `dimension point -> association -> source object -> anchor
kind`; coordinates and derived geometry are evidence, not a replacement for
that association. Runtime DTOs remain implementation-facing projections, while
future migrated cases may use the versioned
observation/decision/action/verification contract from the schema document.

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

During the bridge serialization migration, `TeklaBridge` may temporarily access
internal API types through `InternalsVisibleTo("TeklaBridge")`. Remove that
friend assembly once bridge serialization consumes public read-model DTOs such
as `DimensionContext` and no longer depends on internal implementation types.

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
- `DrawingViewContext`
- `DimensionDecisionContext`
- `DimensionViewPlacementInfo`
- source association and point-to-object mapping
- explicit typed source identity via `DimensionSourceReference`
- no remaining flat source-id semantics in domain/context layers
- debug-first `LayoutPolicy`
- deterministic layout-policy evaluation through `DimensionDecisionContext`
- deterministic orchestration/debug paths using `DimensionDecisionContext`
- arrangement planning using `DimensionDecisionContext` for view-scale-aware gap translation
- orchestration debug packets
- orchestration extracted into a dedicated module/layer
- `PartsBounds` / `PartsHull` / `GridIds` added to `DrawingViewContext`
- per-dimension `PartsBounds` placement classification and exact placement metrics
- `PartsBounds` gap-policy signals exposed in deterministic debug/orchestration evidence
- validated view-local part-geometry contract for both `SolidVertices` and
  `BboxMin` / `BboxMax`
- validated `ViewCoordinateSystem` as the accepted work-plane contract for the
  dimension / `PartsBounds` geometry path
- `DisplayCoordinateSystem` is rejected for this path because it risks mixing
  coordinate spaces between part geometry, dimensions, and debug overlays
- internal/debug-first action-plan generation surface currently exposed through
  the bridge helper named `get_dimension_ai_orchestration_plan`

Current `DrawingViewContext` baseline should be interpreted carefully:

- it currently builds a single-view geometry context
- it currently includes all successful `Parts` and deduplicated `Bolts` from
  that view
- it currently derives `PartsBounds` and `PartsHull` from part geometry only
- it currently assumes part geometry for this context is already normalized into
  the owning view coordinate system before bounds/hull aggregation
- for the validated runtime path, that normalization is expected to come from
  `ViewCoordinateSystem`, not `DisplayCoordinateSystem`
- it currently carries `GridIds` when grids are present on the drawing view
- the current external projection deep-copies `Parts` / `Bolts` mainly as a
  defensive public-contract boundary; this can be relaxed later if transport
  cost becomes significant
- this is a good baseline for assembly-oriented drawing scenarios
- this is not yet the target strategy for heavy GA drawings where a full-view
  object set may be too large to load or reason over directly

Current decision/placement baseline should also be interpreted explicitly:

- `DimensionDecisionContext` is now the shared runtime decision container for:
  - layout policy
  - orchestration/debug
  - arrangement planning/debug support
- `DimensionViewPlacementInfo` is a computed per-dimension placement summary
  relative to `DrawingViewContext`
- `DimensionPartsBoundsGapPolicy` currently evaluates desired gap from
  `PartsBounds` and exposes:
  - current gap
  - target gap
  - whether correction is needed
  - signed axis delta for the nearest chain
- these placement/gap signals are already consumed by the current
  deterministic arrangement pipeline in a narrow, explainable way:
  - the nearest chain on a side can be anchored to `PartsBounds`
  - later chains in the same stack are then arranged from that anchored chain
- they are not yet a full replacement for deterministic arrangement rules or a
  complete annotation-layout engine

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

### Coordinate contract for source geometry and dimension associations

The existing part-geometry path establishes the model work plane from the
owning drawing view. The assignment is implemented in
[`TeklaDrawingPartGeometryApi.cs`](../Geometry/Parts/TeklaDrawingPartGeometryApi.cs):

```text
model work plane = new TransformationPlane(view.ViewCoordinateSystem)
```

`GetAllPartsGeometryInView` and `GetPartGeometryInView` then extract solids,
bounds, and vertices in that view coordinate system. The point reader
([`TeklaDrawingPartPointApi.cs`](../Geometry/Parts/TeklaDrawingPartPointApi.cs))
is a wrapper over the same path. This is the canonical source for
dimension/source-geometry comparisons.

Rules for the next association step:

- do not add an ad-hoc world-to-view conversion after this geometry has been
  read;
- do not use `DisplayCoordinateSystem` for this comparison;
- use sheet projection (`view.Origin` and `view.Attributes.Scale`) only for
  sheet overlays or layout, not for associativity matching;
- persist coordinate-space provenance for every geometry payload in saved
  observations;
- revalidate point-to-source mapping in a live drawing after both dimension
  points and source geometry are explicitly labelled with their coordinate
  space.

The current `get_dimension_contexts` output records that its per-point
selection is inferred. Until the coordinate-space audit is complete,
`DistanceToGeometry` and `MatchedModelId` must not be treated as proof of the
native Tekla associativity rule.

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- current public default for `arrange_dimensions` is `10 mm` paper gap
- `arrange_dimensions` has already been live-validated on real drawings:
  - idempotent second run with the same target gap can produce no changes
  - push works when lines are too close
  - pull works when lines are too far apart
- `place_control_diagonals` has been live-validated on a real view using
  `SolidVertices`-driven hull/extreme-point selection
- `DrawingViewContext.PartsBounds` has been live-validated against debug
  overlay on a real view after restoring the view-local bbox contract
- `arrange_dimensions` has been live-validated with the `PartsBounds` anchor
  path enabled, including outward shifts relative to the overall parts box
- `arrange_dimensions` now treats `PartsBounds` as an exact-gap anchor for the
  nearest chain in the validated deterministic path
- for the validated part-geometry pipeline, `ViewCoordinateSystem` is the
  accepted runtime contract; `DisplayCoordinateSystem` should not be reused
  there
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

### 0. Capture observations in one call

Agreed 2026-08-01. Comes first because everything else in this phase is easier
to judge once examples can be collected without friction, and it is small.

Today an example takes three calls stitched together by hand — view context,
dimension contexts, and drawing identity from a third place. Nothing ties them
to the same moment, so a drawing edited between two of them yields an
observation that is silently a mix of two states. At fifty drawings this also
guarantees examples captured in inconsistent shapes.

Scope, deliberately minimal:

- add **view type** to `DrawingViewContext`. It carries the view id and scale but
  not whether it is a front view, a top view or a section, and the rules differ:
  a section is dimensioned unlike a front view;
- add one command that composes an observation — a header plus the two existing
  context payloads **passed through unchanged**;
- header: drawing type, assembly mark and prefix, units, Tekla version, capture
  time, and a hash of the parts payload.

Explicitly not in scope:

- **no new context type.** Three already exist in the codebase — view, dimensions
  and marks. A fourth overlapping them would have to be kept in sync with the
  others, and this module has just spent a session on exactly that kind of
  divergence. The contexts stay the single source; the observation only binds
  them and attests the moment;
- **marks are NOT part of the v1 observation.** It composes exactly two payloads,
  view and dimensions. Marks have their own context and would widen the contract
  before the first one is proven against a real drawing. When they are needed
  they join as a third payload — not as a new schema;
- no sheet position of the view. It changes with layout while dimensions live in
  view coordinates, so including it would make two observations of an unchanged
  drawing differ and complicate comparison. Layout has its own command;
- no coordinate-space field. All working coordinates are in the view coordinate
  system by invariant, not by choice, so recording it per payload adds nothing.
  The obligation it implies is the reverse: anything working in sheet
  coordinates converts at the boundary and never mixes both into one payload;
- no writing to disk — that belongs to the case service; no reduced context, no
  fingerprints.

The shared parts payload must carry a version or content hash, and the
referencing observation must record it — see `DIMENSION_CONTEXT_SCHEMA.md`.
Sharing is only sound while the geometry is genuinely identical, and a hash is
what makes a mismatch detectable rather than invisible.

Done when an observation of a real drawing agrees with the drawing on every
field, and the existing corpus can be recaptured through it as generation 1. The
six generation-0 cases stay as they are.

### 0b. Keep the relation graph instead of flattening it

Agreed 2026-08-01, straight after the observation command. More valuable than
adding further geometric fields: coordinates describe where a dimension is, the
graph describes what it means.

Target structure:

```text
drawing
 └── view
      └── dimension set
           ├── segment
           │    └── related drawing object
           │         └── model object
           └── DimensionLink → another set
```

**Segment membership is already kept**, as `Owner = "segment:N"` on each candidate
and exposed publicly through `RelatedSources`. What is missing is the **nesting**
and `DimensionLink`: the candidates are flattened into one list per chain, so
walking from a segment to its own related objects means filtering that list by an
owner string rather than following a structure. The data is read; the shape is
lost.

Also missing, and confirmed present in the installed 2025 assembly:

- `DimensionLink` with `GetDimension1()` / `GetDimension2()`, both returning
  `StraightDimensionSet` — the link between two chains is readable and is not
  used anywhere in the project;
- `GetView()`, `GetDrawing()` and `GetRelatedObjects()` on `DrawingObject`, so
  they are available on the set and on each segment alike.

Two things deliberately left out:

- **`GetDimensionSet()` on a segment returns a live `DimensionSetBase`**, not an
  identifier. A live handle must not enter an observation — record the id only.
  Segment-to-chain membership is known anyway, since the traversal goes from the
  set downwards;
- **`GetDrawing()` per object is redundant.** The drawing is one for the whole
  observation and already sits in the header. Reading it per segment is calls
  spent on a constant — the same mistake as reading the assembly mark per part.

Done when a saved observation can answer "which model object does this segment
measure against" without re-reading the drawing.

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

### 3. Add GA-safe `DrawingViewContext` selection strategy

Current `DrawingViewContext` construction is intentionally simple:

- single view
- all parts in view
- all related bolts in view

This is acceptable for assembly-oriented drawing work, but it should not be
assumed to scale to heavy GA views with very large object counts.

The next phase should likely add a more selective strategy for GA-sized views:

- keep the current full-view path for assembly scenarios where it is practical
- add a relevance/filtering mode for large GA contexts
- avoid turning `DrawingViewContext` into an unconditional full drawing dump

### 4. Broaden deterministic use of placement and gap signals

The module already computes:

- `DimensionViewPlacementInfo`
- `DimensionPartsBoundsGapPolicy`

The next step is to broaden and formalize the current narrow anchored layout
path already present in deterministic arrangement planning.

Expected direction:

- use `PartsBounds` as the anchor for the first chain on a side
- preserve the exact target gap from part envelope to the nearest chain
- order later chains from that anchored first chain before applying `Distance`
  translations
- keep the first implementation narrow and explainable

### 5. Add candidate placements and cost-based layout support

This is the first step toward richer annotation-aware layout.

Expected direction:

- keep deterministic baseline
- generate multiple valid placement candidates
- evaluate them by explicit penalties
- remain explainable

Deterministic ordering should also start using view-context geometry more
explicitly:

- treat `PartsBounds` as the baseline anchor for the first chain on a side
- preserve a minimum gap between part envelope and the nearest chain
- prefer chain orderings that reduce crossings between extension lines and main
  dimension lines
- when crossing counts are comparable, prefer richer / denser chains closer to
  the part envelope than overall-only chains

### 6. Expand collision-aware layout as a supporting primitive

Later work may add:

- text avoidance
- mark avoidance
- side switching
- richer placement heuristics

This should be built on top of the context layers above, not as more ad hoc
`Distance` translation rules.

### 7. Keep public/debug projections honest

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

## Known Gap: `LengthList` Does Not Reproduce the Printed Run

Recorded 2026-08-01. Deliberately left as is; do not fix in passing.

### What a dimension prints

Every snap point is projected onto the reference line along the normal, and the
printed values are distances from the START of that line. Confirmed by exporting a
drawing to PDF and comparing: all 26 printed values across four chains matched
this rule, including two horizontal chains counting from the left edge and two
vertical ones counting from the bottom.

### What `LengthList` reports

`DimensionItem.ReplacePointList` projects onto the reference line — that part is
correct and fixed the old straight-line measurement, which invented fractional
values (2555.25 against a printed 2546, 456.91 against 448) that were then read as
snap drift, and produced negative segments once the array ran against the axis.

But it still measures **from `PointList[0]`, not from the near end of the line**.
For a chain whose points arrive in the opposite order to the line, the values come
out correct in magnitude but in reverse: a chain printed as `60 · 1764 · 4309` is
reported as `2545.5 · 4249.1 · 4309.1`.

### Why it was not carried through

Both length lists must stay index-aligned with `PointList`: `DimensionOperations`
turns a length index into a point index by adding one
(`GetLengthMatchedPointIndices`) and takes `LengthList[0]` as the first span.
Ordering the values by position along the line breaks that pairing and matches a
length against the wrong point during packet reduction — a real defect that was
introduced once and caught in review.

Sorting `PointList` itself instead is not free either: it swaps `StartX`/`StartY`
with `EndX`/`EndY` on vertical chains, which grouping, dedup and arrangement all
read. `BuildGroups_MergesNearbySegmentsOnSameLineBandWithinSameDimension` fails on
exactly that.

### What closing it would take

Either a separate list of printed values with its own `length -> point index`
mapping, leaving `LengthList` alone, or changing the downstream contract so a
length and its point travel together as a pair rather than by parallel index.

### Consequence to keep in mind meanwhile

`LengthList` is safe for spans and for anything comparing chains against each
other. It is **not** the absolute run shown on the sheet, so do not diagnose a
drawing by reading it as one — that mistake cost a full session, chasing snap
drift that the drawings did not have.

Test coverage is partial: `ReferenceLineSuppliesTheAxisWhenPresent` proves the
line wins over the direction field, but nothing yet covers the near end or a line
running the other way.

## Tekla API Limitation: AngleAtVertex Movement

Tekla support confirmed that `AngleDimension` objects with
`AngleTypes.AngleAtVertex` cannot be moved visually by changing `Distance`
through Open API.

Observed behavior:

- `Distance` can be changed and persisted.
- `Modify()` returns `true`.
- The drawing does not visually move the angular dimension arc/text.
- Changing `Origin` is not a valid workaround because it changes the measured
  angle geometry.
- `Placing` (`Free` / `Fixed`) does not solve this behavior.

Current policy:

- `MoveAngleDimension` must return `Moved=false` for:
  - `AngleTypes.AngleAtVertex`
  - `AngleTypes.AngleAtVertexGradian`
- The result must include a clear reason.
- Do not move `Origin` as a fallback.
- Any workaround based on delete/recreate must be designed as a separate,
  explicit feature.

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
