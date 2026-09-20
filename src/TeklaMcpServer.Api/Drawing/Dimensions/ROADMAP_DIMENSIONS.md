# Dimensions Roadmap

## Increment: verified writes and one-chain plans (2026-09-19)

Implemented in code, not yet live-validated: shared create/read-back/delete
protocol and explicit preview/apply commands over `DimensionChainSet` for a
single steel location chain. See [current contract](README.md#verified-writes-and-structural-plans-2026-09-19).

The plan keeps reference ownership separate from closure endpoints. Kept and
Removed decisions carry reasons; points are selected from calculated supports,
not reconstructed from bbox. No new automatic drafting policy was introduced.

The active order of work is below. The older numbered proposals later in this
file are background/backlog, not another competing implementation sequence.

## Increment: faster, safer one-chain placement (2026-09-19)

Planned, not implemented. Live runs on M.49/M.48 (sections C/E, back view
`Hinten`) suggest that line placement and manually reviewing too many candidate
positions are the main avoidable costs. The work below first makes placement
semantics trustworthy, then reduces reads and adds a conditional template.

**Observed behavior (TS2025; do not generalize beyond the tested cases yet):**

- The initial observations were the overall chain on M.48 `Hinten` and the top
  and left chains on section E (a wider probe run is recorded below). In those
  cases, the physical offset
  appeared tied to Tekla's geometric base point: leftmost for a horizontal chain
  and lowest for a vertical one, regardless of input enumeration order. On
  M.48 `Hinten`, points passed right-first:
  `InitialDistance` 949.95 = 389.95 + 560; the screenshot showed the line at
  y~230 inside the girder. On M.48 section E, `distance` 120 from a flag edge
  (y 86.8) put the line inside the end plate; 460.25 moved it out. No diagonal
  chain or tied-base case is represented in these observations.
- This placement base is **not** the chain datum. `points[0]` remains the
  requested start of a new chain; running dimensions must follow the main-part
  start rule or an explicit reverse direction. Sorting must not silently change
  that datum.
- The current verified writer corrects and checks the stored `Distance`, points,
  direction and (where applicable) datum; it does not prove the rendered line
  is at the expected location. The reader's `referenceLine` is reconstructed
  from points and `Distance`, not an independent observation of the drawn line.
- **Presentation Model line read is confirmed for dimension segments, not sets.**
  In the live TS2025 drawing `Binder`, section E (`viewId=6093`, scale 1:10),
  `GetObjectPresentation(dimensionSetId)` returned `null` for the two tested
  chains (`22198` top, `22171` left). Calling it for each `StraightDimension`
  segment instead returned the rendered primitives: dimension line, extension
  lines and text. The reported paper-space line coordinates were `y=54.705`
  for the top chain and `x=-38` for the left chain; multiplied by 10 they
  matched the independently expected `y=547.05` and `x=-380`. Segment text
  values matched the visible dimensions. Thus the API can provide an independent
  line observation in this tested case, without reading reconstructed
  `referenceLine` or relying on a screenshot. Read each segment, not its parent
  set. This confirms the route only for these two chains at 1:10; other sides,
  scales, point orders and tied-base cases were not covered by this first run.
  The `null` for the set id is real API behavior in that run: the segment reads
  worked in the same process. (A separate sandbox-launched probe that could not
  reach the Presentation Model gRPC endpoint (`Ping` failed) is a connection
  failure and says nothing about geometry.)
- **Wider probe run (55 of 65 dimensions compared).** With `DIMENSION_PROBE_ALL=1`
  the probe read every dimension on the drawing (1:5 and 1:10 views, all four
  sides). Drawn line = base point (leftmost of a horizontal chain, lowest of a
  vertical one) +- `distance` in view units matched 36 of 55. Base and outermost
  point coincide in 20 of those 36, so only the other 16 tell the two apart; in
  those 16 none matched the outermost point, and no dimension matched the
  paper-mm reading. Only 3 dimensions were 1:5; 1:3, both point orders and tied
  bases are still untested. The other 19 were vertical dimensions past a break
  of a long view: the expected-minus-drawn shift is constant inside a stretch
  and jumps between stretches. The shift levels by stretch were 3419.5, 7733.5,
  13229.5 and 13615.6 (these are levels, not jump sizes; the jumps differ in
  size). This points to a break of the view, not a different base
  rule, but the offset from the base for those 19 was not checked separately.
  The 10 dimensions not compared: 65 were in the probe log, 4 were not listed by
  the dimension reader (ids 11224, 11218, 11216, 11222) and 6 were not straight
  horizontal/vertical sets (ids 11226, 11220, 1532, 13269, 11276, 2205).
- TS2025 `Tekla.Structures.Drawing.xml` documents `StraightDimensionSet.Distance`
  in paper millimeters. Live measurements disagree: across the tested 1:5 and
  1:10 views, `Distance` behaved like view/model units (paper offset × view
  scale); for example, 220 at 1:10 produced a 22 mm paper offset, 460.255 gave
  46 mm and 80 gave 8 mm (three 1:5 dimensions scaled the same way). Keep this as
  measured TS2025 behavior, not a universal rule, until independently revalidated.
- Same side of a raked part at both ends is user-confirmed and recorded in the
  skill. Compact answers shipped in commit `723e66d`; measured payloads were
  2,793 vs 10,565 chars for parts, 13,439 vs 18,201 for section C chain
  positions, and 39,373 vs 62,926 for `Hinten`.

**Work, in this order:**

1. **Characterize and fix write/read verification.** Use
   `GetObjectPresentation(segmentId)` for each `StraightDimension` segment as
   the independent rendered-line observation; the parent
   `GetObjectPresentation(dimensionSetId)` returned `null` in the live test.
   The segment route is confirmed for the top and left chains on section E at
   1:10, including dimension/extension lines and text. Complete the bounded
   side/order/scale/tie matrix below before generalizing that result. Do not
   treat reconstructed `referenceLine` or `Verified: true` as independent
   verification.

   Use a bounded matrix, not open-ended experiments: four sides (`Top`/`Bottom`
   horizontal, `Left`/`Right` vertical), both input point orders, and scales
   1:3, 1:5 and the recorded 1:10 case. Include equal-coordinate/tied-base
   cases. Compare requested side, actual line coordinate and API `Distance`.
   This increment covers straight axis-aligned chains only; diagonal control
   dimensions via `place_control_diagonals` are explicitly out of scope and
   need their own characterization. Preserve the explicit chain datum in every
   case. Do not build an applying helper until the matrix and an independent
   line observation pass on an isolated test copy.
2. **Add `sides` filtering to chain-position reads.** Return only requested
   sides, but calculate the full unfiltered source first. `sourceFingerprint`
   must remain the fingerprint of that full source; `positionIndex` and every
   `supportIndex` must remain indices into the corresponding unfiltered side
   and support list. Add tests comparing filtered entries and indices with the
   same sides in an unfiltered response, and prove a plan made from those indices
   still resolves. Keep `GroupExtent` available for overall dimensions; do not
   drop it merely because it has no single part owner.
3. **Keep the plate-and-flags template explicitly unvalidated.** The proposed
   sequence (top: flag–plate–flange–plate–flag; left: plate–flag–plate) comes
   from one view, M.48 section E. The three reference cases below—symmetric end
   plate, offset plate, intermediate stiffener—do not include flags, so they can
   test the general intent planner but cannot validate this template. Do not
   apply the sequence as a general section rule. Until additional independent
   views with the same confirmed part/role/support pattern validate it, keep it
   as a hypothesis for the M.48 section E test-copy only; do not auto-activate
   it on another view. A `SectionView` label alone is not a match.
4. **Reduce the displayed candidate set only for a matched template.** The
   roughly 10 positions are a starting hypothesis from that one geometry, not a
   general rule. Do not reduce candidates until the template is validated or
   explicitly limited to that exact view pattern. The plan must still classify
   each source position as kept or removed with its reason, but the compact
   response should summarize removals by reason; expose per-position detail only
   on request. Keep the full source available for review. Coordinates continue
   to come from `get_structural_chain_positions`; contacts may support a
   keep/remove decision but never supply a coordinate.
5. **Implement `place_chain` only after step 1 passes.** Scope: horizontal and
   vertical chains only, not `place_control_diagonals`. Inputs: view, side,
   selected supports, explicit datum/start direction, and gap in paper mm. The
   calculator must define the signed side normal, convert paper distance using
   the verified view scale/units, and convert the target line location to the
   API's `Distance` using the characterized semantics. Do not sort away or
   replace the declared datum. Return the planned line and datum; apply only
   through the corrected writer.
6. **Use a conservative contour guard where geometry is trustworthy.** Reuse
   `ViewDepthWindow.Read`'s `RestrictionBox` snapshot and the solid bounding
   boxes already read by `DrawingViewParts.GetDepthFilteredParts`. The current
   `DepthBox.Classify` is only an overlap test (`Disjoint`, `BoundaryTouch`,
   `Overlaps`, `Invalid`); it does not report full containment. This is the
   known gap documented in `ROADMAP_SECTION_CROSS_SECTIONS.md` and in the
   comment beside `ProjectedOutlineBuilder.BuildPart` in
   `TeklaDrawingAssemblyOutlineApi.cs`.

   For the pilot, extend the per-part depth result with an explicit containment
   outcome computed from the already-read view-space `solidBox`; do not select
   the part and read its solid a second time just to repeat this test. A solid
   bounding box fully inside the `RestrictionBox` is a conservative sufficient
   proof for trusting that plate/flag outline. A box that is not contained is
   untrusted, not proof that the solid itself protrudes. Mere overlap is not
   enough. Exclude the unclipped main beam. A collision with this trusted subset
   may reject an inside line. A clear result proves clearance only from that
   subset, not the whole assembly, so report it as partial and retain visual
   verification or a complete clipped contour for global clearance. Check the
   view frame separately as a clipping/visibility constraint, not as a second
   assembly boundary.
7. **Re-place the affected chains on isolated test copies** of M.49 section C,
   M.48 section E (template pilot), and M.48 `Hinten`—not on the working
   drawings. Record the copy/drawing identity used for each run. Proceed only
   after the preceding gates pass.

Out of scope: repairing section-depth geometry itself. A bad or incomplete
contour must produce `not verified`, not a guessed placement decision.

**Acceptance gates:** tests cover the bounded side/order/scale/tie matrix and
preservation of the explicit datum; filtered results retain the unfiltered
indices and fingerprint; the three reference cases test the general intent
planner, while the plate-and-flags template remains disabled for general use
until independently validated; compact candidate summaries retain full detail
on request; containment distinguishes fully-in-box details from mere overlaps;
the trusted-subset guard can reject an inside line but never claims whole-
assembly clearance; an isolated-copy write confirms actual line placement
independently of reconstructed `referenceLine`. Do not change
`CalcDimensionChains` or silently redefine its candidate geometry as part of
this increment.

## Active direction: measurement intent before point selection (2026-09-19)

**Decision:** keep `GeometryGroup`, `CalcDimensionChains` and `DimensionChainSet`.
They provide candidate coordinates and their evidence, not a decision about
which measurements the assembly needs. Do not replace them with another bbox,
OBB or contact-driven selection algorithm to solve a semantic selection problem.

The decision flow is:

`measurement question -> reference + subject -> required relationships -> supported chain points -> placement -> verification`

This is the order of reasoning, not a requirement for six separate API calls.
Read geometry once where possible and reuse it for the questions in scope.
For example, "locate this plate against the beam profile in X" is a question;
"retain four coordinates" is only one possible representation of its answer.

### Current status, not inferred readiness

| Component | Status | Does not yet prove |
|---|---|---|
| Preliminary structural chains | Implemented; carry positions and owner supports | That a selected chain answers the fabrication question |
| `StructuralDimensionPlanBuilder` | Implemented validator for a supplied one-chain steel `PartLocation` plan | Automatic intent selection or sufficient dimensions; reasons are still free text |
| Preview/apply bridge + MCP | Implemented, experimental; Relative/Create only | Deployment or successful live Tekla operation; retain/replace and duplicate suppression are absent |
| Verified create/recreate protocol | Implemented; injected failure tests | Live read-back/reflow behaviour, live compensation, rollback after the original delete started, or verification of every style property |
| `dimension-drawings` skill | Updated: scoped work, current-data reuse, targeted reads, no default overlays | Measured runtime improvement or better drafting results on live cases |
| Intent/relationship coverage planner | Proposed below; not implemented | Anything about whole-view or whole-assembly completeness |

### Write failure handling: compensation before the original is touched

`DimensionWriteProtocol` is still non-transactional, but it compensates where
that is safe. `create_dimension` and the replacement inside `recreate_dimension`
are committed before they are read back. If the write fails **before the
original is deleted**, the new set is deleted, the commit is repeated and its
absence is confirmed: `writeState.newDimensionRemoved = true`, the result's ID is
0 and the original (for recreate) is untouched. If that cleanup fails,
`writeState.cleanupError` is set and the error text says the new set may still
be on the sheet; the caller must re-read the view before retrying.

Once the original delete has been attempted nothing is undone: deleting the
replacement then could leave no dimension at all. A later deletion/commit/
read-back failure can leave both dimensions or an uncertain state. Callers keep
the returned IDs/state, re-read the view before retrying and must not assume a
failed response removed either object.

Covered by injected-failure unit tests only (`DimensionWriteProtocolTests`); the
compensation path is not yet exercised on a live drawing. Exercise it only on a
test drawing; do not induce failures on a production sheet.
`add_dimension_points` and `combine_dimensions` keep their own cleanup/rollback
paths and are not routed through this protocol.

Previous verification recorded 959 passed / 0 failed / 1 skipped in the full
suite and a successful bridge build. Those are code-test results, not live
drawing acceptance. No new live result is asserted by this roadmap update.

### Boundaries to keep

- The LLM/operator identifies the measurement question and resolves plant
  conventions. Code performs repeatable validation, point resolution, writing
  and read-back. Gradually encode only rules supported by the cases; do not
  keep enlarging the skill to compensate for missing deterministic checks.
- Reference, closure and datum are separate choices. A transverse plate chain
  may close on plate edges and locate them against the main-profile edges.
  A plate-size-only chain cannot satisfy a plate-location requirement.
- Part ownership is necessary but insufficient: including both part IDs does
  not prove the chosen faces/axis provide the required location or orientation.
- Reuse the existing plan as the execution boundary; evolve/adapt it to the
  existing domain/context model. Do not create a competing public plan DTO
  merely to name intent. Position/support indices are snapshot-local handles,
  not durable semantic identity or keys for matching existing dimensions.
- Keep three results distinct: source geometry complete, requested measurement
  requirements satisfied, and write/read-back successful. None implies the
  other two. One completed chain cannot certify an entire view.
- Full-solid projection beyond section depth remains an accepted known gap.
  Current section/end-view visual checks remain required; changing the planner
  does not make the contour a verified clipped section.
- Bolts retain their separate data/logic roadmap:
  [ROADMAP_BOLT_GEOMETRY.md](../Geometry/Bolts/ROADMAP_BOLT_GEOMETRY.md).
  Structural chains do not supply bolt coordinates by implication.

### Next steps and acceptance gates

**0. Live smoke test of the implemented path, before expanding it.**

On an authorized test drawing, record drawing/view identity and existing IDs;
preview one horizontal plate-location chain with the agreed settings, inspect
it, apply once, and read back the chain plus affected neighbours. Repeat in the
vertical direction. Test raw recreate separately: structured apply does not
support replacement. Verify unavailable attributes stop before creation and
offset correction is actually reflected on the sheet. Exercise safe failure
paths on a test copy where possible; retain injected tests for failures that
cannot safely be induced live. Do not describe them as live-tested.

Pass only with correct own-support XY points, side, rows, offset and neighbour
state, and no unexplained extra objects. On failure preserve returned IDs and
state, inspect before retry, and fix the demonstrated cause. No blind apply
retry or implied rollback. Do not touch a production drawing just to run this gate.

**1. Define three small reference cases before implementing new selection.**

For each, record the measurement question, reference/subject, axis, policy,
actual support identities, acceptable answer(s) and a deliberately wrong plan.
Obtain values from the case, not from historical M.505 example numbers.

| Case | Required answer | Negative case that must fail |
|---|---|---|
| Symmetric end plate | Location relative to the main profile in each requested axis; explain equal offsets | Plate width/height alone presented as location |
| Offset plate | Correct unequal offsets in the affected axis; preserve the measured asymmetry under the stated policy | Reusing the symmetric template despite changed supports |
| Intermediate stiffener | Its longitudinal station from the specified main-part datum; other relationships only if requested/required | Stiffener thickness alone presented as its station |

These cases validate the named relationships, not complete 3D placement or
orientation of every part. Bolt-pattern orientation is a separate requirement
when needed. Different equivalent chain layouts may pass; exact reproduction
of an old drawing is not the criterion.

**2. Add intent and coverage to a read-only planner.**

Proposed semantic information (not fields already present in the API): the
measurement question/purpose, subject, reference feature, measured axis,
required relationship, policy and supporting evidence. Record each requirement
as satisfied by specific chain spans, intentionally not required by a named
rule, or unresolved with a reason. Free-text `Purpose`/`Reason` alone is not a
coverage proof; add stable reason codes and evidence as the cases justify them.

Resolve those relationships against existing chain supports, keeping each
point's own cross-axis coordinate. If no suitable support exists, report the
missing capability rather than synthesizing a bbox point. Contact facts can
support a choice but neither force a dimension nor replace its coordinates.
Keep selection separate from line placement: a layout collision cannot silently
remove a required measurement. Preserve explicit Internal/row-type choices;
do not switch them to suit the prototype.

Pass when the read-only planner satisfies all three positive cases, rejects
their negative cases and leaves incomplete/ambiguous evidence unresolved.
Test shifted geometry, missing supports, multiple possible references and
policy changes. No automatic whole-drawing placement at this stage.

**3. Extend execution only after those gates.**

Add semantic matching of existing dimensions, retain/replace actions and
duplicate-safe retry handling before batching. A preview token is not an
idempotency key. Then add all-side/whole-view outcomes and explicit running-row
datum support, each with its own tests and live acceptance. Share a stable
contract with orchestration through an adapter/migration, not a second planner
whose independent outputs can disagree silently.

### Efficiency must be measured, not assumed

For the same reference cases and runtime, record before/after wall time,
bridge reads/writes, solid-read counts where instrumentation exists, response
volume and retries. Record model token usage only if actually available.
The shorter skill is not a measured speedup. Target fewer redundant reads and
no default debug objects while preserving verification and correct dimensions.
Do not remove safety reads to hit an arbitrary call-count target, or promise
a cache benefit without invalidation tests.

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

The dated proposals below preserve design history and longer-term backlog.
For current priority and acceptance, use **Active direction** above. In
particular, historical `2b` is not a second planner to implement in parallel.

### What a dimension attaches to (2026-08-12)

Historical observation: the "not implemented" statement below described the
2026-08-12 candidate path. `CalcDimensionChains` subsequently added edge/corner
supports (see the implemented 2026-08-13 section); it still does not decide
whether a selected support answers a measurement requirement.

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

Historical backlog numbering (not the current priority order):

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

**Historical proposal, not the current implementation contract (2026-09-19).**
The initial implementation now uses `StructuralDimensionPlanBuilder` over
`DimensionChainSet`, not the generic candidate path proposed below. It validates
a supplied plan; the intent/coverage planner remains unbuilt. Its own public
preview/apply contract exists, so the old "must not introduce a second plan
shape" instruction is an integration concern to resolve by adapter/migration,
not a claim that unification already happened.

Other superseded assumptions below: offsets are now corrected/read back by the
verified writer, not routinely repaired by a separate `move_dimension`; datum
can be recovered only for a unique connected segment path, not from normalized
point order; sections/end views use the current skill's visual gate; contact or
bbox candidates are not alternative create coordinates; the old 15-60 mm
grouping discussion is not an active tolerance. Limited Create/apply is already
implemented, but still awaits live validation. Use **Active direction** for
new work and the README for exact supported arguments. The remaining rationale
is retained as design history, not instructions to bypass the current skill.

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

### 2c. Defect detection for batch processing — retired

The dimension-defect detector and its public command were removed. Placement now
starts from calculated structural-chain positions and makes every keep/remove
decision before writing. Its empirical history remains in
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

The first step sideways is to collect reliable geometric facts in the drawing view
coordinate system, before deciding which facts deserve a dimension.

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

#### 3. Four preliminary chains from the structural box — implemented 2026-08-13

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
   rings where the part has them. An edge contributes one X position only when its endpoint
   X coordinates coincide within the planar 0.001 mm tolerance; similarly for one Y
   position. Otherwise a polygon edge contributes no coordinate by itself: both coordinates
   vary along it. Its two endpoints are nevertheless real corners and may contribute there,
   where the tilted edge meets another edge. The 10-degree test used to audit an already
   placed point is intentionally not used here: it answers proximity, not whether an edge
   has one scalar coordinate.

   A `Segment` is different from a polygon edge: it is already a visible line with two real
   terminal places, for example a flattened contact. Both endpoints remain candidates even
   when the segment is tilted, and are recorded as `SegmentEndpoint`, not as invented
   polygon corners.
4. The side is chosen by the actual source point, not by copying a coordinate. For a
   vertical or horizontal edge, its endpoints are the source points. A point low in the view
   feeds the bottom chain and one high in it feeds the top; left and right likewise. A stud
   therefore contributes its X to both top and bottom through two different endpoints, which
   is not the same as copying one coordinate across. A source exactly equidistant from two
   sides is deliberately offered to both preliminary chains; the set is over-complete and a
   later policy chooses whether either side keeps it.

   Confirmed on the measured view: every point of the bottom chain lies at the bottom of
   its part and every point of the top chain at the top, and the left and right chains
   divide the same way by X. This confirms where a source point belongs; it does not claim
   that the human drawing used every position of the preliminary symmetric set.

   Coalesce equal coordinates before forming a chain. The common 0.001 mm planar
   coincidence tolerance (the one that flattened the source geometry) governs both which
   points support an extent and which coordinates become one position; those are one
   question, not two tolerances. Several parts sharing one face, or a raked corner agreeing
   with a square edge, make one position rather than several coincident dimension points.

Only the structural box supplies the four outer extremes; other assembly-boundary steps
and hole rings are retained as factual boundary geometry, but do not add preliminary
positions. Positions come from the member shapes that actually explain them. A part box
is not used: on a raked or cut part its corner can be empty space, and its extremum can
lie on a tilted edge that has no one coordinate to dimension to. This is why `ViewHull`
and OBBs are still forbidden as dimension evidence.

This is not a loss of useful steps. A step in the union boundary is either already an
edge of a member contour, which supplies the same position with its owner, or is created
or displaced by merging across `MergeGapTolerance`. Dimensioning the latter would invent
a location which no part actually has.

The result is four preliminary chains near the parts they describe. Later policy may
remove a second face that only restates a part size, add opening faces, or decide that a
side should hold only its overall. Those are policy decisions after this geometric
proposal, not reasons to duplicate every coordinate on both sides.

#### 4. Keep semantic geometry groups separate — implemented 2026-08-13

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

Calculation requires at least one real planar point in the boundary, or, when there is no
boundary, in the group shapes. It fails explicitly if that evidence is absent; four empty
chains would look like a valid answer. `Apply` also runs once per `GeometryGroup`: it refuses
to replace a calculated or policy-reviewed set, because that would silently discard kept or
removed decisions and their reasons. Recalculation means constructing a new geometry snapshot.
An `Empty` shape among otherwise usable group shapes is intentionally ignored here: it has no
place to propose. The adapter that formed the snapshot remains responsible for reporting why
that shape was empty and whether the group read was complete.

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
group from one part contour and its hole rings.

**The thing that must not branch is the calculator.** Positions come from contours the same
way whichever the subject is, and a `CalcDimensionChains` that asked what it was looking at
would have two behaviours to keep true instead of one. `GeometryGroup` shares one type today
because nothing has yet appeared that a part snapshot needs and an assembly snapshot cannot
hold - not because the two subjects are the same. The drawing type is known to the caller,
so a second snapshot type is cheap to introduce the day a part needs something of its own;
the calculator taking a switch is what would not be.

The difference begins afterwards. `AssemblyDimensionPolicy` locates parts and may
remove a span that merely repeats a made part's size; a future `PartDimensionPolicy` must
instead describe that size, holes, cut-outs and other fabrication features. Do not apply
assembly rules to a single-part drawing.

There is no policy interface yet because only the assembly policy has evidence. When a
single-part policy is supported by real cases, the two policies may share an explicit
contract over calculated chains. The calculation boundary stays concrete either way.

#### 4a. Four constraints for the structural adapter — implemented 2026-08-13

`StructuralGeometryGroupBuilder` connects the tested calculator to the structural outline,
and `draw_structural_chain_positions <viewId> [side]` is its read-only line overlay. These
constraints remain here because each is a decision that the code will not explain, and
three of them are the kind somebody later corrects in good faith.

**The group's completeness stays free of Tekla.** A group carries whether it is complete
and which sources went unread, named by the same caller-owned ids its shapes use. Not
`modelId`, not an `Identifier`, not a Tekla error string. The reason is the one that kept
`IsHole` out of `PlanarShape`: a group is the same object for an assembly and for a single
part, and the first adapter to put a model id in it will look entirely reasonable doing so.

The completeness travels inside the group rather than beside it. A partially read group
still produces plausible chains, and the damage is not confined to the gaps: if a
`Defining` part went unread, the overall extremes are wrong, which is the one dimension a
reader trusts without checking.

**Rings alternate by depth.** Unfolding a contour tree, even depth is a normal ring and
odd depth is a hole. This actively determines the `IsHole` evidence of part rings, which
the calculator reads for positions. Assembly-boundary rings retain the same topology, but
currently provide only the overall extent. Obvious only to someone who has already met an
island inside an opening; without the rule that island becomes a hole and its edges are
read inside out.

**The overall extremes span all components together, gap included.** An assembly can
project as several disconnected pieces, and the extent across them is the assembly's real
overall size. Written down so that the emptiness between them is not later mistaken for a
bug and "fixed".

**Debug drawing is an adapter, not part of the calculation.** The calculator knows nothing
of views, sheets or overlays. A position is a coordinate, so a debug view of one draws a
line across the group's extent - not a cross, which would put back the point framing this
whole section exists to replace.

#### 3a. Checked on a live view (2026-08-13)

The raked timber wall, front view, all four sides drawn as lines and looked at.

| side | positions proposed | used by the drawing | not found |
|---|---|---|---|
| bottom | 10 | 5 | 0 |
| top | 14 | 5 | 0 |
| left | 19 | 5 | 0 |
| right | 21 | 4 | 0 |

Every position the seven existing chains actually dimension is in the preliminary set, and
every line drawn lands on a part face - none in empty space. The set is two to five times
larger than what the drawing uses, which is what "deliberately over-complete" was meant to
produce.

Three things the run confirmed that no unit test could:

- **the role layer earns its place.** The X extent starts at 210, not the 200 that
  `get_assembly_outline` reports. Two hundred is the edge of an outer layer; the structural
  outline excludes it. That is the ten-millimetre overhang recorded in the assembly-geometry
  roadmap, now visible as the difference between two commands;
- **the same coordinate does arrive from both ends of a member.** All ten bottom positions
  appear among the fourteen at the top, each from a different endpoint of the same stud -
  which is what the side rule predicted and what the drawing itself does;
- **the raked members crowd one side.** Ten of the twenty-one right-hand positions fall
  between 1248 and 1393, one per stud, because the rake cuts each at its own height. They
  are real corners, and thinning them is a policy question rather than a fault.

The doubled positions at the extremes appeared exactly as predicted - see 4b. On the sheet
they read as one line.

Reading the drawing was blocked twice during this session by Tekla returning the model
coordinate system for a view, after a drawing was closed abnormally. Geometry then arrives
in model space with `success: true`, and only comparing spans revealed it: a front view that
should read 1739.9 by 3665 came back 200 by 1749.9, the height dropped and the thickness
kept. Restarting Tekla cleared it. Nothing in the code caused it and nothing in the code
detects it yet.

#### 4b. Merging near-equal positions belongs to policy, not to the calculation

Measured on a live panel view: the assembly outline sits **0.00235 mm inside** the part
contours it was built from, at both X extremes and nowhere in Y. The union is not a plain
combination - `ProjectedOutlineBuilder.BuildAssembly` inflates every contour by half the
merge gap and deflates it back, so that parts standing a fraction apart read as one
boundary. That round trip returns exactly on some corners and not on others, and what it
leaves behind is a few thousandths of a millimetre.

The visible effect is a pair of positions at each end of every chain: one on a real part
edge, one on the outline, 0.00235 apart and so beyond the 0.001 mm coincidence tolerance.
The `GroupExtent` support - the overall dimension's own anchor - lands on the outline one,
a coordinate no part actually has.

Write that down rather than fix it here, because two obvious repairs are both wrong:

- raising the calculation tolerance would hide it everywhere. 0.001 mm is the tolerance
  that flattened the geometry, and a millimetre there would erase rebates and sheet
  thicknesses;
- changing the outline builder would touch geometry shared with contacts, to correct
  something no drawing can show. At 1:25 the residue is a ten-thousandth of a millimetre
  on paper.

**The merge belongs to the stage that removes redundant dimensions.** Two positions closer
together than a dimension can express are one position on the drawing - and in construction
that threshold is around a millimetre, since nothing real is thinner. But collapsing them
is a decision about what the drawing should say, not a fact about the geometry, and the
disposition already exists to record it: the removed position is marked with its reason
instead of vanishing.

So the calculated set keeps both, deliberately. A reader of the raw output should expect a
doubled position at each extreme and should not treat it as a defect of the calculation.

Modelling is the other source of small differences and is not the same thing. On the panel
measured here the whole assembly sits at -0.02235 rather than 0, which is where the parts
genuinely are and what a dimension should report. Faces that ought to coincide did coincide
to better than 0.001. Where a model is looser than that, one intended face will produce two
positions - and that is worth showing rather than hiding, because it is a fact about the
model.

#### 4c. Contacts belong in the snapshot, for the policy to read — agreed, not implemented

Observed once, on 2026-08-13: front view 3759 of a raked timber wall, comparing
`draw_structural_chain_positions 3759 <side>` against `get_contact_candidate_points 3759`.
Of 41 part pairs sharing a chain position, 34 really touched and **7 did not** - their faces
merely landed on the same coordinate. The case files were not kept, so treat this as one
observation that motivated a decision, not as a check anyone can rerun from here; repeating
it needs those two commands on that view.

A later read returned the same aggregates - 18 parts, 56 contacts, extent
`200…2090 × -1771…1729`, and `Segment 39 / Polygon 16 / Point 1` - but a different set of
model ids. The assembly/drawing identity was not captured for both reads, so this is not
evidence of two independent panels: it may be a similar panel, rebuilt/renumbered source,
or a selection/read defect. Record that discrepancy rather than promote the matching totals
to confirmation.

What it motivates: the supports already recorded on a position cannot answer whether two
parts are joined. About one time in six they would say yes when the answer is no.

That answer is a fact about the geometry, not a decision, so it travels in the snapshot.
But only a policy has any use for it, and a policy must stay a pure function over the
snapshot: if it has to read Tekla for contacts itself, it stops being testable and we are
back where we started.

**Touching is not the useful fact on its own.** Two studs standing side by side touch along
a vertical plane; for a horizontal chain their shared face is an internal seam and no
dimension goes there. A stud standing on a bottom plate also touches - and that is two
different members, where the plate's top face is a real place. The same contact is a seam
for one chain and a bearing surface for another. So the snapshot carries contacts **with
their flattened shape**, and a policy asks its own question about an eligible shape: does it
lie across my chain direction or along it.

##### What must be decided before implementation

**A neutral contact type.** `ViewContactGeometryResult` has the shapes but is Tekla-shaped:
`ViewId`, `UnreadPart`, `Unresolved` full of model ids. Embedding it would break the
snapshot's independence from Tekla. That independence is a separate argument from the one
above about subjects: it is what keeps a policy a pure function, testable without a running
model. Model ids and `Identifier`s in the snapshot would take that away whether there is one
snapshot type or two. What goes in is a neutral `GeometryGroupContacts`: the
flattened shapes, two caller-owned participant ids per contact, the contact kind, and
neutral issues.

Each flattened shape keeps both a caller-owned `ContactId` and `ShapeId`. The participant
pair and kind do not identify one place: one pair can have several contacts, and one contact
can project as several separate shapes. Losing either id would make policy reasons and debug
output ambiguous, and would invite a later deduplication to merge distinct places.

**A way to tell which contact touches which position.** A position knows its supporting
`GeometryGroupShape`; a contact knows two parts. Today the only bridges between them are
parsing `defining-part:123:ring:0` back into an id, or re-matching by distance - a string
contract and a proximity guess, and both are the kind of thing this whole area exists to
avoid. Fix it at the source: a group shape carries a list of neutral `SourceIds`, a contact
carries the same two ids, and the policy joins on those. Only then does it check the
geometry - whether the position's supporting point lies on the contact shape within a
tolerance, and whether that shape runs across the chain.

**A rule for every planar form.** Only a `Segment` has one direction, so only a segment can
answer the first policy's across/along question directly. A `Point` has none, and a
`Polygon` can have edges in both directions; the observation that 39 of 56 contacts project
as segments leaves 17 that this rule does not cover. Until a separately measured rule exists
for them, they must not be silently treated as seams or bearing surfaces: the affected
position is `Undecided` with that reason.

**A disposition for "looked at, could not decide".** `Calculated` cannot carry it: it means
"nobody has judged this", it takes no reason, and `MarkPolicyApplied` refuses while any
position still holds it. A policy that leaves a position undecided must be able to finish
its review and say why, so the enum needs a third outcome - `Undecided` - with a mandatory
reason, exactly as `Kept` and `Removed` have.

##### Completeness binds the policy

"These two do not touch" is only true if the whole view was searched **and** every region
survived flattening. `ViewContactGeometryResult.IsComplete` already means all three -
nothing unread, nothing `Unflattened`, nothing `Unresolved` - and all three must reach the
snapshot, not only the unread parts. A region that flattened to nothing is a contact whose
place on the sheet is unknown, and treating that as "no contact" is the same error as
treating an unread part that way.

`IsComplete` is a useful whole-view warning, not the scope of a decision. Each neutral
contact issue must retain the affected participant `SourceIds` and, where one was known,
its `ContactId` and `ShapeId`; this is structured data, not a compound string for policy to
parse. A missing or unflattened A/B contact blocks only a no-seam conclusion about an
affected A/B position. It must not make an unrelated C/D position undecidable. Where an
issue does affect a position, the policy marks it `Undecided` and names that exact gap.

So the policy takes one thing - the snapshot. Boundaries and part shapes, the preliminary
chains with their positions and supports, the contacts with their shapes and participants,
and the completeness of both reads. It reads nothing itself; it only marks positions.

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
