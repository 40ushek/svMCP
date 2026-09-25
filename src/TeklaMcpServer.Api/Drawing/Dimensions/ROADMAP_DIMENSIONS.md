# Dimensions Roadmap

Updated 2026-09-25. This file is the active work order. Steps 1-3 have source
implementations and automated tests; placement/write acceptance still has live
gates. Step 4 remains design. Contact candidate reuse is implemented, with the
remaining comparison and performance gates listed in step 5.

## Current delivery and next gate

- Persistent `ViewDimensionContextProvider` shares frozen, filter-specific
  geometry between chain reads, short context queries and automatic creation.
  Chains are calculated lazily; explicit-distance creation needs no outline.
- `get_view_dimension_context` answers points/edges/parts/scale/placement and
  opt-in `contacts`. `all` deliberately excludes placement and contacts. Queries
  return detached JSON; candidate choices cannot mutate the snapshot.
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
- Next: deploy on an authorized test drawing, validate four sides and 1:5/1:10,
  ambiguous bases and shortened views, then measure full-task time and response
  size on the agreed cases. No live speedup or placement guarantee is claimed.
  Trace events count context builds/hits, not individual solid reads.
- Contact live comparison on M.505 matched the old command: 52 points, 9 parts,
  matching participant pairs, anchor keys and contact states; no unread,
  unflattened or unresolved items. Separate first-query timing, an excluded-part
  case, full contact-shape output and degrees-of-freedom equivalence remain open
  (see step 5).
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

Automatic templates, whole-view planning and batch writes remain later work.
They require measured benefit and their own coverage/retry validation. Bolts and
solid clipping retain their separate roadmaps. Do not reintroduce the removed
plan commands or a second creation path as a prerequisite for this increment.

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

The `contacts` answer is too large to read: 21 KB for 52 points, 63 KB for 92.
Adding contact shapes would grow it. Default to a short summary (pairs, counts,
states) and return points or shapes only on request or for a named pair.

The M.48 view returned contact state `Overlap` between a plate and the main part;
confirm that it is expected.

Still required before considering this complete: compare a view where some parts
are excluded by dimension rules and others are not; include/compare full contact
shapes and `contactId` (the context currently returns candidates and unresolved
shapes, not every shape); expose/compare the constrained-axis summary from
`get_part_degrees_of_freedom`; measure a view with many parts; and deploy the
rounding fix to the bridge (the deployed bridge still printed values such as
`4704.0010000000002`). Only after them decide whether to remove the old MCP tools.
