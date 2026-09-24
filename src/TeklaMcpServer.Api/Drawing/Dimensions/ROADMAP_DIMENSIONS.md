# Dimensions Roadmap

Updated 2026-09-24. This file is the active work order. The context and query
changes below are agreed design, not implemented functionality.
[The archive](ROADMAP_DIMENSIONS_ARCHIVE_2026-09-24.md) preserves the previous
roadmap, measurements, rejected proposals and longer-term ideas. Its work orders
do not compete with this one.

## Goal and current state

Reduce elapsed time and token usage per completed dimensioning task without
losing required measurements, source evidence or existing write checks.

- `GeometryGroup`, `CalcDimensionChains` and `DimensionChainSet` already provide
  the structural contours and preliminary candidates. Reuse these components.
- `get_structural_chain_positions` currently builds its own structural outline
  and chains for each call; it does not retain that result for creation.
- `create_dimension` calculates an omitted `distance` using another structural
  outline read. Its current outline call has no exclusion filters. Chain reads
  can have filters, so the two commands can currently use different extents.
- `DimensionPlacementCalculator` and `DimensionPlacementSettings` already exist.
  The default gap is 8 paper mm; `paperGapMm` overrides it. An explicit `distance`
  remains a manual view-unit input. Preset distances are not outline gaps.
- Create/recreate use `DimensionWriteProtocol`: stored-value read-back, offset
  correction, and compensation before the original deletion is attempted.
  Preserve those checks and failure handling. This is not independent proof of
  rendered-line placement; live validation remains incomplete.
- The experimental structural preview/apply commands were removed on 2026-09-24.
  The creation path for this increment remains `create_dimension`.

## One source of geometry for the selected view

Introduce `ViewDimensionContext` in `TeklaMcpServer.Api`. It contains a captured
view identity, coordinate system, type, scale and depth window; normalized
exclusion filters; structural part properties and roles; structural outline;
`GeometryGroup`; and calculated preliminary chains. An assembly-wide context
is not needed for this increment: candidate coordinates depend on the view.

Build it from the structural-outline path and view metadata. Do not call
`DrawingViewContextBuilder` merely to obtain a scale or parts list: it also
reads part geometry, bolts for every part and grids. Those remain optional
future capabilities and are not part of this first context build.

Keep completeness and provenance with the geometry: read errors, excluded and
outside-depth parts, unresolved selection, and unresolved main-part identity.
Automatic offset calculation must retain the existing complete/nonempty-outline
guard. Complete reading does not prove a section outline is a clipped solid:
full-solid projection on section/end views remains an accepted known limitation.
Caching must not promote that geometry to a verified cut contour.

Treat the captured geometry and calculated candidates as read-only. Existing
`MarkKept`/`MarkRemoved` methods must not mutate a shared cached chain. Keep
choices for an individual dimension separately; use a read-only boundary or
separate working copies where the existing mutable types require it. Query
sorting, filtering and rounding must not change the stored source.

### Filters and identity

Use drawing identity, view identity and normalized exclusion filters to resolve
the context within the active view run. The captured coordinate system, scale
and depth window belong to that snapshot generation; refresh replaces them.
Do not reread depth or recalculate `sourceFingerprint` on every question to
prove freshness. No new caller-supplied fingerprint is required.

Add the same optional `excludePrefixes`/`excludeMaterials` to `create_dimension`
as to chain reads, with the same empty defaults and matching semantics. Both
commands resolve the exact filter scope requested. Never choose whichever
snapshot was most recently queried for a view. Different filter sets have
different contexts; equivalent normalized filters share one.

### Lifetime and refresh

During dimension placement on one view, its source geometry is treated as
unchanged. Reuse the snapshot for questions and dimensions; creating a dimension
does not invalidate source geometry. Retain filter-specific contexts for the
active drawing/view. Clear them on explicit refresh or a switch of drawing/view;
bridge restart also loses them. A missing context is built on first use.

External geometry or view edits during the run may remain unnoticed until
refresh. No reliable automatic change detector is assumed. The explicit refresh
path must force a source reread, including bypassing or invalidating any lower
geometry caches used by the builder, rather than reconstructing from stale data.

The host of the persistent bridge owns the provider/store lifetime and injects
it into command handling. Confirmed in `TeklaBridge/Program.cs`: `ExecuteCommand`
creates a new `CommandDispatcher` on every request; a field of that dispatcher
or handler alone cannot retain the context. Context construction, lookup policy
and placement orchestration belong in API services; bridge handlers parse,
delegate and serialize. API code must not depend on bridge implementation types.
One-shot mode builds a new context per process and cannot provide cross-call reuse.

## Work order

### 1. Share the snapshot between chain reads and creation

Implement the context builder/provider and persistent lifetime, then route
`get_structural_chain_positions` and automatic `create_dimension` through them
in the same increment. Move the handler's outline/offset orchestration to an API
service that calls the existing calculator and writer. The explicit-distance
path should continue to work without constructing geometry for an unused offset.
Calculate chains once when needed from the captured geometry; an offset-only
request need not calculate unused chains.

Acceptance:

- Reading candidates and then creating multiple dimensions in the same scope
  builds the structural outline once. Creation-first followed by candidate
  reading also reuses geometry; queries do not rebuild contours independently.
- A dimension write does not invalidate source geometry. Explicit refresh,
  missing context and drawing/view switches follow the documented policy.
- Different filters never cross-contaminate; empty filters retain their defined
  meaning. Incomplete geometry remains incomplete and blocks automatic offset.
- A query or point choice does not mutate another query's source candidates.
- Existing stored-value verification, correction and cleanup behavior remains.
  Cheap identity checks and post-write Tekla reads are still required: the
  optimization eliminates repeated source-geometry reads, not all Tekla reads.
- Count outline builds and, where instrumented, actual solid reads separately.
  One outline build can contain several lower-level reads; do not claim every
  solid is fetched once without measuring that path.

### 2. Return short answers from the same context

Support side points, side edge, structural parts/main-part identity and scale;
allow any or all four sides together so small answers need not multiply calls.
Offset questions use the same calculator as creation and the supplied points;
the base depends on those points, not one fixed base cached for the whole view.

Deduplicate the displayed XY point while retaining every associated owner,
support kind, hole flag and `partExtentAlongChain`. Distinct transverse points
at one chain coordinate remain distinct. Keep source precision internally;
three-decimal display does not redefine geometric equality. Preserve ownerless
`GroupExtent` anchors. Validate evidence preservation, not just point counts.

The new short projection can omit `positionIndex`, `supportIndex`, long source
IDs, nulls and false flags. Preserve full source evidence through the existing
verbose read. Update the skill and removed-plan guidance with the new response
contract when it ships, not ahead of implementation. Selection still belongs to
the operator/skill; compact projection does not silently choose required points.

Measure complete responses and full-task elapsed time on the same long view,
section and column cases before/after, including retries. Record call counts,
geometry reads and available token usage; character counts are only a proxy.
The historical 1,806-character prototype omitted extents and is not a delivered
savings result. This step does not require a separate command for every field.

### 3. Correct line reconstruction and add independent observation

`DimensionProjectionHelper.TryCreateCommonReferenceLine` currently uses the
maximum projection toward the side plus `distance`. Reconcile reconstruction
with the supported base-point semantics; keep calculated and independently
observed line positions distinguishable. Do not generalize an axis-aligned
formula to diagonals, ambiguous ties or shortened views without evidence.

Retain stored `Distance` correction and the existing point/side/row/datum checks.
Add rendered-line observation through `GetObjectPresentation` of individual
dimension segments; the archived probe found no presentation for their parent
sets. Report `matched`, `mismatch` or `not verified` with a reason, independently
of stored-value verification. Where rendered matching becomes mandatory for
replacement, integrate it before original deletion in the existing protocol;
preserve the post-deletion read-back and compensation boundaries as well.
Do not substitute a post-return check that discovers failure after losing the
original. Define unsupported/not-verified behavior explicitly before enforcement.

Validate supported sides, scales, input order and tied-base behavior on captured
cases and an authorized test drawing. View breaks require a verified coordinate
mapping or `not verified`. The archived TS2025 probe supports selected 1:5/1:10
cases; it is not universal validation. Preserve measured numbers as observations.
Independent line verification does not delay the behavior-preserving read reuse
in steps 1-2, and caching does not claim to fix placement accuracy.

### 4. Improve point selection only after measuring the simpler path

Use the existing candidates to test three measurement questions: locate a
symmetric end plate, locate an offset plate, and locate an intermediate stiffener.
A plate's width/height alone does not establish its position against the member.
Record correct alternatives and wrong cases before encoding reusable rules.
Keep measurement completeness separate from geometric completeness and write
success. Unsupported relationships remain unresolved with a reason.

Automatic templates, whole-view planning and batch writes remain later work.
They require measured benefit and their own coverage/retry validation. Bolts and
solid clipping retain their separate roadmaps. Do not reintroduce the removed
plan commands or a second creation path as a prerequisite for this increment.
