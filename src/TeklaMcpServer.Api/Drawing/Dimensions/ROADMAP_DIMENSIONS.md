# Dimensions Roadmap

Updated 2026-10-02. This file is the active work order. Steps 1-3 have source
implementations and automated tests; placement/write acceptance still has live
gates. Step 4's steel, section and timber-wall previews are implemented behind
explicit rule-set selection. Timber-wall live findings are recorded below; the latest
rules still need repeat validation on the named fixtures. Five rule transfers and the
legacy steel location cleanup in step 4b are complete. Contact candidate reuse is
implemented, with the remaining comparison and performance gates listed in step 5.

## Creation row fix (2026-10-02)

Historical deployed increment: step 7 below supersedes preview/batch row selection
in the current source, while keeping the single-create numeric row contract.

The reviewed numeric preview row now passes through single and batch creation.
Rows are positive integers (1, 2, 3, ...); the default outline gap equals row
number x default gap. Explicit `paperGapMm` or `distance` takes precedence over
the calculation and bypasses the unused row. Omitting row uses 1. No
text aliases are retained in this undeployed increment. The existing writer and
presentation checks are unchanged. Steel and timber location previews expose
row 1; nonempty overall previews expose row 2. Callers must forward the chosen
row; no rule or point selection is added to the writer. The bridge and MCP server were deployed and hash-verified on 2026-10-02.
The installed numeric row schema and Tekla connection were verified. Existing
clients need to reconnect to reload schemas. Live acceptance remains open,
including neighbour reflow and text collisions.

## Current delivery and next gate

### Conservative composition preview (2026-10-02)

The pure composer now supports explicit shared-datum union in diagnostic preview.
Synthetic pairs/triples exercise compatibility and the panel adapter; current panel
rules still have unresolved reference semantics and therefore remain separate.
Compatibility components with competing unions remain explicitly ambiguous rather
than selecting a subset by ID ordering. Shared per-request `DimensionCoordinateSettings`
now supplies bolt and composition tolerance (default 0.1 mm; previous bolt default
0.01 mm). Typed Query exposes the setting; MCP transport of custom values is pending.
This code stage adds no batch writing and has not been deployed or accepted live.
See [union policy](ROADMAP_CHAIN_COMPOSITION.md#first-conservative-union-policy-2026-10-02)
for contract, exact-coordinate policy and restrictions. The earlier position anomaly
is deferred after the user's report of a possible model/drawing update; cause remains
unverified.

### Diagnostic part classification: runtime review (2026-10-02)

The internal classification stage (`1ec9c40`) and MCP/bridge transport (`c61128e`)
were deployed; bridge/API/contact-core hashes and the new-process MCP `layerRules`
schema were verified. Existing clients must reconnect to refresh their tool schema.
The read-only EW.1 - 6 Front review preserved preview/decisions and exposed configured
classes/conflicts. C24 battens absent from that Front snapshot are covered only by
captured Top attributes and pure classification replay; Top panel planning is unsupported.
One existing overall line changed its read position during the session without an
issued write command; cause is unestablished, so drawing-position invariance is not
claimed. See [classification review](ROADMAP_CHAIN_COMPOSITION.md#read-only-classification-review-2026-10-02)
for evidence, capture scope and remaining gates. No classification-driven point
selection or dimension placement was added.

### Bolt chains and composition: current work order (2026-10-02)

This update supplements the structural/panel work order below. It does not close
the independent batch, contact, layout or panel acceptance gates.

**Implemented:** frozen raw bolt groups in the view dimension context; internal
row/column and actual projected part-edge proposals; combined per-part chains
across model bolt groups, preserving group/index provenance; explicit selection
through the existing batch writer and final read-back. Structural and bolt
chains remain independent proposals. The runtime contract lives in
[Dimensions README](README.md#explicit-bolt-chain-creation-2026-10-02).

**Live evidence:** M.81 Section G (1908) and E (1409) were dimensioned with separate
structural and bolt chains. Coordinates and rendered offsets were read back.
This does not prove occlusion, section clipping, collision-free text or equivalence
to Tekla Integrated dimensioning. In E, the 126 mm segment uses the full-solid
projected contour; its correspondence to the section edge remains unverified.

**Latest source cleanup:** explicit-selection contract, side-set preview caching,
all blocked-source diagnostics, boundary tests and shorter tool descriptions are
implemented and tested. Deployment and runtime acceptance of that cleanup remain
open; live evidence above belongs to the previous deployed increment. This section
is the canonical status for bolt-chain delivery; other roadmaps link here.
Composition design/work order lives in [Chain Composition roadmap](ROADMAP_CHAIN_COMPOSITION.md).
Commit/push history belongs in Git, not roadmap status.

### Targeted refactoring before chain composition (2026-10-02)

Status: review findings and proposed work, not implemented. Preserve the public
MCP/bridge contracts and verified-write protocol. Prefer existing domain models;
do not replace the whole dimension module or introduce a new framework.

| Priority | Area | Proposed change and acceptance |
|---|---|---|
| High | `ViewDimensionContext`, bolt preview/resolution | Define only the minimal typed proposal contract needed by the first composition use case, reusing existing models and adapters. Preserve supported point order, provenance, eligibility, IDs, precision and refusal diagnostics; JSON remains an output boundary. Do not require migration of all preview families before composition starts. |
| Incremental | `ViewDimensionContextProvider.Batch.cs` | Extract phases only where the composition integration needs them. Avoid repeated source resolution and attribute validation; recalculate only placement for the actual row. Full batch restructuring is a separate task, not a prerequisite. Preserve complete preflight, retention, stop-on-failure and partial-success reporting. |
| High, separate correctness fix | Final batch reconciliation | Compare final observed points, side, offset and type with the prepared plan, including retained and merged cases. Keep rendered-line observation distinct from calculated reference lines. Add unchanged-ID reflow regressions and the false-merged regression specified below. |
| High, measured | Verified-write lookup | `FindDimensionSet` enumerates sets across the whole sheet and repeats on verification/correction/cleanup. Baseline below confirms a major remaining per-chain cost. Evaluate view-scoped or ID-based lookup with a fresh read after each commit; preserve ownership/deletion/cleanup checks and never trust stale drawing handles. |
| Hypothesis, measure before prioritizing | `DimensionGroupFactory`, `DimensionOperations` | Nested connected-group scans give a quadratic worst case, but elapsed-time impact is unmeasured. Ordinary `GetDimensions` disables reduction decisions; reduction is not an established hot path. Measure grouping and debug materialization separately before any optimization; preserve connected-component semantics and deterministic output. |
| Low | `DrawingCommandHandler.Dimensions.cs` | Split handlers by responsibility using existing partial-class conventions: queries, writes, arrangement/combine and debug. Keep argument parsing, responses and command names unchanged. File size alone is not evidence of runtime slowness. |

The single first composition delivery and minimum proposal/plan contract are
defined in [Chain Composition roadmap](ROADMAP_CHAIN_COMPOSITION.md#single-first-delivery-read-only-composition-plan).
A full typed-preview migration, complete batch phase extraction and broad
optimization are not prerequisites; extract more only for a demonstrated need.

Measured lookup optimization and final-readback correctness are independent High
tracks; mechanical bridge splitting stays Low. Neither requires rebuilding all
planners, and neither should defer the first composition plan indefinitely.

**Baseline measurements from the dimension-write/read review dated 2026-10-02:**

Attribution: these are the review author's measurements, reproduced from the
supplied review report, not measurements performed during this roadmap edit.

| Log event / measurement | Observed result | Drawing / scale / sample metadata |
|---|---|---|
| `verified_write`: `findMs` / `verifyMs` | Lookup 27–254 ms, approximately 90% of the verification stage in the measured writes. | Drawing, scale and number of writes not specified in the supplied report. |
| `snapshot_read_phases`: first read of five dimensions | 2.2 s, approximately 78% attributable to text bounds; 0.2 s after the read-path correction. | Five dimensions; drawing, scale and repeated-run count not specified. |
| `rendered_line`: rendered-line verification | 4.3 s before the correction, 5 ms after it. | Drawing, scale and sample count not specified. |

These are existing measurements, not estimates or new measurements by this roadmap
edit. Attach the review author's original logs and missing fixture/scale/sample
metadata before a new performance comparison; do not assign these timings to
M.81 or the panel fixtures without evidence. Lookup is a
confirmed remaining cost in those cases. The other two rows document improvements
already made, not open optimization targets. Use existing `PerfTrace`/stage timers
for before/after comparisons and broader small/large-sheet and batch cases.
`DimensionStableReadHelper` performs 2–3 reads; its isolated contribution and
grouping/reduction costs are still unmeasured. Preserve stable reads, verification
and compensation. Do not claim a general speedup from these few cases.

**False-merged regression:** inject `_readDimensions` snapshots where an overall
still exists alongside a covering location chain. Complete final read-back must
retain the overall ID and must not report `merged` merely because reduction could
hide it. Also simulate a filtered/incomplete response omitting that ID while an
independent raw lookup still finds it: absence/merge must not be claimed. A filtered
snapshot alone cannot distinguish hiding from actual deletion/merging; without
complete or independent absence evidence, return `uncertain`. Cover a genuine
confirmed merge separately. Add a pure read-projection test that ordinary reads
preserve all set IDs with reduction disabled; fake-provider tests alone do not
prove the live Tekla reader contract.

Acceptance: preserve structural and bolt preview contracts with regression tests,
exercise batch retention/merge/partial failure and expired contexts, preserve
replacement-before-original-deletion and cleanup checks, then perform authorized
live acceptance on assembly drawing M.81 (`Stütze`), Section G (view 1908, scale
1:5) and Section E (view 1409, scale 1:5), plus larger cases for lookup costs. Geometric
visibility/section-clipping and text collision gaps are separate capabilities,
not problems that mechanical refactoring alone resolves. Keep behavior-preserving
extraction, correctness fixes and performance changes in separate reviewable steps.

### Chain architecture and composition

The canonical proposal/plan contract, single first read-only composition delivery,
later fixture capture, batch addressing, section migration and skew/interior expansion
are in [Chain Composition roadmap](ROADMAP_CHAIN_COMPOSITION.md).
Keep this file focused on dimension delivery and independent acceptance gates.

**Parallel/deferred checks:** occlusion (`Drawing.Bolt.CheckVisibility(int index)`
is still an unproven candidate), depth-clipped section contours, refusal rates
for vertex-aligned edge rays on complex/rounded profiles, and broader live
acceptance. Record refusals with source IDs before relaxing the edge algorithm.
Bolt-group position/datum, centered/skewed rules and extreme-bolt checks remain
separate unimplemented policy roles. Geometry-specific work is tracked in
[Bolt Geometry roadmap](../Geometry/Bolts/ROADMAP_BOLT_GEOMETRY.md).

- **Next gate: finish live acceptance of batch writes (step 6).** The code is
  implemented and deployed. A live run on view 7429 exposed a mismatch between the
  batch status and subsequent drawing reads; see step 6. Fix that reconciliation and
  verify partial success, retry, and overall/location merging. Keep step 5's contact
  comparison and performance gates open as a separate track.
- **Open panel acceptance:** rerun IW1.1 - 1, RE.1 - 1 and IW1.3 against the latest code, including
  the full-position-set duplicate rule and the recent outline/extreme selection
  changes. The live run on view 7429 checked a partial Top/Bottom match only; it did
  not validate the exact-match suppression case or replace the named fixture checks.

- Persistent `ViewDimensionContextProvider` shares frozen, filter-specific
  geometry between chain reads, short context queries and automatic creation.
  Chains are calculated lazily; explicit-distance creation needs no outline.
- The source increment in step 7 restricts `get_view_dimension_context` to
  prepared-chain questions with default `chain,scale`. Internal queries still
  support points/edges/placement/contacts. Queries return detached JSON;
  candidate choices cannot mutate the snapshot.
- The context retains detached per-part solid DTOs (bbox, vertices, faces,
  loops and view hull) from the same successful reads used to build outlines.
  `GetPartSolidGeometry(modelId)` returns a copy so consumers cannot mutate the
  snapshot. Tekla `Solid` handles themselves are not retained.
- Contact geometry and its candidate result use a separate view-scoped lazy
  cache, independent of structural exclusion filters. On `contacts`, captured
  solids are reused and missing depth-selected solids are read; the result is
  reused until refresh or drawing/view switch. This includes parts excluded
  from dimension extents.
- Create accepts the same exclusions as reads. Explicit refresh clears lower
  geometry caches too; drawing/query-view changes end the active snapshot run.
- Axis-aligned reference-line reconstruction uses a unique leftmost/lowest
  base. Stored Distance correction is retained. Segment presentation checking
  reports matched/mismatch/not verified separately; mismatch fails the existing
  write verification before deleting an original. Missing presentation,
  shortened views and ambiguous bases do not count as a match and do not alone
  reject the stored-value-verified write.
- Open placement/write acceptance: deploy on an authorized test drawing, validate
  four sides and 1:5/1:10,
  ambiguous bases and shortened views, then measure full-task time and response
  size on the agreed cases. No live speedup or placement guarantee is claimed.
  Trace events count context builds/hits, not individual solid reads.
- Contact live comparison on M.505 matched the old command: 52 points, 9 parts,
  matching participant pairs, anchor keys and contact states; no unread,
  unflattened or unresolved items. First-query timings and a full-exclusion case
  are recorded in step 5. Partial exclusions, full contact-shape output,
  degrees-of-freedom equivalence and larger-view measurements remain open.
[The archive](ROADMAP_DIMENSIONS_ARCHIVE_2026-09-24.md) preserves the previous
roadmap, measurements, rejected proposals and longer-term ideas. Its work orders
do not compete with this one.

## Goal and current state

Reduce elapsed time and token usage per completed dimensioning task without
losing required measurements, source evidence or existing write checks.

- `GeometryGroup`, `CalcDimensionChains` and `DimensionChainSet` already provide
  the structural contours and preliminary candidates. Reuse these components.
- `get_structural_chain_positions` and automatic `create_dimension` resolve
  the same context for the requested view and normalized exclusions. Repeated
  reads/writes reuse the outline; different exclusions never use the last
  context indiscriminately.
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

Implemented `ViewDimensionContext` in `TeklaMcpServer.Api`. It contains a captured
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

The previous `DimensionProjectionHelper.TryCreateCommonReferenceLine` used the
maximum projection toward the side plus `distance`. It now uses the supported
unique leftmost/lowest base. Keep calculated and independently observed line
positions distinguishable. Do not generalize an axis-aligned
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

Automatic templates and whole-view planning remain later work. Batch writes are
tracked as step 6 below and require their own retry and merge validation. Bolts
and solid clipping retain their separate roadmaps. Do not reintroduce the removed
plan commands or a second point-selection mechanism.

### 4a. Dimension points as objects, so the assistant does not read coordinates

First slice implemented: the view context now exposes IDs for points already
present in the four preliminary chains, and `create_dimension` accepts those IDs
instead of coordinates. This avoids sending coordinates when the caller chooses
`questions=dimensionPoints`; the existing coordinate query and write contract
remain available. Contacts, bolts, new geometry sources, automatic point choice,
and replacing preliminary chains with the ID model are not implemented here.

Model, stored in `ViewDimensionContext`:

- `DimensionPoints`: all dimension points of one view and four
  `DimensionPointLine` (Top, Bottom, Left, Right).
- `DimensionPointLine`: the order of point ids along one side. A point that lies
  on two sides (a corner on Top and Left) is one `DimensionPoint` with one id,
  referenced from both lines; the line keeps only the side and the order.
- `DimensionPoint`: an identifier, coordinates, a list of parents (part model id
  plus the source vertex or segment; a merged point or a contact has several), a
  set of kinds (flags), and the distances to the previous and next point on its
  line.
- Identifiers live only in one snapshot. Each snapshot build issues a new
  `contextId`, returned together with the points. `create_dimension` takes the
  `contextId` and the ordered `pointIds`, checks that the snapshot is still
  active and substitutes the coordinates itself; the order of the ids is the order
  written. A `contextId` belongs to one snapshot, and the exclusion filters are
  part of that snapshot, so `create_dimension` takes no filters together with it.
  A request with other filters builds another snapshot with another `contextId`;
  the earlier id stays valid while its snapshot is cached. All ids are refused
  after `refresh` or a drawing or view change, which is when the provider clears
  its cache. The source fingerprint is not used for this: it can stay the same
  after a refresh.
- `DimensionPointKind` (flags, listed in the answer so names are not guessed).
  First stage: only the kinds the existing chains already give (group extent,
  axis-aligned edge, tilted edge corner, segment end, point shape). Supports the
  chains already mark with `isHole=true` are kept, not dropped, and get a general
  `Hole` kind; it names the source of the support, not a hole centre or edge.
  Later stages: contact (touching / gap / overlap), precise hole kinds (centre,
  edge), bolt, grid axis; the context does not build these points yet. The existing
  `DimensionChainPositionSupportKind` is the starting point.
- `DimensionChain` keeps its meaning for a chain that a query selects from these
  points (a list of point ids) and that `create_dimension` accepts instead of
  coordinates. The old preliminary chains stop being a separate concept; their
  data becomes part of `DimensionPoints`.

Not a graph yet: ordered lines with neighbour distances cover dimensioning.
Real links (contour adjacency, contact membership) are added only if a query
needs them.

The implemented question is `dimensionPoints`: points of requested sides with
IDs, kinds, parent evidence and neighbour distances, without coordinates. The
existing `points` question still returns coordinates. Future questions may cover
main-part points, contact points of a named pair, outer extremes, and a ready
chain with its computed offset; those are not part of this slice.

First-slice behavior: points are built from the current chains' existing
supports (2D position, part id, support kind, extent). Exact XY matches share
one context-local ID across sides; no new tolerance-based merging is applied.
`get_view_dimension_context(questions=dimensionPoints)` returns the `contextId`
and ordered point records; `create_dimension` accepts `contextId` plus ordered
`pointIds`, resolves them in the cached snapshot, checks side membership, and
preserves the requested order. Coordinates and IDs are mutually exclusive.
An incomplete snapshot cannot be used for ID-based creation, even with an
explicit `distance`; manual coordinate input remains available.
Filters are part of the snapshot and cannot accompany the ID form. A different
filter scope has its own ID while cached; refresh or a drawing/view switch
invalidates these IDs. Contacts, bolts, new geometry kinds and automatic point
choice remain later stages.

Merging by tolerance (the Tekla rule dialog defaults its alignment tolerance to
50 mm; see the source below) is not enabled automatically: it can glue different supports together. The tolerance is
discussed and checked separately.

Tekla's own dimensioning rules group points by object category (documented at
https://support.tekla.com/doc/tekla-structures/2025/dra_dimensioning_rule_properties).
The project owner's observation is that this works poorly on complex
assemblies; that is an observation, not a documented limit, so the rules are not
copied for point choice. Only the mechanical parts are taken, as documented
there: line order (the first rule is placed closest to the part), the
alignment tolerance (default 50 mm), the minimum dimension length, grouping by
side.

#### 4a, second stage: chain preview (implemented, read-only)

`get_view_dimension_context(questions="chain")` returns, per side, a preview of the
standard location chain and the overall chain as point ids, segment lengths, the
role of each point and the parts that were skipped. It writes nothing; a chain is
still created by a separate `create_dimension` call with `contextId` and `pointIds`.
`all` does not include it.

Rules of this first version (built from the M.505 hand-placed chains):

- The main axis is the longer side of the main part's points. A side that runs
  along it gets a chain of: the extremes of the side, the main part's two ends
  (preferred at their coordinate) and the first edge of every other part.
- A side across it (an end-plate chain) uses only the parts that stick out past the
  main part's end on that side, with the main part's own edges; with an end part
  flush with the main part it returns no chain and says why. No overall is
  produced across the main part: the location chain already spans it.
- One point per coordinate (within 0.01): the main part wins, otherwise the point
  farthest to the outside, otherwise a real part edge before a bare group extent.
- Segments shorter than 3 view units are dropped in the interior of the chain. The
  3 is in the units of the point coordinates, not on paper, so on paper it is
  3 divided by the view scale. Dropped points are listed in `droppedShortPointIds`
  and a part left without any point is added to `skippedPartIds`. A short first
  segment is intentionally allowed.
- The preview refuses instead of guessing: a section or end view (they need the
  section/end-view check), a main part that is not exactly one, or an unresolved
  main part, or an incomplete context. The answer carries the reason in `note`.

Checked live on M.505 (back view): bottom 20 / 203.999 / 4500.002 / 348.094 / 142.5 / 10,
left 94 / 152 / 94, right 8 / 136 / 8 and the bottom overall gave the same point
ids as the chains placed by hand. Not checked: M.48 (long girder with raked ribs) and
M.81 (columns); the rule "one point per welded part" uses every part on the side, not
a real welding relation, so contacts may be needed to tell attached parts from
neighbours; mirrored sides (Top repeats Bottom) are not suppressed yet; a section
or end view is not covered.

Added after live runs on M.86 (raked girder, sections) and M.81 (column):

- **Witness lines stay off part outlines.** On one coordinate the point farthest
  toward the dimension line wins, from any part (not only the main part); ends of
  a location chain come from the half of the view on the line's side, so a raked
  end takes its near corner, not the far one that sends a line across the profile.
  `create_dimension` with `contextId` and `pointIds` applies the same rule to
  every id (`DimensionPointCatalog.Resolve`), so it holds even when a point was
  named by hand. Not covered: the overall chain still runs between the true
  extremes, so on a raked end its witness line can cross the profile; calls with
  plain coordinates are unchanged.
- **A part is located from the side of the main part it reaches.** The side lists
  are assigned by the middle of the whole assembly, not by the main part's axis, so a
  part on one side of a column showed up in both chains. A part reaching one side is
  left out of the other only when the other preliminary chain offers its points, and
  is then listed in `offeredOnOtherSidePartIds`. This field does not prove that the
  points survived selection in the final chain. A part on the axis can be retained
  on both sides; a part crossing the axis may be offered to only one side when the
  assembly's middle is shifted. The accepted criterion is that it remains located
  on at least one side.
- **Coordinates of a section are read in `DisplayCoordinateSystem`.** Dimensions are
  drawn in it; `ViewCoordinateSystem` has the same axes but on some sections a
  different origin (M.86 section D: 152.6 view units apart, E and F identical), so
  everything read in the view system landed that far from the drawn part. The
  `diagnostics` answer now carries both systems and the depth window under `view`.
  The depth filter is deliberately still read in the view system, where the window
  and the solids agree. Other geometry readers (all parts, part points, marks) still
  use the view system and need the same check.

#### 4a, third stage: chain preview for sections and end views (provisional)

Code status after this stage: `chain`/`chainDetails` now return **provisional**
`profile` and `location` chains on a complete `SectionView`/`EndView` when the
main part has one recognizable rectangular or I-shaped projected contour.
The points still come from the context catalog, so `create_dimension` can resolve
their IDs. Profile levels come from real straight contour edges, not a bounding
box. The location chain includes the main profile limits and secondary-part
outer faces; it reports `locatedByProfilePartIds`, `mergedNearbyPartIds`, and
`offeredOnOtherSidePartIds`. Nearby secondary positions merge below **0.5 mm
on paper** (the operator's chosen threshold), scaled to view units. The response
also has `unlocatedXModelIds` / `unlocatedYModelIds`; an empty list means this
candidate plan accounted for every part on that axis, either by a selected
point, an exact profile coordinate or an explicitly reported nearby merge.
It does **not** mean that the drawn section has been verified. The overall is
suppressed when the location chain spans it; otherwise it remains a manual
question.

This implementation has synthetic tests only. It has **not** been compared on
M.86 D/E/F or M.355, and cannot claim their acceptance yet. It still projects
full solids rather than clipping them to the section depth. Each response says
`sectionPreviewStatus=provisional` and requires visual verification before any
dimension write. Unrecognized/multiple main contours refuse with a reason.
The near-face rib exception is not inferred from a small coordinate gap alone:
that requires contact or other face evidence and remains open.

Before this stage the preview refused a section or end view, so the assistant picked the points
by hand from about 50 listed points and skipped a part (the facade angle on section
E), which made the drawing unmakeable; time and tokens went into that reading.
The hand-placed sections that were accepted are the acceptance cases: M.86 D (main
beam with a 430 end plate), E (beam with a small angle), F (beam, two ribs, a gusset,
two overlapping angles), M.355 section (HEB800, four ribs, two gussets).

Acceptance rules to finish and verify, in this order:

1. The main part is dimensioned by its profile: across it the flange width with the
   web thickness, along it the height with the flange thicknesses.
2. Every other part is located against the main part by at least one dimension on
   each axis, from its own side of the main part's axis (same side rule as above).
3. A part standing against a main-part face (gap under the readability threshold,
   the ribs of F) counts as located by the profile and is listed as such in the
   answer, not left out silently.
4. Duplicates on one place (two angles 0.14 apart) collapse into one dimension.
5. No separate overall when a chain already spans it.
6. The answer lists the parts of the view without a locating dimension; an empty
   list is the completeness check.

Open: the readability threshold in paper mm (the 3 view units above are not it),
overall on a raked end, and whether a rib against a face may ever need its own
dimension (default: no, listed instead).

Decided on live runs (M.86 sections D, E, C and M.78): the section answer is one chain per
axis; thicknesses inside the profile (web, flange faces) are left out unless a part
needs them; a part within 2 mm of a profile face abuts it and gets no dimension (the
1-2 mm gap of ribs is for the welds). Not decided, to revisit and then write into the
dimensioning skill: a part is located by the one face that sticks out past the profile,
while on M.78 the user also wanted the plate's second face (70, 10 mm short of the column
top); options are both faces always, or the second only when it is within 10-20 mm of a
profile face. The skill still says the assistant picks the points and asks for settings.

#### 4a, fourth stage: chain preview for timber panels (implemented; live validation pending)

The steel preview looks for one main part and refuses a panel ("no main part in this
view"). A wall may still have a main part (a long plate), so absence of a main part is not
the sign of a panel and the steel preview must not run on one. The mode is chosen
explicitly: `get_view_dimension_context(questions="chain", ruleSet="panel")`; without
`ruleSet` the behaviour is unchanged (steel). Which mode to use is read from the drawing
(name and mark of the drawing, properties of the assembly, e.g. "Timber Wall Zone 0",
"IW1.1 - 1") and written down in the dimensioning skill as a short mapping, not in code:
plant conventions live in the skill and settings. The mode is `panel`, not `wall`,
because panels differ (wall, roof, floor); the first kind implemented is the wall.

First hand run on IW1.1 - 1 (frame of studs T 60x100, a glulam GLB 100x180 on top, a bottom
plate; exclusions `R,S,M`; the two control diagonals were already on the view). It was
accepted ("не плохо"). The rules it used, to be turned into code:

- The reference body is every part left after the exclusions; no single member is the base.
- X chain (Bottom side only; Top repeats it and is dropped): the two extremes, a touching
  group of parts at an end (doubled post) as its outer face plus the far face of the group,
  one face (the left) of every regular stud, the inner face of the end group. Result on
  the panel: 120 / 427.5 / 625 / 625 / 625 / 535 / 120. Keep this accepted Bottom
  preference when Top repeats the complete X-position set.
- Y chains (Left and Right, each only if it differs from the other): the lowest face of the
  frame first (-1278.5 on this panel, the underside of the end posts; the bottom plate
  T-104 sits above it at -1233.5), the faces of the horizontal members (bottom plate, glulam), the top
  of the end parts on that side. The right end was taller, so the right chain carried
  one more segment (45 / 60 / 2227 / 180 / 220).
- One overall along X, second row below the chain. No overall along Y: the chain sums to it.
- Witness points use the outermost point of a coordinate (same rule as steel).
- Control diagonals are not part of the preview (`place_control_diagonals`).

Answer shape: like the section preview, a short list of chains (side, direction, point
ids, segments). Not decided: openings (both faces bound an opening; none were in the
panel used), which face of a stud family (left by default), raked tops on roof panels,
floor joists and battens, sheathing joints, and where the exclusion list `R,S,M`
comes from (skill for now, a setting later).

Plumbing: new parameter `ruleSet` through the MCP tool, the bridge command and
`ViewDimensionContext.Query`; a `TimberPanelChainPreview` next to the section preview; tests
on a synthetic wall with a doubled post, regular studs and a taller end.

Implementation status (2026-09-25): `ruleSet=panel` is wired through MCP, bridge
and context query; omission still selects `steel`, and unknown values are
rejected. The preview uses only points from the captured catalog, reports missing
supports rather than inventing IDs, computes the IW1.1 - 1 fixture chains, and
uses contacts lazily only when touching vertical-member candidates exist. A full
contact result must satisfy the exact rule 4b checks; incomplete contact data
falls back to the geometric touching rule. Automated tests cover the fixture,
missing support, contact gating and routing. The skill now selects `panel` only
for the measured timber-wall domain and records the fixture-specific lowest
frame-face datum. Live IW1.1 - 1 and a second wall have not been run.

Made precise after a review (these are the working definitions for the code):

1. **Choosing the mode.** The caller chooses; the code never guesses. The skill carries the
   mapping from the drawing's name, mark and assembly properties to `steel` or `panel`;
   an unknown drawing type makes the skill ask, not pick. Without `ruleSet` the answer is the
   steel preview, which refuses a panel with its usual note.
2. **Geometry, in numbers.** A member's box comes from its own points. A member is vertical
   when its Y extent is larger than its X extent, horizontal otherwise. Two vertical members
   touch when the gap between their X faces is at most 1 mm and their Y ranges overlap. A
   run of touching members is a group. An end group is one that reaches the panel's lowest or
   highest X; its positions are the outer extreme and the far face of the group. An interior
   group or a single interior stud contributes its left face. Two chains count as the same
   when they have the same number of positions and every coordinate agrees within 0.5 mm; the
   second is then dropped. Positions closer than 3 view units are collapsed as in steel.
   Stated for the fixture: the left end pair (0.04 to 60.04 and 60.04 to 120.04) gives the
   positions 0.04 and 120.04 and not 60.04; the right end pair gives 2957.54 and 3077.54
   (the pair starts at 2957.54 and ends at the outer face 3077.54, with 3017.54 left out).
   Only straight members are classified: an axis-aligned box from axis-aligned edges. A member
   with a tilted edge (a brace, a raked plate) is not called vertical or horizontal; it is
   listed as `tiltedPartIds` and left for manual choice, never silently treated as
   horizontal. Units: the 1 mm for touching, the 0.5 mm for comparing positions and the 3 view
   units below are coordinates of the view (model millimetres), not paper distances. The
   short-segment rule is the steel one: an interior segment under 3 view units is dropped,
   a short first or last segment is kept (the user confirmed that).
3. **Order and datum.** Every chain runs in ascending coordinate order: X from left to
   right, Y from bottom to top, so `points[0]` is the lowest coordinate. Distance is measured
   from that first point. A vertical chain starts at the lowest face of the frame, which is
   the underside of the wall (-1278.5 in the fixture, not the bottom plate face at -1233.5),
   so `points[0]` of every Y chain is the point at that coordinate.
4. **Completeness.** The answer lists every included part that has no position of its own
   with its reason: `inGroup` (a member of a doubled post, located by the group's faces),
   `onChainOfOtherSide`, `horizontalByFaces` (a plate or beam located by its Y faces),
   `droppedShort` (its position was dropped as too close), `tiltedPartIds` (not classified).
   Every regular stud has its own position, its left face, so there is no separate
   "in family" reason. A part in no chain and in no list is a defect; a test checks on
   every fixture that nothing disappears silently.
   **Contacts decide the doubled post** (user's note). Two studs standing against each other
   share a side face, and the view's contacts report that as a contact line (kind
   `FaceToFace`, state `Touching`); they are computed from the captured solids, lazily. A
   contact confirms a doubled post only when all of these hold: the contact selection is
   complete (`selectionComplete=true`); both participants are vertical members of the panel;
   the contact lies on a vertical face, that is its X coordinate is one shared X (not the
   contact of two end faces, which would lie at one Y) and its Y span is at least half of
   the shorter member; and that X coincides exactly with an existing position (the rule 4b of
   the timber rules: contacts choose among positions that exist and never add one). An
   incomplete or non-matching result proves nothing and the geometric test decides.
   The geometric test (gap up to 1 mm, Y ranges overlap) is the fallback and the first
   version. Contacts are requested only when the geometry finds a candidate: at least one
   pair of vertical members whose X faces are within 1 mm of each other. A wall with only
   isolated studs never asks for them, so it costs no contact work. Where contact and geometry
   disagree the answer says so. A gap of 1 to 2 mm has no contact; the threshold settles it.
5. **Order of work, with routing tests.** (a) the fixture and the failing test from the
   table below; (b) `ruleSet` through the MCP tool, the bridge and `Query`, with routing
   tests: no `ruleSet` gives the steel preview, `panel` the panel one, an unknown value is
   rejected with the allowed values named; (c) `TimberPanelChainPreview`; (d) the skill: the
   short mapping from drawing name, mark and assembly properties to `steel` or `panel`,
   and "unknown type: ask"; (e) live check on IW1.1 - 1 and a second wall.

Fixture from the accepted hand run (IW1.1 - 1, view coordinates in mm, exclusions `R,S,M`),
to be stored with the tests so the code is compared with the live example and not only with
synthetic walls:

| Part | Role | X range | Y range |
|---|---|---|---|
| T-666 (3759265) | end post, left | 0.04 to 60.04 | -1278.5 to 1053.5 |
| T-666 (3759385) | doubled post, left | 60.04 to 120.04 | -1278.5 to 1053.5 |
| T-106 (4085631, 3759355, 3759325, 3759295) | studs at 625 spacing, left faces 547.54, 1172.54, 1797.54, 2422.54 | 60 wide | -1173.5 to 1053.5 |
| T-666 (6987430) | end post, right | 2957.54 to 3017.54 | -1278.5 to 1053.5 |
| T-105 (3759235) | end post, right, taller | 3017.54 to 3077.54 | -1278.5 to 1453.5 |
| GLB-2 (3759205) | glulam on top | 0.04 to 3017.54 | 1053.5 to 1233.5 |
| T-104 (3759175) | bottom plate | 120.04 to 2957.54 | -1233.5 to -1173.5 |

Expected chains: Bottom X positions 0.04, 120.04, 547.54, 1172.54, 1797.54, 2422.54, 2957.54,
3077.54 (120 / 427.5 / 625 / 625 / 625 / 535 / 120); overall 3077.5; Y positions (rule 1 applied
after the hand run: one face per horizontal member, so the 60 mm of the bottom plate and the
180 mm of the glulam are not positions): Right -1278.5, -1233.5, 1233.5, 1453.5 (45 / 2467 / 220);
Left is contained in Right and is not printed (rule 7). The hand-placed chains of the first run
(Left 45 / 60 / 2227 / 180, Right 45 / 60 / 2227 / 180 / 220) are superseded.

Live findings on panels after the first implementation (walls IW1.1 - 1, IW1.3 - 1, IW1.5 - 1
and the frame RE.1 - 1, all timber, `ruleSet=panel`). What worked and what the preview still
gets wrong, all confirmed on the drawings:

- IW1.1 - 1 and IW1.5 - 1 came out complete and equal to the hand-placed chains (18 to 21 s and
  about 3 to 5k tokens per drawing).
- **Rule 1 of the timber rules is missing in the Y chains.** `BuildY` adds both faces of every
  horizontal member (`MinY` and `MaxY`), so a 60 mm member gives two positions 60 apart. The
  plant keeps one face, the span between two faces of one part being its own size: the lower
  face of an interior member, the outer face of the top and the bottom plate. On IW1.3 the
  Left chain went from 10 to 6 points and the Right from 12 to 7 (hand edit by the user), on
  RE.1 - 1 from 10 to 5 (0 / 615 / 1230 / 1845 / 2445). The data carries the evidence
  (`partExtentAlongChain: 60`).
- **Rule 7 covers only equal chains.** A chain whose positions are all contained in another
  (IW1.3: the Bottom chain inside the Top one) is not dropped, only an identical one is.
  For identical X-position sets keep Bottom and suppress Top, preserving the accepted IW1.1
  chain; keep both when the sets differ. For identical Y-position sets keep Left and suppress
  Right. The 2026-09-28 change that kept Top on an exact match broke the IW1.1 fixture tests.
  Source was corrected on 2026-09-29; the corrected branch still needs deployment and a live
  check on IW1.1 - 1 and IW1.3.
- **`BuildX` refuses when it finds no end group** and reports every part as unlocated. On
  RE.1 - 1 the leftmost members start at -41.97 with tilted corners, no end group matches the
  panel edge, the answer is empty, and 25 parts are listed as "no support". The support test
  is also too strict: the point catalog puts each point on the nearer of Top and Bottom, so a
  stud of an upper row has points only on Top, while the columns repeat at the same X in every
  row. The Bottom list of RE.1 - 1 does carry the column faces (164.955, 1360.555, 2523.158,
  3814.955). The user's chain there was -42, 165, 1360.6, 2523.2, 3815, 4054.8.
- **Fix decided:** when no end group is found, take the panel edges as the extreme positions and
  the left face of every column of studs, one column (one X across all rows) being one
  position; map each position to the side line by its coordinate, not by the owner part. The
  support check becomes "a point exists at this X on this side", so a stud row that lies on
  the other side no longer makes the part "unlocated".
- **Answer size:** the contact fallback pairs, the unlocated and missing lists are repeated in
  every chain of the answer (about half of the 2.7 KB on IW1.3). Print them once in the header.
- Contacts: the pair of the doubled post is confirmed by a real contact (a vertical segment
  at X 60.04, 2332 long) but the comparison with the position is exact and the contact reports
  60.03999 and 60.0401. The tolerance for that comparison is 0.001 mm (measured difference
  0.0001); until it is added every doubled post falls back to the geometric test.
- **Implementation status (2026-09-25):** `BuildY` now selects one Y face per horizontal
  member (outer face of the lowest/highest horizontal member, lower face for an intermediate
  member); full position-set equality suppresses the redundant side chain, while partial
  containment keeps both; `BuildX` falls
  back to panel extremes plus column left faces when an end group is missing, matching points
  by coordinate; the contact tolerance is 0.001; shared diagnostics are emitted once as
  `chainDiagnostics`. The RE.1 - 1 and IW1.3 live fixtures below have not yet been rerun
  against this implementation.
- **Live check (2026-09-28, Timber Wall Zone 0, FrontView 7429):** Top returned 9 positions;
  Bottom returned 7 plus a separate overall. The partial match correctly kept the Bottom
  location proposal. Dimension 17622 was read back as a 7-point `Relative` chain at distance
  200; overall 16998 remained a 2-point `RelativeAndAbsolute` set at distance 405. A batch
  request for an `Absolute` overall reported ID 17683 as failed/absent, while the following
  arrangement call saw that ID; a later drawing read no longer contained it. Final read-back
  showed IDs 16998 and 17622. This confirms the partial-chain behavior, but not exact-match
  suppression or rendered-line placement. Investigate the transient ID/status discrepancy in
  step 6 before treating the live write as accepted.
- Not covered and not started: top views and sections of panels ("FrontView/BackView only");
  the section of RE.1 - 1 is left undimensioned on the user's decision. Openings are still unseen
  in a real panel.
- Expected results for the tests: RE.1 - 1 view 4220 Bottom -42 / 165 / 1360.6 / 2523.2 / 3815 /
  4054.8, Left 0 / 615 / 1230 / 1845 / 2445 (615 / 615 / 615 / 600), Right the same Y as Left;
  IW1.3 view 4485 Left 6 points (-1388.5, -1343.5, -691.5, 906.5, 1063.5, 1343.5) and Right 7
  (1343.5, 1063.5, 906.5, 588.5, -691.5, -1343.5, -1388.5).

#### 4b. Extract an extensible rule architecture (in progress)

First slice implemented (2026-09-26): `IDimensionRule`, `DimensionRuleContext`,
`DimensionRuleResult` and `DimensionRuleSet` establish the initial boundaries.
`OverallDimensionRule` receives `OverallDimensionSettings`; the panel preview
adapts its results to the existing chain response. Geometry is reused and the
Tekla write path is unchanged. The initial extraction passed all four existing
panel preview tests before adding the requested vertical extension.

The panel defaults now propose horizontal overall on Bottom and vertical overall
on Right, both in the second row. The former preserves its existing side-specific
extreme selection; the latter uses the full panel MinY/MaxY. Missing catalog
supports produce an empty proposal with a reason, never invented point IDs or a
shorter height. Both directions can be disabled independently and their sides
selected through the internal settings (no new MCP settings parameter or file UI).
This user-requested extension supersedes the earlier horizontal-only / no-Y-overall
policy recorded in the historical panel examples below step 4a.

Validation: API and test projects build. Build warnings remain in existing
dependency/layout and test-analyzer code. The updated assembly has not been
deployed or checked on a live drawing.

Architecture boundary update (2026-09-26): the common `DimensionRuleResult` no
longer contains a required Top/Bottom/Left/Right side. It carries a normalized
view-plane direction and a separate placement intent. The current outside-outline
placement and four-side preview response are handled by the overall rule and an
axis-aligned compatibility adapter. The general context accepts detached rule
points without requiring a four-side catalog; the current catalog is one provider.
Each point carries extensible source evidence (`objectKind`, object id, geometry id
and optional feature metadata), so future bolt, bolt-group, rebar and rebar-group
providers do not change the executor contract. No reads or rules for those objects
were added. Targeted validation now passes 31 panel-preview and view-context tests,
including an inclined direction and future object-source kinds.

Second rule transfer completed (2026-09-26): timber-panel part-location calculation
now implements the same `IDimensionRule` contract and executes in one ordered rule
set with `OverallDimensionRule`. `TimberPanelPartLocationSettings` owns the minimum
segment setting, while `TimberPanelPartLocationInput` supplies the captured group,
selected part IDs and lazy contact access. The rule returns four location proposals
and one evaluation-level diagnostic set. The result evidence retains point roles,
part ownership, dropped short parts and incomplete status so the compatibility
adapter produces the unchanged preview response.

This transfer exposed a real contract requirement: diagnostics belong to one rule
evaluation rather than every chain. `DimensionRuleEvaluation` therefore contains
the proposed chains and a single diagnostic dictionary; `DimensionRuleSet` combines
evaluations and rejects duplicate diagnostic names. Geometry and lazy contacts are
still reused from the captured context. Targeted validation after the transfer
passes the same 31 panel-preview and view-context tests. No live Tekla check or
deployment was performed.

Third rule transfer completed (2026-09-26): the verified rectangular/I-profile
face selection in `SectionDimensionChainPreview` is now
`SectionProfileDimensionRule`. Its immutable input contains the already verified
main-part id and X/Y profile levels; its settings contain the current side and
coordinate tolerance. It runs through `DimensionRuleSet`, returns the shared
direction/placement/result contract, and the existing section preview consumes
those points for its unchanged location and merged-chain calculations. The rule
context is created once per section preview and reused for every requested side.
The API project builds successfully. Existing dependency and nullable warnings
remain; automated and live Tekla checks were not run in this transfer.

Fourth rule transfer completed (2026-09-26): section part location is now
`SectionPartLocationRule`. It consumes the preceding profile proposal, preserves
side ownership, profile-located parts, readable-gap merging and located-part
evidence, and feeds the unchanged section consolidation calculation. To support
this real dependency, `DimensionRuleSet` now executes rules in order and supplies
accumulated proposals through `DimensionRuleContext.PriorResults`; it still reuses
the same detached points, geometry adapter and captured inputs. Independent rules
ignore prior results. The API project builds successfully. Existing dependency and
nullable warnings remain; automated and live Tekla checks were not run in this
transfer.

Fifth rule transfer completed (2026-09-26): the active standard steel location
path now uses `SteelPartLocationRule`. It preserves point roles and part ownership,
parts offered on the opposite side, skipped parts and points dropped by the minimum
segment rule. `DimensionChainPreview` adapts the generic proposal to the existing
detailed and short response; its overall chain is unchanged. The API project builds
successfully. Existing dependency and nullable warnings remain; automated and live
Tekla checks were not run in this transfer.

Selection authority clarified by the user and the project skill (2026-09-26): a
rule result is a safe base proposal, not the final dimension. The LLM remains
responsible for the final chain plan and may remove proposed points with an explicit
drawing reason. It may select only real captured support points and may not invent
replacement coordinates. The bridge validates and writes the chosen chain, followed
by read-back verification. This already works through the skill and the existing
`create_dimension` point-ID input. Do not introduce a second selection or write-plan
mechanism unless a concrete limitation of that workflow is demonstrated.

Goal: separate settings, rule calculation and writing dimensions to Tekla while
preserving the current behavior. Existing preview classes combine these decisions
and repeat some operations; extract boundaries from the working code rather than
trying to anticipate every future dimension type.

Tekla's dimensioning system is a source of examples only. Matching its internal
architecture, rule classification or settings file format is not a requirement.
The architecture must allow rules to evolve as actual drawing cases reveal needs.

Architectural scope clarified by the user (2026-09-26): chains may follow an
inclined face/edge and may lie inside an assembly. Top/Bottom/Left/Right and
placement outside the outline describe the current rules, not all dimensions.

- Keep chain direction separate from line placement. A future chain can have an
  arbitrary direction in the view plane, with a reference and offset locating its
  dimension line; neither property must be inferred solely from a cardinal side.
- Keep the dimensioned geometry (assembly, part, face/edge, bolt or bolt group,
  reinforcing bar or reinforcement group) separate from where
  the dimension line is placed. Interior supports and interior lines are valid
  future cases; an outer-outline gap is a placement policy for applicable rules.
- Point provenance must be able to identify these object kinds and their relevant
  geometric references. A part contour or part ID alone is not a universal source
  contract. Add access to bolt/reinforcement geometry when an actual rule needs it;
  existing rules must not trigger those reads merely because the context supports
  additional object kinds.
- Ordered points and the actual first point remain explicit, including for inclined
  chains. Ascending X/Y ordering is a convention of current axis-aligned rules,
  not a universal ordering requirement.
- The current `DimensionRuleResult.Side`, side-indexed point catalog and context's
  side-extreme helper are limitations of the first implementation. Do not make
  them mandatory assumptions of the general executor or future rule contracts.
  Revisit these boundaries as rules are transferred; direction/placement metadata
  must be able to extend the result without rewriting every rule.

Inclined and interior-chain calculation, bolt/reinforcement rule integration,
placement and live validation are deferred in this architecture step.
This is a constraint on the architecture now, not a request to implement those
features or design all their settings in advance.

Minimum boundaries:

- **Settings:** parameters used by a rule; no geometry calculation. Initially keep
  current values and units. No settings-file infrastructure is required for this step.
- **Context:** access to the existing captured view geometry, point catalog and lazy
  contacts. Reuse the current snapshot and its completeness/identity checks.
- **Rule:** calculate a proposed result from the context and its settings, without
  creating or modifying dimensions in Tekla.
- **Result:** proposed dimension chains and enough evidence to retain current
  coverage and unresolved-case diagnostics.
- **Rule set:** the configured rules and their execution order.
- **Tekla writing:** use the existing creation and verification path to apply the
  selected result; preserve its failure handling and compensation boundaries.

These are responsibilities, not a fixed list of classes or interfaces. Refine the
concrete contracts while transferring existing logic. Extract shared operations
when the transferred rules demonstrate a common need.

Order of work:

1. Select one small existing rule and its reference cases; record its current
   output. Transfer its calculation and settings through the minimum boundaries.
2. **Done:** transfer timber-panel part location. This added evaluation-level
   diagnostics while preserving its response, point order and lazy contacts.
3. **Done:** transfer section-profile point selection from a different preview
   domain while preserving the existing adapter and downstream merge calculation.
4. **Done:** transfer section part location using the profile proposal as an input.
   The executor now supports ordered dependencies without repeating geometry reads.
5. **Done:** transfer the current steel location rule, including its skipped,
   opposite-side and dropped-point evidence.
6. **Done:** after behavioral verification, remove the inactive legacy steel
   location calculation duplicated by `SteelPartLocationRule`. The remaining
   overall-chain helper was reduced to its active behavior. Keep the current
   skill-driven choice of ordered point IDs and the existing `create_dimension`
   boundary; add no parallel selection mechanism. Keep behavioral fixes separate
   from extraction.

Acceptance:

- Existing reference cases produce the same chains, ordered points and diagnostics.
- Geometry is reused; extracting rules does not introduce repeated geometry reads.
- Existing write checks, correction and failure recovery remain in place.
- A further rule can be added with its settings and connected to a rule set without
  changing the shared execution or Tekla writing mechanism for that addition.
- Latest panel fixes still have the separate live acceptance gates listed above;
  passing extraction checks does not close those gates.

Deferred until concrete needs justify them: an exhaustive catalog of rule classes,
settings file format/location/versioning/editor, and support for roofs, floors,
openings or other cases not yet established by the current implementation and
reference drawings. These decisions must not block the first rule transfers.

### 5. Compute contacts from the view snapshot and consolidate MCP reads

Partially implemented. `get_view_dimension_context(questions="contacts")` now
uses an explicit `all-depth-visible` scope, independent of structural dimension
exclusions. On the first request it reuses captured solid DTOs and reads only
missing depth-selected parts; it caches both those reads and the computed
candidate/contact result until refresh or drawing/view change. Ordinary
dimension questions and `all` do not trigger contact work. Contact completeness
is reported separately from outline completeness. The old contact MCP tools and
their debug-drawing path remain available.

Live comparison reported on the M.505 beam view with `refresh=true`: the context
and `get_contact_candidate_points` both returned 52 points across 9 parts, with
matching participant pairs, anchor keys, contact states, and empty unread,
unflattened, and unresolved lists. Coordinates differed only below 0.001 mm;
the display serializer was then corrected to emit the rounded decimal cleanly.

Live timing (first `contacts` question measured separately from context
construction, from the perf log; all part solids were already captured):

| View | Parts | Points | Context build (`refresh`) | First `contacts` | Whole call | Answer size |
|---|---|---|---|---|---|---|
| M.505 beam view | 9 | 52 | 433 ms whole call | 8 ms | 33 ms | 21 KB |
| M.48 view, main part about 32 m | 21 | 92 | 1231 ms | 33 ms | 58 ms | 63 KB |

Contact calculation is about 3% of context construction on these views, so
laziness saves little time here; it only avoids work when contacts are not asked
for. Both views are small: check a view with many parts before relying on this,
because pairwise cost grows faster than the part count.

Independence from dimension exclusions, checked on M.505 with `excludePrefixes=P`
(the prefix of all 9 parts): the structural outline became empty
(`isComplete=false`, "every part in this view was excluded by the filter") while
contacts stayed at 52 points and the same pairs, `exclusionsApplied=false`.
A partial exclusion (some parts excluded, the rest not) is still not checked.

Compact contact response implemented (2026-09-26): `questions=contacts` now
returns pair/count/state summaries without point or shape arrays.
`questions=contactDetails` explicitly returns every retained contact point and
flattened shape, including `contactId`, shape id, participants, state, kind,
normal and view-plane points. Supplying `contactPair="id1,id2"` with `contacts`
returns those details only for the named pair. Resolved shapes are retained in the
cached contact snapshot, so these modes reuse the same geometry read and contact
calculation. The API, bridge and MCP server build successfully; 81 focused context,
contact and tool-contract tests pass. Live answer sizes are not yet remeasured.

The M.48 view returned contact state `Overlap` between a plate and the main part;
confirm that it is expected.

Still required before considering this complete: compare a view where some parts
are excluded by dimension rules and others are not; live-compare the newly exposed
full contact shapes and `contactId`; expose/compare the constrained-axis summary from
`get_part_degrees_of_freedom`; measure a view with many parts; and deploy the
rounding fix to the bridge (the deployed bridge still printed values such as
`4704.0010000000002`). Only after them decide whether to remove the old MCP tools.

### 6. Batch-write the reviewed dimension plan

Implemented and deployed (2026-09-27); one live attempt made 2026-09-28, acceptance
still pending.
`create_dimensions_batch` accepts the final, AI-reviewed set of dimension
chains for one view. The batch supplies one existing `contextId`; each chain
supplies ordered `pointIds`, direction, paper gap or explicit distance, and attributes. The AI
continues to choose the rule set, sides and kept/removed points; the batch tool
must not silently select or prune candidates.

The batch operation validates the full request before writing, then applies chains
sequentially through the existing dimension-write protocol. Each chain can optionally
request a Tekla row type (`Relative`, `Absolute`, or `RelativeAndAbsolute`); the MCP
tool description documents this JSON field and its override of the attributes file.
Preserve per-chain
read-back, correction and cleanup behavior. Return a compact per-chain status
(`created`, `retained`, `merged`, `failed`, `skipped`, or `uncertain`) and perform one final compact view
read to report what actually remains, including whether an overall chain merged
with a location chain. A later chain failure must not make earlier successful
writes ambiguous; report each result without speculative retries.

The MCP server and bridge build, focused batch/context checks pass, and the MCP
stdio tool-list check confirms the deployed server advertises this operation. The
live drawing acceptance items below remain open until it is tried on an authorized
drawing.

Acceptance:

- All chains in a reviewed plan can be submitted in one MCP call without changing
  the current point-selection responsibility or write validation.
- Invalid context/point IDs are rejected before the first write.
- The final response identifies created, retained, merged and failed chains and
  matches the compact read-back from Tekla. The 2026-09-28 run failed this gate: a
  requested Absolute overall was reported absent, then briefly appeared to the arrange
  command, and was absent from the subsequent read; the existing RelativeAndAbsolute
  overall remained. Resolve this state/status mismatch before calling live acceptance.
- On an authorized test drawing, compare the same placement before and after for
  total elapsed time, MCP/bridge call count, response size, and token usage when
  the runtime exposes it. Do not claim token savings from character count alone.
- Verify retry behavior after partial success and verify overall/location merge
  outcomes, including a requested row type differing from an existing merged chain;
  never count a merged overall as a separate visible dimension.

### 7. Preview-only context: no raw points for the assistant (source implemented 2026-10-02)

Goal: the assistant takes the prepared chains from the preview and decides which
to keep or remove, without reading every source candidate. This saves tokens and
time; it is not an access-control measure. Implemented in source; not deployed.
Automated coverage checks the shared question whitelist, MCP rejection before
bridge dispatch, default preview output, per-side rows beyond two chains,
retention at existing distances, ambiguous matches and occupied-side validation
before any write. Deployment and same-view live performance/placement gates
remain open.

- `get_view_dimension_context` accepts only `chain`, `chainDetails`, `parts`,
  `scale`, `diagnostics`. `points`, `dimensionPoints`, `edges`, `all`,
  `contacts` and `contactDetails` are rejected
  before the snapshot is built, not ignored. Default becomes `chain,scale`;
  `viewId`, `sides`, `ruleSet`, exclusions and `refresh` stay.
- Restrict in the MCP wrapper and bridge handler, not in `ViewDimensionContext.Query`:
  tests build point IDs through `Query("dimensionPoints")` and the check inside
  `Query` runs after the snapshot exists. Keep `chainDetails` for targeted
  inspection of a prepared chain; contacts are not exposed by this tool.
- Each chain returns purpose, side, selected `pointIds`, segments and an
  incompleteness flag with the concrete reason. An incomplete chain is not rebuilt
  automatically, but keeps its `pointIds` so it can still be placed.
- Rows are assigned when a batch is created: per side, in order among the kept
  chains; a single chain gets row 1, and `overall` alone does not imply row 2.
  This replaces the preview `row` field added on 2026-10-02. A row does not
  prevent Tekla from merging an overall into a location chain.
- Update `.agents/skills/dimension-drawings/SKILL.md` step 3 to
  `questions="chain,scale"`. Check that steel chains closing on plate edges
  (steel-rules) are still reachable from the preview.
- Gates: a rejected question fails before geometry is read; `chain,scale`
  returns ready chains; a call without `questions` returns no source points.
  Repeating the same batch without explicit offsets retains matching chains;
  a new chain on an occupied side requires an explicit offset before any write.
  Measure response size and call time before and after on the same view.
- Out of scope: closing other readers of source candidates
  (`get_structural_chain_positions`, `get_*_points_in_view`, raw `points` in
  `create_dimension`).

Review decisions implemented in source (2026-10-02; live fixture gates remain):

- **Incomplete chains.** The preview must name the concrete reason per chain.
  Default: no `pointIds` or a failed outline blocks creation; a missing support
  for some parts or unlocated parts (as on EW.7 Left, `missingSupportModelIds`)
  lets the assistant choose, with the reason shown in the answer.
- **Row scope on partial creation.** Existing dimensions have no stored `row`,
  only a distance and a line position, so a free row cannot be found by counting
  dimensions on a side, and manual offsets make counting wrong. Default: automatic
  row numbering applies only to a side with no existing dimension. Only NEW
  chains on an occupied side require `paperGapMm` or `distance`; retained chains
  keep their existing placement without requiring a repeated offset. A free-space
  search rule is a later decision.
- **Retained chains come first.** The batch retains a chain only if its points,
  side and offset (distance within 1 unit, `FindMatching` in
  `ViewDimensionContextProvider.Batch.cs`) all match, so a chain with a different
  offset is not retained. Order: recognise the chains to retain, then assign places
  to the new ones. Implemented repeat behavior: when no explicit offset is supplied,
  first identify a unique existing match by points, side and requested dimension
  type, and retain its current offset. Do not guess if several existing chains
  match. An explicit offset must still match the existing placement for retention;
  requesting a different offset does not silently retain the old placement.
- **Precedence.** `row`, `paperGapMm` and `distance` are all in the batch contract.
  The old batch `row` input is ignored; preview rows are removed.
  Keep: `distance` over `paperGapMm` over the automatic row; `distance` together
  with `paperGapMm` stays rejected. Automatic rows apply only when neither is given.
- **Measurement.** Measure the whole cycle: preview, keep/remove decision, create,
  verify. Report separately: server time, permission-review time and decision time,
  plus call count and response size. On 2026-10-02 five `delete_dimension` calls took
  about 50 ms each on the server but were spaced 3-6 s apart; the Codex diagnostics
  (`.codex/diagnostics/codex-mcp-latency-20261002.md`) attribute that to automatic
  approval review before dispatch, not to Tekla and not shown to be model turns.

### 8. Reference prepared chains in the batch (stage 1 source implemented 2026-10-02; not deployed)

Goal: stop retyping preview point IDs into `create_dimensions_batch`. On 2026-10-02
(CE.4, five chains) the server needed 3.9 s of a 15.5 s run; the longest observed pause
before a call was about 7.4 s. These timings alone do not establish how much of that
pause was composing `chainsJson`, model inference, client processing or approval review.
Removing repeated point-ID output is an expected optimization; its actual benefit, in
seconds or in tokens, must be measured.

Historical measurements for five-chain batches on different drawings: batch 25.2 s, then
8.1 s after the shared presentation connection, then 2.2-2.9 s after reading dimensions
without text bounds (`0e940ae`). These runs are context, not a controlled same-view
before/after baseline.

Stage 1 is implemented in source: extend `create_dimensions_batch`, no new tool.

- **Entry form.** An entry is either `{"preview":"<chain key>"}` or has `pointIds`;
  passing both is an error. `direction` of a reference comes from the preview and cannot
  be overridden. `key` defaults to the `preview` value; two entries naming the same chain
  are rejected before any write. The existing rule that each entry has a unique
  non-empty `key` still applies to `pointIds` entries.
- **Resolution is explicit.** A context stores geometry only: `ruleSet` and the section
  variant are chosen per query, so one `contextId` can yield different previews. The batch
  therefore takes `ruleSet` (default `steel`, as in the preview) and `chainView`
  (`chain`, the default, or `chainDetails` for the split section chains) as batch-level
  arguments and recomputes the preview from the frozen context with them. No `previewId`.
  The same context, `ruleSet` and `chainView` give the same chains and point order, so
  the assistant must pass the values it used when it read the preview. The panel preview
  is built for all sides and `sides` only filters its output, so `sides` is not an input
  of the resolution. Automated steel, panel and section checks compare a Top-only answer
  to the Top chains from an all-side answer, including both section variants. Steel reads
  the opposite side's catalog, independently of which sides were requested for output.
- **Keys** are `<Side>-<kind>`: `Top-location`, `Bottom-overall`, `Right-overall`, and
  `Top-chain` for a consolidated section chain. Direction follows the side (`horizontal`,
  `horizontal-down`, `vertical-left`, `vertical`); overall chains default to the
  `overall` attributes file, other chains to `standard`.
- Per-entry `paperGapMm`, `distance`, `attributesFile` and `dimensionType` keep their
  current batch meaning, so no separate overrides structure is needed. Rows, retention of
  existing dimensions, the explicit-offset rule on occupied sides, verification and
  read-back are those of the existing batch.
- Nothing is placed that the caller does not name: removing a whole chain means not
  listing it (for example the empty `Right-location` that the preview marks as covered
  by Left).
- Unknown key, a key whose chain has no `pointIds`, a `contextId` that expired or was
  invalidated (drawing/view switched, refreshed), or a `ruleSet`/`chainView` value that is
  not valid is rejected before any write.
- An entry with `pointIds` keeps working unchanged, so steel chains that close on plate
  edges (see `steel-rules.md`) are unaffected.

Point selection stays with the preview rules. Extra points that recur are fixed in the rule
(for example `TimberPanelPartLocationRule`). Excluding parts with `excludePrefixes` or
`excludeMaterials` is a different thing: it chooses which parts are dimensioned at all.
It can remove supports that are needed or change the overall, and it makes a different
context, so the batch must then use the `contextId` of that filtered read. It is not a way
to remove one extra point.

Stage 2, only if a concrete need appears: `dropPointIds` on an entry, by point ID and not
by index (`{"preview":"Top-location","dropPointIds":["p0012"]}`). It would need a
validate-only mode that returns the chain and its segments after the drop before any
write, protection for the first and last point of every chain (the first point is the
datum, see `SKILL.md`), and a rule for repeat runs (retention matches points, so a rerun
without the same drops would not recognise the earlier chain). Not designed further now.

**Zone=0 reference polygon for timber panels (discussion only, 2026-10-06):** idea is to
read the ZONE user attribute now on `DrawingPartInfo`/`PartRoleInView`/`ViewDimensionContext`
(frame=0, OSB=1, counter-batten=2, cladding=3 in the plants seen so far) and mix a reference
polygon built from the frame's own parts into `TimberPanelPartLocationRule`'s existing
`OutlineCoordinates` candidate mixing, the same mechanism the rule already uses for its own
panel outline.

ZONE is signed like a building's floors counted from ground level: 0 is the frame itself,
positive zones are layers built outward from it (siding, cladding), negative zones are layers
built inward (like underground floors). A set-valued `referenceZones` (see below) should be
read with this in mind — `zone > 0` / `zone < 0` is as natural a selection as an explicit list,
not just "zone equals 0". Each zone's polygon is effectively its own floor plan: a flat,
per-zone projection of that layer's parts, the same way a building's floor plan is a
horizontal slice at one elevation — which is exactly what `ProjectedOutlineBuilder` already
produces per filtered part set, one zone at a time.

**Why:** a higher-zone part (e.g. an OSB sheet, zone 1) is fastened to the frame (zone 0) and
is not self-locating — like a ship's superstructure on its deck, its own outline only says
where the plate itself ends, not whether that edge lines up with, overhangs, or falls short of
the frame it is nailed to. The point of mixing in the frame's outline is not "draw the frame
too" but to put a frame coordinate next to the plate's own coordinate on the same chain side,
so the dimension shows the installer the actual offset/overlap between plate edge and frame
edge — the number someone on site would otherwise have to measure by hand. The dependency is
one-directional: the frame (zone 0) is the datum the plate (zone 1) is fastened to and measured
against, never the reverse. Open points, none implemented:
- the reference zone must be a caller-supplied collection (e.g. `referenceZones`), not a
  literal "0" in the rule, matching the no-plant-conventions-in-code rule elsewhere
  (`PartExclusionRule`, `PartLayerRule`); a union of several zones is a plausible later need
  but not required now
- "Zone=0" alone is not the frame geometry: insulation/sealant parts can carry Zone=0 too and
  must still drop out through the same `excludePrefixes`/`excludeMaterials` the panel itself
  uses, not just a zone match
- the zone-filtered ID source should be `_partAttributes` (all depth-selected parts: Included +
  Excluded + Unclassified), not `outline.Included`, since the frame is typically excluded from
  the panel's own outline by the very filter that isolates the panel
- avoid reading any part's solid twice: one `GetAssemblyOutline` call over the union of the
  panel's IDs and the zone-filtered IDs reads each unique solid once; the panel outline and the
  zone-reference outline are then two separate, Tekla-free calls to
  `ProjectedOutlineBuilder.BuildAssembly` over disjoint subsets of the same already-read
  `PartOutlines`/`PartNodes`
- side-filtering of the zone polygon's vertices (`OutlineCoordinates`'s `panel.MinX/MaxX` cut)
  must use the current panel's own bounds, not a global cut, so a neighbouring panel's frame
  vertices are never pulled in
- a reference-zone point must not be tagged with the frame part's `modelId` as an `Owner`:
  `Owners` feeds `missing` directly, and a frame part is not one of this panel's members.
  ReferenceZone candidates therefore have no owners. `CandidateKind` is implemented in
  `TimberPanelPartLocationRule`; it is retained on each selected point and exposed in the preview.
  When candidates at one coordinate coalesce, the point keeps every contributing kind; there is
  no precedence between them. `located`/`dropped` use `AccountedPartIds`, which reads the chosen
  `DimensionPoint`'s `Parents`, limits IDs to the panel's included member IDs, and skips a point
  sourced only from ReferenceZone. Thus a coincident panel candidate still accounts for its own
  panel IDs, while the frame ID is never reported as a panel member. The polygon and producer for
  ReferenceZone candidates remain future work. The enum currently has `Member`, `PanelOutline`,
  and `ReferenceZone`, and can be extended for another point source if a concrete need appears.
  This change stays inside `TimberPanelPartLocationRule`; bolt chains build points through an
  unrelated mechanism and remain out of scope unless a concrete need to unify them appears.
Automated gates: an unknown or stale chain key, an entry with both forms, a repeated chain or an
invalid `ruleSet`/`chainView` writes nothing; references and explicit `pointIds`
produce identical dimensions for the same chain; a repeated batch retains a referenced
chain as it does an explicit one. Explicit entries can be mixed with references and keep
their standard attributes default. Preview references expose keys, directions and default
attributes; serialized references preserve the overall default. The bridge rejects deferred
`dropPoints`/`dropPointIds` instead of silently ignoring them.

Deployment and live placement/performance gates remain open. Performance gate: compare equivalent
manual-batch and referenced-batch runs on the same view, with identical initial
dimensions, chains, attributes and offsets, and report wall-clock, call count and request
size. Separate cold and warm connection runs and use client telemetry to distinguish
model, transport, approval and tool time where available; unexplained gaps stay
unexplained. Do not promise a fixed saving.
