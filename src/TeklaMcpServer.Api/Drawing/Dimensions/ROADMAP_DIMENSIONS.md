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
  the bridge helper named `get_dimension_action_plan`

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

### The coordinate contract does not always hold — verified failure

Found 2026-08-01 on drawing `IW.10`, view 1218, after nine drawings where it did
hold. The part-geometry read returned **model** coordinates while the dimension
read returned view coordinates, so the two could not be matched at all.

Measured across all thirteen parts: X spanned 1363, Y spanned 100, Z spanned
3738.2. The chains in the same view measured 1363 wide and 3738.2 tall. So the
wall's height sat in the parts' Z and the dimensions' Y; the parts' Y held the
wall thickness. `axisY` on every part was `[0, 0, 1000]` — world Z — where a
correct read points it up the wall.

Not transient: repeating the read reproduces it, reading dimensions in between
changes nothing, and the top view returns a third orientation
(`axisY = [0, -1000, 0]`).

**This invalidates the assumption the association layer rests on.** Everything
downstream — candidate points, chain coverage, the placement rules — works by
matching a dimension point to a part face. Without a shared space none of it is
meaningful, and a planner that proceeded anyway would produce confident nonsense.

Two things follow:

- **the read must report which space it is in**, not leave callers to infer it
  from the numbers. A `coordinateSpace` on the geometry result, checked by
  `get_dimension_chain_coverage` before joining, would turn a silent mismatch
  into a refusal;
- **the cause needs finding.** The hypothesis is that this wall sits in the model
  in an orientation where `TransformationPlane(view.ViewCoordinateSystem)` does
  not flatten it into the view plane. Unverified. Evidence is kept in
  `cases/dimension_cases/assembly/965a95fe-…/before/` — the mismatch reproduces
  from the saved files without Tekla.

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

Implemented 2026-08-01 as `capture_dimension_observation` / `CaptureDimensionObservation`:
the command is read-only, returns the header plus the unchanged view and dimension
payloads, and does not persist files.

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
field, and the existing cases can be recaptured through it as generation 1. The
six generation-0 cases stay as they are.

### 0b. Keep the relation graph instead of flattening it

Agreed 2026-08-01, straight after the observation command. More valuable than
adding further geometric fields: coordinates describe where a dimension is, the
graph describes what it means.

Implemented 2026-08-01: `get_dimension_contexts` and the observation payload now
retain per-segment related sources and expose readable `DimensionLink` endpoint
IDs. The legacy flat `RelatedSources` list remains for compatibility.

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
  `StraightDimensionSet` — readable, and not used anywhere in the project.

  It is specifically a link between two **perpendicular** dimension lines, joined
  so the lines meet and the sheet reads more cleanly — typical on embeds in a cast
  unit, floor beams on a plan, or anchor bolts. It is created **by hand** in the
  drawing (select both with Ctrl, then Link dimensions), never by automatic
  dimensioning.

  That is why every drawing looked at so far reports `dimensionLinks: 0`, and why
  the read path is still unverified: there is nothing to read until someone links
  two lines. Not worth manufacturing an example for — it will appear on a drawing
  that actually uses them;
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

### 1. Clarify orchestration naming — done

`DimensionAiAssistedOrchestrator` was named for something it never did: it uses
no model and executes nothing. Renamed to `DimensionActionPlanBuilder`, with
`DimensionActionPlanResult`, `DimensionActionPlanStep`, `DimensionPlanAction`
and `DimensionActionPlanEvidence` alongside it. Behaviour is unchanged.

The command is now `get_dimension_action_plan`. The old
`get_dimension_ai_orchestration_plan` stays as an alias so existing callers keep
working.

The boundary this fixes in the naming:

```text
observation -> DimensionActionPlanBuilder -> plan
            -> an LLM or a person decides whether to apply it
            -> existing combine / move / arrange / recreate
```

A future model consumes the same observation and either emits a plan of this
shape or selects steps from one. It does not go inside the builder.

### 2. Strengthen `DimensionGeometryContext`

Keep growing the geometry-first summary of the annotation itself.

The next useful capabilities are:

- stronger line/band primitives
- clearer text-bounds status
- better support for collision reasoning
- future candidate placement generation

Checked 2026-08-01: line direction, normal, band and measured points **already
exist** here, as do the reference line, dimension-line ends, distance and segment
geometry. Point-to-object association exists too, in `Association`. There is no
missing field on that list.

What this context has instead is duplication. One `DimensionContext` carries
three point collections — `Geometry.PointList`, `Association.MeasuredPoints` and
`AnnotationGeometry.MeasuredPoints` — and the last silently falls back to
`item.PointList` when `item.MeasuredPoints` is empty, so its source is not
visible from the value. Consolidating those, or at least making the fallback
explicit, is worth more than any new field.

Note also what this context is *for*: it describes an **existing annotation**.
Placing new dimensions (`2b`) barely needs it. That work needs part geometry —
which face of which part a point should sit on — and that lives in
`DrawingViewContext.Parts`, where the real gap is: bounding box, solid vertices
and axis are available, but no notion of a face. The rule being implemented is
"the point sits on the part being located", and a raked member already showed
that the box is not the part.

### 1a. Report what the sheet actually prints

Found 2026-08-01, after two wrong conclusions in one day about what a chain reads.

Nothing here is genuinely unknowable. Tekla holds the point order, the type decides
which rows are drawn, and the sheet shows definite numbers. The read model simply
does not carry any of it, so every consumer reconstructs it — and can get it wrong,
as the assistant did twice, once by differencing adjacent points and once by
exporting a PDF and seeing only one of the two rows.

Worse, one piece is destroyed on the way out: **the point order is normalised on
read**, top-to-bottom and right-to-left. The order that fixes the zero of the
absolute row is not recoverable from the read model at all.

The dimension context should report:

- **which point is first** — the zero the absolute row counts from. This has to be
  captured before normalisation, or it is gone;
- **which rows are printed**, from `TeklaDimensionType`. Already available, just not
  interpreted anywhere;
- **both printed rows, computed here**: the relative values between adjacent points
  and the absolute running totals from the start. Consumers should read them, not
  derive them.

`LengthList` is not this. It is computed from the normalised point order, so it
answers neither row reliably — see the known gap below.

Done when a caller can tell what a drawing prints without opening the drawing.

### 1b. Set a chain's start point as an operation

Requested 2026-08-01: changing where an absolute chain counts from is a task that
comes up regularly, not a one-off.

Absolute dimensions print the running total from the chain's start, and the start
is fixed by the order the points were passed at creation. Read-back normalises the
order, so the current start is not visible in the read model at all — only on the
sheet. The usual correction, on a layer laid over a finished frame, is to move the
zero onto the frame.

Today this is done by hand with `recreate_dimension` and a reordered point list.
That works, and it has three side effects the caller has to clean up every time:

- the set is renumbered, so any id held elsewhere goes stale;
- the offset drifts, because copied attributes carry their own, and has to be
  restored with `move_dimension` — which moves by a delta, not to a value;
- creating or recreating reflows neighbouring chains, so their offsets need
  re-reading afterwards.

A `set_dimension_start_point <dimensionId> <x> <y>` would read the chain, rotate
its point order so the point nearest the given anchor comes first, recreate with
the same attributes, and restore the offset. Verified on `EW.3-6`: reordering
changes only the absolute row — the relative segments are between geometrically
adjacent points and are unaffected — so the operation is safe in that respect.

Two things it should report, because neither can be checked afterwards from the
read model: which point ended up first, and what the absolute row now reads.

### 2a. Candidate points on parts

Agreed 2026-08-01. Prerequisite for `2b`, and separate from it because it is a
different question: `2b` decides which parts to dimension and in what order,
while this decides *where on a part* a dimension point may legitimately sit.

The rule being served is "the point sits on the part being located". Today that
cannot be answered reliably. `DrawingViewContext.Parts` carries a bounding box,
solid vertices and the part axis — and no face. The box is not the part: a raked
top plate measures 383 mm by its box against a 45 mm member, so the box corner is
nowhere on the part.

#### Minimum result

Per part, a list of candidate points. Each candidate carries:

- the **point** in view coordinates;
- the **source** it was derived from — axis end, solid vertex, face midpoint,
  box corner — so a consumer can tell a real geometric feature from a fallback;
- the **normal / side** it faces, which is what makes a candidate "the left face"
  rather than just a coordinate. **Nullable** — see below;
- a **confidence** value;
- a **structured reason** for the choice;
- a **stable anchor key**, so a point can be compared without coordinates.

#### The normal is often genuinely unknown

A face has a normal. A solid vertex does not — several faces meet there — and the
end of an axis has a direction but no side. Those cases must be representable as
`unknown` / null, never as an invented normal: a fabricated side reads exactly
like a measured one and there is nothing downstream that could tell them apart.

The consequence is worth stating, because it is not a mere annotation: a
candidate without a normal can still fix a **position along the chain**, but it
cannot satisfy a rule phrased as "the left face of the studs". `2b` must be able
to see that difference and either pick another candidate or say it could not.

#### Anchor identity

"The same object and the same anchor" needs a formal key, or the strong form of
the acceptance comparison in `2b` cannot be implemented:

```text
modelObjectId + anchorKind + anchorId
```

- `modelObjectId` — the durable part. Drawing object ids are transient and must
  not carry identity;
- `anchorKind` — face, vertex, axis end, box corner. Already the vocabulary used
  in `DIMENSION_CONTEXT_SCHEMA.md`;
- `anchorId` — which face or which vertex, when the kind alone is ambiguous.

The key has to be reproducible across two reads of an unedited model, and that
needs verifying rather than assuming — Tekla's own ordering of solid faces and
vertices is not documented as stable. If it turns out not to be, the key must be
derived from geometry (for example a face's own normal and its position on the
part) instead of from an index, and the strong comparison form depends on getting
 this right.

The `2a` acceptance check is explicit: read the same unchanged model twice,
produce the candidate list twice, and compare every key for the same
`modelObjectId`. The check passes only when the keys are identical across both
reads. If a face or vertex key changes, that anchor kind is not eligible for the
strong comparison form until a stable geometry-derived key replaces it.

Several candidates per part is the expected output, not a failure. Narrowing to
one point per grid position is `2b`'s job, and it needs alternatives to choose
between. Ranking here, deciding there.

Confidence must degrade honestly. The geometry evidence has three levels, in this
order:

```text
face / exact contour > convex hull (ViewHull) > bounding box
```

`ViewHull` is a useful coarse projection, but it can bridge a concavity, a cut-out,
or an L-shaped re-entrant edge. A point derived from it is therefore not proof that
the point lies on the part. It has no stable native face/vertex anchor either. Hull-
derived candidates must remain distinguishable and must not silently become
`Create` points; they require a later exact-contour check or an explicit degraded
decision. The difference between a face-derived point, a hull-derived point, and a
box fallback must survive into the plan rather than being averaged away.

#### Box-derived points are not usable for `Create`

Producing a box corner as a last resort is fine; letting it become a dimension
point is not. On a raked member the box corner is provably **not on the part** —
that is the failure the whole rule exists to prevent, so a plan that quietly used
one would be wrong in exactly the way it was built to avoid.

The rule for `2b`:

- a box-derived candidate **must not** silently become a `Create` point;
- if no better candidate exists for a part, the default is to **omit the part**
  and record why — a missing dimension is recoverable, a dimension pointing at
  nothing is not;
- if such a point is used deliberately anyway, the step is marked **degraded**,
  names the parts concerned, and requires confirmation. When applying arrives, a
  degraded step must never auto-apply.

Confidence alone is not enough here. A threshold on a number invites tuning until
the plan looks complete; the source of the point is categorical and should be
treated as such.

Done when every candidate states where it came from and how far it can be
trusted, and a box-derived fallback is distinguishable from a face-derived point
without re-reading the model.

Implementation 2026-08-01: `get_part_candidate_points_in_view` now exposes the
read-only candidate list through the bridge. Every non-degenerate face edge
contributes its midpoint, with a key containing face, loop and vertex indexes;
a face centroid is not used because it may fall into a hole or concavity. Live
validation remains: read the same unchanged part twice and compare every emitted
face-edge and vertex anchor key.

Live validation 2026-08-01 confirmed stable face-edge and vertex keys across
repeated reads and across two drawing views. Hull keys are intentionally
view-local and must not be compared across views. The observed 42 candidates for
one part occupied only 10–12 distinct XY positions: `2b` must first collapse
candidates by position, record the members of every cluster, and only then rank
the alternatives. Otherwise the same projected point competes with itself and a
tie is resolved by incidental traversal order. This validation covers simple
solids; holes, cut-outs, post-restart reads and model edits remain open cases.

### 2a.1. Coverage of a corrected chain — done

Agreed and run 2026-08-01. Before writing the planner, check whether the candidate
layer already contains the points a person actually used, and **save the answer**.
An acceptance criterion that lives in someone's head is not one.

`get_dimension_chain_coverage <viewId> <dimensionId> [toleranceMm]` joins one chain
to the candidates of every part in its view, point by point. It records the
dimension and segment ids, the coordinate, the associated model id, every anchor
key within tolerance, the distance, and a status.

It **never picks a winner** among several matches. Which of them a plan should
prefer is the rule `2b` has to derive, and pre-selecting here would destroy the
evidence for it.

Two radii, deliberately not one:

- the **search tolerance** says how far from the dimension point to look;
- **position coincidence** is a separate, tight epsilon. A face edge shared by two
  faces yields the identical midpoint and a hull vertex is built from a solid
  vertex, so genuine coincidence is exact. Using the search radius for both would
  report a real choice between two places as settled.

`matched` therefore means one *place*, which normally carries three to six anchor
keys; `ambiguous` is reserved for matches at genuinely different places.

#### First run: `IW.1 - 1`, view 1214 — a tool test, not a reference

This drawing was the one open at the time. It has no recorded human pass, no
before/after pair, and it is an interior wall, while every captured drawing with a
human pass is a roof panel. Treat the run as evidence that the command works, not
as evidence about correct dimensioning.

All eight chains in the view, 27 points: every one `matched`, on the part the
dimension was already associated with, none missing, none ambiguous. Twenty-five
matched a face edge, a solid vertex and a hull vertex at the same place within
0.05 mm — so the candidate layer does contain the points these chains use.

**Three of the 27 points are claimed by two parts each**, all of them junctions:
a stud standing on the plate below it, and a raked plate resting on a stud. The
status stays `matched` — one position, not a choice of positions — but eleven or
twelve keys arrive from two parts. `2b` needs a rule for this: "the point sits on
the part being located" does not say which of two parts meeting at one place is
the one being located, and on a wall almost every level is such a junction.

**Two points reach only a bounding-box corner, and they turned out to be a drawing
error.** Chains 1622 and 1686 both anchor at `(1833.5, 1691.1)`, where the sole
candidate is the box corner of the raked top plate. That corner is not on the
plate: its real vertices at that end are at y=1284.6 and y=1223.1, and it reaches
y=1691.1 only at the opposite end — where a genuine vertex exists. The dimension
was set against an imagined assembly box; the fix is to extend the point to the
part or drop it.

So this is **not** a missing source in `2a` — the anchor exists on the same part.
`fallbackOnly` now marks the condition: matched, but only by a hull vertex or a
box corner.

The flag **requires adjudication and is not proof of an error**. Two different
situations raise it. The point may genuinely sit in empty space, as here. Or the
part's solid could not be traversed — and then the candidate layer offers nothing
but the axis and the box, so a perfectly good point simply has no better evidence
available. Check `SolidGeometryComplete` for the parts involved before reading the
flag as a defect.

#### Consequence: which chains may serve as reference, and only after screening

Two separate points, and the first was got wrong on the first attempt.

**Not every captured drawing is reference material.** Of the six cases that
existed, only three carried a recorded human pass; the rest were single as-found
snapshots claiming nothing. A drawing that merely happens to be open is not a
reference, however convenient — that mistake was made here first, on the drawing
this section reports.

**Even a corrected chain must be screened.** A human pass reduces mistakes; it
does not certify their absence. Grading a planner on reproducing a chain point
for point would grade it on reproducing whatever survived. Any point that is
`missing`, `ambiguous`, or `fallbackOnly` is a candidate defect and has to be
adjudicated — extended to the real anchor, deleted, or confirmed as intended.
Unclear cases are discussed, not decided by the tool.

Coverage screens **anchoring, not selection**. A chain carrying a redundant point
— two parts sharing one grid position, the 15–60 mm junk segment this whole line
of work started from — passes cleanly, since every point does sit on its own
part. That class needs its own check before a chain is called screened.

Every case under `cases/`, including this run's fixture, was deleted on
2026-08-01: it predated anchors, candidate points and segment relations, and its
lengths came from a formula since corrected, so re-capturing beat migrating. The
numbers above are reproducible — the drawing is unmodified and the commands are
in the skill file.

**`2b` has no acceptance material until a new before/after pair is captured.**
That capture is the prerequisite, not the planner.

### 2b. Read-only `DimensionPlacementPlanBuilder`

Agreed 2026-08-01. The first component that decides where dimensions *should*
go, as opposed to reducing the ones already there. It changes nothing: it reads
one view and emits a plan.

Depends on `2a` for candidate points.

Scope of the first version:

- one `FrontView` on an assembly drawing;
- select the parts to locate, and one point per part;
- decide the chain axis, the side, and the point order;
- emit `create_dimension` arguments;
- apply nothing.

#### Reuse the existing plan contract

`DimensionActionPlanStep` already describes "a call someone may choose to make",
with action, tool name, arguments, `previewOnly` and evidence. Placement must
extend `DimensionPlanAction` with `Create` and widen
`DimensionActionPlanToolArguments`, **not** introduce a second plan shape. Two
plan contracts means every consumer — a person or a model — has to learn both.

The builder is separate; the contract is shared.

#### Arguments the plan must actually carry

`CreateDimension(viewId, points, direction, distance, attributesFile)`. All five,
including the two that are easy to forget:

- **`distance` is a target, not an outcome.** Attributes carry their own offset
  which Tekla adds on top, so the created line can land elsewhere than asked. The
  plan must say so and expect `move_dimension` to settle it;
- **`attributesFile`** decides that offset, so it is part of the decision, not a
  detail of the call.

All five are **required for `Create`**, and required for nothing else —
`viewId` included. It is easy to overlook because it already exists in the
contract, but a `Create` step without it is as incomplete as one without points.

`viewId` currently sits in **three** places, all optional: on the plan result, on
the step, and in the tool arguments. Pick one authority rather than adding a
fourth reading of it:

- `ToolArguments.ViewId` is what the call actually carries, so it is the
  authoritative value for `Create`;
- the copies on the step and the result are context for a reader;
- they must **agree**, and disagreement is a validation error, not a preference
  to resolve silently. Three optional copies of one number is exactly how a plan
  ends up describing one view and executing on another.

`DimensionActionPlanToolArguments` is currently all-optional, which suits
`Combine` and `Arrange`; adding four more optional fields would turn it into a
bag in which no shape is ever wrong and an incomplete `Create` step serializes
happily.

So validation belongs **per action, not per field**: each action declares the
arguments it requires, and a step is checked against its own action. Widening the
existing actions to demand placement fields they have no use for would be the
opposite mistake.

#### Direction is three decisions, not one

All three must be stated explicitly, because two of them cannot be recovered
afterwards:

- the **axis** of the chain;
- the **side**, given as a vector — never as the sign of `distance`;
- the **point order**, which sets the zero the printed run counts from. Read-back
  is always normalized, so the order is invisible once created.

#### Candidates in, one point per position out

Two separate steps, and conflating them is what produces junk segments:

- **candidates** come from `2a` — several per part is normal;
- **the plan** carries one point per grid position, not per part and not per
  candidate.

Two constraints carry over from `2a` and bind here: a box-derived candidate may
not become a `Create` point without marking the step degraded, and a candidate
with no normal can fix a position but cannot satisfy a face-specific rule.

#### What "grid position" means

The term has to be pinned down before it can be implemented, or each producer
will group differently and the disagreement will be invisible afterwards:

- position is measured **along the chain axis only** — the projection onto that
  axis. Two points differing solely across the axis are the same position;
- points within a **tolerance in millimetres, in view coordinates**, are one
  position. The tolerance is a policy value, stated in the plan, not a constant
  buried in the grouping code. It has to be larger than snap noise and smaller
  than the shortest real spacing that must survive — the observed junk segments
  were 15–60 mm, so the working range starts there and needs confirming against
  the cases rather than guessing;
- **coincident and near-coincident points collapse to one position**, and the
  plan records which candidates were collapsed and which one it kept. A cluster
  silently reduced to its first member is the same failure as no grouping at all;
- collapsing must not cross a real gap: if a cluster spans more than the
  tolerance end to end, it is more than one position, however close consecutive
  members are.

Filtering parts by prefix and material type is reliable and stays as it is.

#### The plan's value is its justification

Coordinates cannot be checked by reading them. For every point the plan must say
which part it sits on and why that face; for every part not dimensioned, why it
was left out. That is the only review possible before anything is applied.

Those reasons must be **structured, not a `Reason` string**. A sentence is
readable once and aggregable never: it cannot be counted across the cases,
filtered on, or compared between two runs, so a rule that starts misfiring stays
invisible. At minimum a reason needs a stable code, the objects it refers to, and
the values it was decided on — with free text as an addition to those, not as a
substitute. `Reason` stays for the existing actions; it is not enough for this
one.

#### Gating

- **drawing type gates everything.** The rules are for assembly drawings. Refuse
  on single-part and GA drawings rather than adapting by analogy;
- refuse explicitly on view types outside the supported set instead of silently
  producing a plan for them;
- **do not consume `Role`.** Everything except the control diagonal currently
  comes back `External`, so an overall dimension and an internal chain are
  indistinguishable. Either fix the classifier first or plan without it.

#### Acceptance

Verifying a proposal on a fresh view is weak: the printed run is not in the
context, point order normalizes on read, and text bounds are empty — so the
result cannot be read back and compared with the decision.

**Part of the answer is not derivable at all.** Stated by the user 2026-08-01:
some dimensioning choices differ because of the plant's habit or the production
technology. Those are properties of the factory, not of the drawing, so no number
of captured cases will yield them. The rules therefore split in two, and `2b` must keep them apart:

- **derivable** — a span that restates a part's own size is redundant, a point sits
  on the face bounding an opening, a wall reads from the bottom, a chain prints a
  running total;
- **convention** — how many chains, which carries what, where each sits. This must
  be *configuration the planner is given*, never something it infers. Captured
  examples are local to one plant and their layout conventions do not transfer.

A layout question with no geometric answer is a question to ask, not to guess.

**Exact reproduction is the wrong criterion.** Established 2026-08-01: the user
produced two different acceptable dimensionings of the same wall and stated that
neither is the only correct one. They agreed on which points are redundant, which
face each point sits on, and what the sheet must show; they differed in how the
work was split between chains. A planner that emits the second variant is not
wrong, and a criterion that fails it is measuring the wrong thing.

So acceptance must check the plan against the rules and against what the sheet is
required to show — the opening's position, the reading direction, the absence of
spans that merely restate a part's own size — and treat an exact match with a
reference as sufficient evidence, never necessary.

With that understood, the first version should still be measured against **a
chain a person corrected by hand**. The drawings are already collected.

But the answer is known only after screening. A corrected chain is not
unconditional ground truth — `2a.1` found a point anchored to an imagined
assembly box on the first drawing it was run against, reused by two chains. Run
coverage over the reference chain first and adjudicate every `missing`,
`ambiguous` or `fallbackOnly` point; only the screened chain is the target.

A builder that cannot reproduce a screened chain has nothing to be judged against
on a new view.

"Point for point" needs a definition, or the comparison is either vacuous or
impossible — exact coordinate equality will never hold. A planned point matches a
reference point when **either**:

- it resolves to the **same model object and the same anchor** on it — the
  strong form, and the one to prefer, since it survives the model being edited
  and does not depend on any tolerance; **or**
- it lands **within a stated tolerance in view coordinates**, when the reference
  point carries no usable association. This is the weaker form and must be
  reported as such, not silently counted as a match.

State both the tolerance and which form each match used. A run where most points
matched only geometrically is a different result from one where they matched by
object, and a summary that hides the difference is misleading.

Done when a plan for a captured view matches the hand-corrected chain on that
view under that definition, with a structured reason for every point and every
omission.

Applying the plan is deliberately out of scope until that holds.

### 2c. Defect detection for batch processing

The goal behind this whole line of work is to process drawings in batches. `EW.4-6`
(2026-08-02) is the first drawing where the full check list was run *before*
proposing anything, and it produced a number worth building on:

**The checks found all four defects. Three of the four were then left in place by the
assistant, and the person removed them.** The fourth was acted on, incorrectly.

So detection is not the bottleneck. The bottleneck is that a found defect gets talked
out of — and it gets talked out of precisely where the rules contain the word
*exception*. Each excuse sounded reasonable on the drawing: "exception for an overlay
layer", "the panel is wide, the batten tops must show somewhere", "the chain prints x,
so the number is honest". All three were wrong.

Two consequences that must not be conflated:

- **Speed.** Moving the checks into the bridge is a clear win and is not in dispute.
  `get_dimension_contexts` returned 119,921 characters on a six-chain drawing and did
  not fit the tool limit. A command that returns findings instead of the read model
  costs hundreds of bytes and stops scaling with part count.
- **Correctness.** A detector does not help here at all — it would report exactly what
  was already visible. What helps is making *removal* the default and forcing an
  exception to name a checkable condition instead of telling a story.

There is a second, independent argument for putting the checks in code: on `EW.4-6`
the ad-hoc script and `get_dimension_chain_coverage` caught **different** things. The
script tested points against part bounding boxes, reported the width overall's two
points as "on nothing", and the assistant dismissed them; coverage returned `Missing`
— no candidate at all, not even a box corner — a class the bbox test cannot express.
Two implementations of "the same" check will keep diverging as long as one of them is
rewritten from scratch on every drawing.

#### Step 1 — grade the checks offline, against the captured cases

Write the checks as a pure function over captured JSON: `dimension_contexts.json`,
`parts_geometry.json`, `candidates_*.json`, `coverage_*.json` in, findings out. No
Tekla, no drawing touched.

`cases/dimension_cases/assembly/` already holds five drawings with a human-edited
state (`004604c1`, `5cf600c9`, `8a856c51`, `c5018fe1`, `c5109755`) plus four with an
accepted `after`. Run the function over all of them and compare each finding against
what the person actually changed.

This is the only honest way to decide which defect classes are safe to fix
automatically — instead of deciding it by opinion. **If a check fires on a point the
person deliberately kept, that class is not safe.** The cases are on disk, so this
costs nothing and risks nothing.

#### Step 2 — move the same code into the bridge

`get_dimension_defects viewId`. Logic already graded in step 1; only the data source
changes. Returns findings, never the read model.

Implemented 2026-08-02. The live source reads dimension contexts and part geometry once,
builds candidate coverage once per distinct part, and passes that snapshot to the unchanged
`DimensionDefectDetector`. The bridge command and MCP tool return compact chain summaries,
findings and warnings; neither modifies the drawing. If any part's candidate read fails, anchor
checks are explicitly skipped rather than turning unavailable evidence into `UnanchoredPoint`.

#### Step 3 — auto-apply only the classes that passed step 1

The score on `EW.4-6` argues for acting rather than reporting: found 4, broke 0,
wrongly left 3. Mechanical removal beat the assistant's judgement. But *which* classes
is decided by step 1, not by this paragraph.

Provisional split, to be confirmed or refuted by the run over the cases:

| likely automatic | likely reported to a person |
|---|---|
| phantom anchor (`fallbackOnly` / `Missing`) re-anchored to the nearest real vertex | anything concerning an overall dimension |
| span equal to the part's own extent along the chain | the start point of an absolute chain |
| point far across from its own chain's line | any drawing where the coordinate-space check failed |
| chain whose printed values are contained in another's | a raked top beyond the one worked case |

#### Start points are out of scope for batch

A chain's start point is not recoverable by reading it back — the point order is
normalised. Mass re-creation of absolute chains with the zero on the frame would
therefore be invisibly wrong until something is printed. Report only, until **1a**
lands and the printed rows can be read directly. There is no middle option here: the
choice is "report" or "rebuild them all blind".

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
