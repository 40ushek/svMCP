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

Operational/current-state notes belong in [`README.md`](README.md).

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

Current phase status: v1 complete. The baseline is the stable foundation for the
next phase. What it contains is operational state and is listed once, in
[README.md](README.md); completed milestones and the evidence behind them are in
[DIMENSION_HISTORY.md](DIMENSION_HISTORY.md).

The strategic boundary is what the baseline does **not** claim: it is
assembly-oriented, and it is neither a complete annotation-aware layout engine
nor an autonomous replacement of drafting judgment.


## Validated Runtime Findings

The detailed observations are in
[DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md). Roadmap-level
consequences are:

- validate coordinate-space provenance before source association;
- use ViewCoordinateSystem, not DisplayCoordinateSystem, for the validated
  part-geometry path;
- treat native dimension text position as unobservable unless Tekla exposes it;
- preserve read-back, neighbour-reflow and ambiguity checks in mutation flows.


## Next Phase

### What a dimension attaches to (2026-08-12)

Measured, not assumed, and **not implemented** - no code tests this yet, and the candidate
layers still emit corners only. It changes what selection is choosing between. Every point of
the seven chains on a real assembly view lies on an **edge** of a part contour, and on one
**square to its own chain** - or, where the member is raked and no edge is square, at a
corner where two of its edges meet. Those are the two halves of one rule, not a rule and an
exception: a tilted edge has no single coordinate across the chain, so it can only
contribute where it meets something. Taking corners alone as the unit made three of
seventeen points look unreachable; taking edges alone misses the raked members.

Applied as a filter the square edges leave fifteen X positions and ten Y positions on that
view, and the corners of the raked members add theirs - against 274 candidate places found
by pooling every source. Selection is choosing among tens, not among hundreds.

Diagonals are outside this. A control diagonal checks an assembly's geometry during
fabrication, corner to corner, and is served by `place_control_diagonals`.

The full measurement, its two qualifications and one recorded correction are in the
part-points roadmap under "A dimension attaches to an edge, not to a point".

### Research-Informed Direction (2026-08-08)

Research supports a staged design: semantic selection first, typed completeness
checks second, deterministic placement third, and stochastic optimization only
as a later tie-breaker. Details and sources are in
[DIMENSION_RESEARCH.md](DIMENSION_RESEARCH.md).

Whatever structure the placement layer eventually takes, measurement semantics
must stay separate from placement geometry: a layout inconvenience must never
delete a correct dimension. Two constraints came out of the review and are
recorded in [DIMENSION_RESEARCH.md](DIMENSION_RESEARCH.md) — identity must be
semantic rather than Tekla ID or point index, and several sources at one point
is a policy decision, not an error.

The roadmap records decisions and acceptance criteria; it does not port any
paper's algorithm directly.


The foundational architecture cleanup is largely complete.

The next phase should improve naming, semantics and richer layout support on top
of the new baseline.

Priority order:

### 0. Capture observations in one call — done

Implemented 2026-08-01 as capture_dimension_observation. It is read-only, binds
the existing view and dimension payloads to one capture header, and does not
persist files. Details are in [DIMENSION_HISTORY.md](DIMENSION_HISTORY.md).


### 0b. Keep per-segment source relations nested, not flattened — baseline done

Per-segment related sources and readable DimensionLink endpoint IDs are retained
in the observation/read model. The legacy flat list remains for compatibility.
Further DimensionLink coverage is only needed when a real drawing uses such
links.


### 0c. Share cached view-part geometry across readers

The view context, chain coverage, defect detection and placement paths all pass
through `GetAllPartsGeometryInView`, while `GetPartPointsInView` currently reads a
single part through a separate solid path. A single analysis can therefore read
the same solid geometry more than once.

Target:

- add a shared `DrawingGeometryCache` in the long-lived `TeklaBridge` scope;
- key cached view geometry by `drawingId`, `viewId` and
  `Drawing.UpToDateStatus`, with entries indexed by `modelId`;
- make `GetAllPartsGeometryInView` populate and reuse the cache;
- make `GetPartGeometryInView` / `GetPartPointsInView` consume the same cached
  `PartInView` and derive points without another `GetSolid()`;
- expose cache hits/misses and solid-read counts through `PerfTrace`.

Before implementation, run a live positive invalidation test: move a model part,
read `Drawing.UpToDateStatus` immediately afterwards, and verify that the status
changes in time to invalidate the cached view geometry. The negative test is also
required: editing a dimension must leave the status unchanged. If the positive
test fails, `UpToDateStatus` cannot be the sole cache version; add a model
revision/fingerprint or disable reuse after model edits.

Invalidation:

- clear on `open_drawing`, `close_drawing` and `update_drawing`;
- clear affected view entries after the explicit view-mutator list:
  `move_view`, `set_view_scale`, `fit_views_to_sheet`, `arrange_views_only`,
  and any future view rotate/transform/recreate command;
- treat a changed `UpToDateStatus` as a cache miss;
- do not clear solely because dimensions were edited when the drawing status is
  unchanged.

The current bridge processes stdin commands strictly sequentially, so the first
implementation may assume one-threaded cache access. This is an explicit
assumption, not a concurrency guarantee: if bridge dispatch is parallelised,
the cache must gain a lock or an immutable/concurrent implementation before that
change is enabled.

`viewId` may be reused after a view is deleted and recreated. Each entry must
therefore retain a cheap view fingerprint (at least view type, scale and
`ViewCoordinateSystem`; include bounds when available). A fingerprint mismatch
is a cache miss even when `drawingId` and `UpToDateStatus` are unchanged.

The cache must not expose mutable internal lists to consumers. Direct one-shot
`TeklaBridge.exe` CLI calls will not share entries across processes; the benefit
is for the persistent `--loop` bridge session.

Done when the positive and negative `UpToDateStatus` live tests pass, the explicit
view-mutator list is covered by invalidation tests, repeated reads reuse one solid
read per `(drawingId, viewId, modelId)`, view recreation cannot hit an old entry,
the bridge sequencing assumption is documented in code, and the measured
`GetSolid()` count and elapsed time are lower on a repeated read while producing
byte-equivalent geometry-derived results.

### 1. Clarify orchestration naming — done

DimensionActionPlanBuilder and get_dimension_action_plan are the stable names;
the previous command remains an alias for compatibility.


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

Confidence must degrade honestly. Evidence intended for placement has two levels:

```text
face / exact contour > bounding box
```

This is the intended fitness ladder, not a description of the code today. The
convex hull sits in neither level: it bridges a concavity, a cut-out or an
L-shaped re-entrant edge, so a point derived from it is not evidence that the
point lies on the part at all, and it has no stable native anchor either. Since
real contours became available it fills no place a real contour does not fill
better. The parts hull was removed from the view context on those grounds.

What is actually in the code, and stays there for now:

- `DrawingPartCandidatePointBuilder.Build` still emits `HullVertex`, and
  `get_part_candidate_points_in_view` still returns it. Legacy, kept because
  removing it is a separate change with its own tests to update.
- `BuildFromContours`, the contour layer, does not use the hull at all.
- `DrawingPartCandidateConfidence` still has four values. A contour point is
  `DerivedGeometry` and not `ExactGeometry`: the union runs at a tolerance that
  moves boundaries, so the corner is an accurate place on the drawing without
  being a feature the model would name.

The obligation this puts on whoever first builds a combined view-level set:
discard `HullVertex` there, and do not wait for it to be deleted at the source.

A bounding-box point is still a legitimate degraded fallback, and must stay
distinguishable: box-derived candidates must not silently become `Create` points;
they require a later exact-contour check or an explicit degraded
decision. The difference between a contour-derived point, a hull point that is
still being emitted, and a box fallback must survive into the plan rather than
being averaged away.

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

get_dimension_chain_coverage records candidate matches, confidence, status and
ambiguity without selecting a winner. Candidate selection remains the
responsibility of the placement planner.


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

get_dimension_defects is implemented as a compact, read-only detector over a
shared snapshot. Remaining work is to grade automatic actions against captured
cases and apply only defect classes that pass that grading. The empirical basis,
EW.4-6 result and provisional action boundary are preserved in
[DIMENSION_HISTORY.md](DIMENSION_HISTORY.md).


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

### Where dimension positions come from (2026-08-09)

Worked out against one real elevation, `[EW.1 - 1]` of `Midi_1-1-1`, by comparing
computed positions with what its chains actually measure. Nothing below is built
yet; this records what to build and what has to be checked before building on it.

**The primitive is an interval, not a point.** Collapse a part onto the chain's
direction and it becomes `[from, to]`. Its width says whether it marks a position
at all, its ends serve dimensions taken to faces, its middle serves those taken
to centres — and the choice between the last two need not be made while
projecting. On the wall this was tried on, widths came out 0, 45, 60 and 120
against 1010 and 2050: members against background, an order of magnitude apart.

**Parts that abut fuse into one block.** Two studs nailed together read as one
post, and the drawing locates the post, not the seam inside it. Merging is
transitive, so three or five in a row need no special case. Three separate rules
collapse into this one operation — do not dimension between adjacent studs, the
doubled post, and the outer face of a block giving the overall.

Fusion is per axis. A stud and the plate it stands on touch, but along X the
plate runs the whole length and is background, not a position; along Y the two
swap roles. So two parts fuse along an axis when both are narrow along it, their
intervals meet within the gap tolerance, and they actually touch.

**Positions are block boundaries**, and the chain marks where each next block
begins, scanning from the hooked end. Not the left face nor the right: the first
face the tape reaches. A trailing face starts nothing.

**An empty run between blocks is an opening**, dimensioned clear between the
faces flanking it.

#### Shape

Per view and per axis, ordered along it — a sorted list, not a graph, because
order is the whole content:

```
AxisLayout
  ViewId, Axis
  Blocks[]      From, To, ModelIds[]     ordered
  Background[]  From, To, ModelIds[]     wide along this axis
  Gaps[]        From, To                 candidate openings
```

Two instances per view. Background is kept, not discarded: it fixes the extent
and explains why a member is not a position. Positions are derived from the
block boundaries rather than stored, so there is one source of truth. Gaps are
named because an opening is a thing people talk about.

#### What has to be checked first

Compute `AxisLayout` for both axes of that elevation from `BboxMin`/`BboxMax`,
which the view geometry API already returns in view coordinates, and compare:

- X against `0, 60, 1040, 1635, 2050`
- Y against `-1294.8, 740.2, 855.2, 1089.8, 1294.8`

Y is the stronger test: along it the plates become positions and the studs
become background, so a threshold fitted to X will fail there visibly.

#### The open question

What counts as narrow. The order-of-magnitude gap that separated members from
background is one wall. A noggin spanning 540 mm on a 2050 mm wall does not fall
either side of it. Do not fix the threshold from a single drawing — the same
debt as the contact comparison.

#### What contacts are still for

Boxes cannot answer whether an abutment is real. A view collapses depth:
sheathing in front of studs covers them on both axes and would fuse the whole
wall into one block. That did not surface on this elevation only because the
sheathing is hidden there and the frame is all that remains; on the plan, with
every layer shown, it would. So intervals propose positions cheaply and contacts
reject false fusions, and neither replaces the other.

The ladder is: bounding boxes, then the view outline for a raked or cut member
whose box corner sits in space, then contacts. Most cases stop at the first.

### Architecture reset: geometry before dimension policy (2026-08-11)

The next implementation is not another extension of `get_dimension_defects`.
That command audits annotations already present in a drawing and remains needed
to validate the dimensions placed later; it is simply a different task from
creating them.  The first step sideways is to collect reliable geometric facts
in the drawing view coordinate system, before deciding which facts deserve a
dimension.

#### 1. `AssemblyOutline` — the first concrete result

For one drawing view, first project each visible part's faces onto the view plane
and union those face projections with Clipper into that part's real 2D contour.
Then union the part contours with Clipper into the assembly outline.  The result
must preserve outer rings, holes and multiple disconnected components, together
with the real vertices and extrema that produced them.  It represents the
visible external polygon of the assembly in that view.

`ViewHull` is a convex hull, not a real part contour, and must not enter either
union.  As with an OBB, it bridges concavities, openings, raked ends and
cut-outs, so its corners are often points in empty space.  A convex hull may
later be useful only as a coarse bound or fallback; it is not a source of
dimension points.  Clipper is already available through `SolidContacts.Core`,
so this needs no new dependency.  This step finishes by comparing the computed
outline with real drawing views.  It does not create dimensions.

#### 2. Candidate facts — still no dimension policy

Extend the existing `DrawingPartCandidatePoint` model and
`DrawingPartCandidatePointBuilder`; do not introduce a second candidate type.

Implemented 2026-08-12: real per-part and assembly-contour vertices from step 1 pass
through `BuildFromContours`, not `ViewHull`. `PartContour` candidates retain their one
part; `AssemblyContour` candidates intentionally retain none. Their ring metadata says
whether the point is on an outer boundary or a hole. This is a separate contour-facts
layer, deliberately not yet combined with the older per-part candidates.

Implemented 2026-08-12: contacts are a separate `Contact` source with a stable
individual identity, both participants and a contact-id-based anchor. Their read
completeness stays explicit. They remain facts about a junction, not a reason by
themselves to emit a position in a dimension chain.

The sources do not become one automatic view-level collection. A later policy may
explicitly combine particular compatible chains, but only after each semantic group has
been calculated separately. `ViewHull` may remain a clearly degraded legacy diagnostic
if separately useful, but does not enter this route. Part OBBs may be used internally
for a broad-phase optimisation, but are never candidates themselves.

#### 3. Four preliminary chains from the structural box — proposed, not implemented

This is the smallest first proposal for an ordinary assembly view with horizontal and
vertical chains. It does not select the final dimensions or create anything in Tekla.

1. Take `minX`, `maxX`, `minY` and `maxY` from the structural outline: the extent of
   `Defining` parts, not every visible layer.
2. Seed four preliminary chains with those two extremes:

   ```text
   top:    minX ... maxX          bottom: minX ... maxX
   left:   minY ... maxY          right:  minY ... maxY
   ```

   They are the four outer sides of the assembly box. At this point each is only an
   overall dimension. Top and bottom chains carry X positions; left and right chains carry
   Y positions.
3. For every `Defining` part contour, inspect its real edges and vertices, including hole
   rings where the part has them. A vertical edge contributes its X coordinate; a horizontal
   edge contributes its Y coordinate. A tilted edge contributes no coordinate by itself:
   both coordinates vary along it. Its two endpoints are nevertheless real corners and may
   contribute there, where the tilted edge meets another edge.
4. The side is chosen by the actual source point, not by copying a coordinate. For a strict
   vertical or horizontal edge, its endpoints are the source points. A point low in the view
   feeds the bottom chain and one high in it feeds the top; left and right likewise. A stud
   therefore contributes its X to both top and bottom through two different endpoints, which
   is not the same as copying one coordinate across.

   Confirmed on the measured view: every point of the bottom chain lies at the bottom of
   its part and every point of the top chain at the top, and the left and right chains
   divide the same way by X. This confirms where a source point belongs; it does not claim
   that the human drawing used every position of the preliminary symmetric set.

   Coalesce equal coordinates before forming a chain. Several parts sharing one face, or a
   raked corner agreeing with a square edge, make one position rather than several
   coincident dimension points.

Only the structural box supplies the four outer extremes. A part box is not used: on a
raked or cut part its corner can be empty space, and its extremum can lie on a tilted edge
that has no one coordinate to dimension to. This is why `ViewHull` and OBBs are still
forbidden as dimension evidence.

The result is four preliminary chains near the parts they describe. Later policy may
remove a second face that only restates a part size, add opening faces, or decide that a
side should hold only its overall. Those are policy decisions after this geometric
proposal, not reasons to duplicate every coordinate on both sides.

#### 4. Keep semantic geometry groups separate — proposed, not implemented

`GeometryGroup` is the geometry snapshot for one semantic group. It stays with the work
after reading, rather than being a disposable argument to one calculator. It contains the
group identity, its optional outer boundary, and its projected shapes. After
`CalcDimensionChains.Apply(group)`, it also contains that group's working
`DimensionChains`.

`CalcDimensionChains` is a local calculation over **one** `GeometryGroup`. It does not
read Tekla, decide a part's role, create dimensions, or merge chains. It calculates the
initial, deliberately over-complete chains from the snapshot. Policies and AI skills then
change only `group.DimensionChains`; `Boundary` and `Shapes` remain the unmodified
geometric evidence used to explain or reconsider every change.

Working state must say what has happened to it. `DimensionChainSet.Stage` distinguishes
`Calculated` from `PolicyApplied`. More importantly, every `DimensionChainPosition` retains
its geometric sources and carries a policy disposition with a reason: initially
`Calculated`, then explicitly `Kept` or `Removed`. A policy or skill must mark a position
removed rather than silently deleting it, so a later reader can answer both why one
position remains and why its neighbouring position does not. A position is one coordinate
along a chain direction, with one or more geometric supports; it is not yet the two-
coordinate point passed to Tekla. Tekla creation consumes only kept positions and makes
that point at its final placement. A future policy-created position must carry equally
explicit evidence; it may not be an unexplained coordinate.

Naming boundary: existing `DimensionChainCoverageResult`,
`DimensionChainCoverageBuilder`, and `DimensionPointCoverage` audit a chain that Tekla
has already drawn. The `DimensionChain` objects discussed here are calculated candidate
chains, before a drawing policy decides whether any dimension should exist. Do not use a
coverage type for `group.DimensionChains`, or call its positions coverage.

The structural group is the first caller: its boundary is the structural assembly outline
and its shapes are the contours of `Defining` parts. Future groups may instead be named
`wood-frame`, `electrical`, `contacts`, or `bolts`. A group is a deliberate semantic
choice by the caller, never an inference from material type or an automatic mixing of
all visible objects.

`GeometryGroup` is a concrete data object, not an interface. Each group shape wraps the
existing `PlanarShape` with the topology and provenance the calculator needs. In
particular, a shape made from an `OutlineTreeNodeResult` retains its `IsHole` flag beside
the `PlanarShape`; putting it into `PlanarShape` itself would make a drawing-contour fact
part of the common contact geometry vocabulary. Every shape therefore keeps its honest
form without pretending that all evidence is an area: a part contour is a `Polygon`, a
contact can be a `Polygon`, `Segment`, or `Point`, and bolts are normally `Point`s.

When a caller has no real outer contour, the calculator may derive scalar X/Y extrema
from its shapes, but it must not manufacture the four corners of their box. Each emitted
extremum still needs a supporting point on a real shape. This is safe for a group of bolt
points, and prevents a polygon or segment group from dimensioning to an empty box corner.
Such a derived extent is not an outline and supplies no skew evidence. An interface may
later be useful for a component that *supplies* several groups; it is not useful at the
calculator boundary.

Each group receives its own four preliminary X/Y chains. No result from one group is
silently added to another, even when coordinates coincide. A later policy may explicitly
combine selected compatible chains, such as `wood-frame.bottom` with
`contacts.bottom`; at that time it must preserve every source that supported a shared
coordinate. Combining is a decision about what the drawing should say, not geometry
collection.

The outer boundary of a group also retains future skew evidence: a substantial tilted
outer edge can later supply the direction of an aligned chain. That does not authorize
skew-chain generation yet; the rule for which tilted edges deserve a chain still needs
evidence.

This calculation is deliberately usable for both drawing subjects. An assembly adapter
will build a group from the selected structural parts; a single-part adapter will build a
group from one part contour and its hole rings. Neither `GeometryGroup` nor
`CalcDimensionChains` carries an `Assembly`/`Part` switch: both calculate geometric facts
only. The difference begins afterwards. `AssemblyDimensionPolicy` locates parts and may
remove a span that merely repeats a made part's size; a future `PartDimensionPolicy` must
instead describe that size, holes, cut-outs and other fabrication features. Do not apply
assembly rules to a single-part drawing.

There is no policy interface yet because only the assembly policy has evidence. When a
single-part policy is supported by real cases, the two policies may share an explicit
contract over calculated chains. The calculation boundary stays concrete either way.

#### 5. Return to dimension placement only after the facts exist

Only then should a policy select points, form chains and decide which dimensions
are necessary.  `AxisLayout`, contacts and later collision/placement logic are
policy layers on top of these facts, not substitutes for the geometry-collection
step.  The preceding bounding-box ladder documents the current layout approach;
it is not the implementation order for this reset.

## Deferred / Non-Goals

The following are intentionally not part of the current baseline.

- turning `arrange_dimensions` into a full annotation layout engine immediately
- making text geometry the primary grouping semantic
- exposing all debug/orchestration surfaces as public MCP tools right now
- letting AI bypass the deterministic baseline and operate on raw Tekla DTOs
- re-centering the module around DTOs, bounds or orientation summaries
- transactional unification of combine commit and arrange handoff in the current phase

## Runtime notes and limitations

Detailed LengthList, native text, angle-dimension and transport constraints are
in [DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md). They are
implementation constraints, not roadmap milestones.


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
