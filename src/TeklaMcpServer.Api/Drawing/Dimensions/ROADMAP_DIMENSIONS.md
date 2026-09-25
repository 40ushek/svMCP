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

#### 4a, fourth stage: chain preview for timber panels (planned)

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
  the panel: 120 / 427.5 / 625 / 625 / 625 / 535 / 120.
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
3077.54 (120 / 427.5 / 625 / 625 / 625 / 535 / 120); overall 3077.5; Left Y positions
-1278.5, -1233.5, -1173.5, 1053.5, 1233.5 (45 / 60 / 2227 / 180); Right Y positions
-1278.5, -1233.5, -1173.5, 1053.5, 1233.5, 1453.5 (45 / 60 / 2227 / 180 / 220).

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
