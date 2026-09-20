# Roadmap Bolt Geometry

## Goal

Introduce a canonical bolt-aware geometry library under
`Drawing/Geometry/Bolts`.

The goal is to expose raw bolt group geometry in drawing/view-local
coordinates first, and only later derive higher-level connection or contact
semantics from it.

Current priority order:

- raw bolt positions in view-local coordinates
- bolt group relation data to connected parts
- reusable bolt group bbox and anchor points
- derived bolt reference and extreme points

Not in scope for the current stage:

- MCP transport design
- bridge command design
- contact inference
- dimension or mark behavior

## Architectural Decision

The canonical location for this work is:

- `TeklaMcpServer.Api/Drawing/Geometry/Bolts`

Reasoning:

- `BoltGroup` is its own model entity, not a subtype of `Part`
- bolt geometry may relate to multiple parts at once
- bolt-aware geometry should be reusable by dimensions, marks and later
  connection logic without living inside those modules

Module split should stay explicit:

- `Drawing/Geometry/Parts`
  - raw part solids and part-derived geometry
- `Drawing/Geometry/Bolts`
  - raw bolt groups and bolt-to-part relations
- `Drawing/Geometry`
  - shared geometric helpers

## Current Base In Code

The module should build on top of runtime data already available from Tekla.

Current relevant sources:

- `BoltGroup.BoltPositions`
- `BoltGroup.FirstPosition`
- `BoltGroup.SecondPosition`
- `BoltGroup.PartToBeBolted`
- `BoltGroup.PartToBoltTo`
- `BoltGroup.GetOtherPartsToBolt()`
- `BoltGroup.GetSolid()`
- `Part.GetBolts()`

This means the first useful library layer is a raw bolt group geometry layer,
not a dimension-driven interpretation of bolts.

Current implemented state in the wider geometry stack:

- `Bolts`
  - raw bolt geometry and derived bolt points are implemented
- `Assemblies`
  - bolt groups are reused in aggregate assembly geometry
- `Nodes`
  - bolt groups are reused for work points and connection-aware nodes

## Canonical Domain Direction

The target model should represent bolts in two layers.

### Layer 1: Raw Bolt Geometry

The raw layer should expose:

- bolt group identifier
- bolt group shape and basic metadata
- `FirstPosition`
- `SecondPosition`
- individual `BoltPositions`
- optional bolt group solid bbox
- identifiers of related parts

### Layer 2: Derived Bolt Geometry

The derived layer should later expose:

- bolt group axis or reference line
- bolt group center / bbox corners
- bolt-driven anchor points for part geometry
- later connection-aware or contact-aware semantics

## Raw Geometry First

The canonical source of truth must be raw bolt geometry, not pre-baked point
bags.

At the current stage the most important geometry contracts are:

- `BoltPointGeometry`
- `BoltGroupGeometry`
- `BoltGroupGeometryInViewResult`
- `PartBoltGeometryInViewResult`
- `IDrawingBoltGeometryApi`
- `TeklaDrawingBoltGeometryApi`
- `DrawingBoltPointKind`
- `DrawingBoltPointInfo`
- `GetBoltGroupPointsResult`
- `IDrawingBoltPointApi`
- `TeklaDrawingBoltPointApi`

These contracts should be sufficient to:

- inspect real bolt locations in the current drawing view plane
- reuse bolt-to-part relations without re-reading Tekla in every consumer
- later derive bolt-driven geometry for parts and assemblies

## Geometry Rules To Preserve

- the canonical output is reusable geometry data, not one-off dimension inputs
- coordinates should be emitted in view-local coordinates
- bolt-to-part relations should be preserved as explicit IDs
- raw bolt geometry should remain independent from contact inference
- consumers should reuse extracted bolt geometry instead of re-reading Tekla
  runtime objects ad hoc

## Practical Consumer Scenarios

- read one bolt group geometry in drawing/view-local coordinates
- read all connected bolt groups for one part
- build later bolt-driven anchor points for geometry consumers
- reuse the same bolt group geometry for dimensions, marks or contact logic

## Phases

### Phase 1: Folder And Domain Boundary

Status: done in first form.

Done when:

- `Drawing/Geometry/Bolts` exists as the agreed home for bolt geometry
- roadmap and naming make the module boundary explicit

### Phase 2: Raw Bolt Group Geometry

Status: done in first form.

Minimum output:

- `BoltPositions`
- `FirstPosition`
- `SecondPosition`
- `PartToBeBoltedId`
- `PartToBoltToId`
- `OtherPartIds`
- optional bolt group solid bbox

Done when:

- one API call can return canonical raw geometry for a bolt group
- one API call can return connected bolt groups for a part
- all returned coordinates are in view-local coordinates

### Phase 3: Derived Bolt Geometry

Status: done in first form.

Target additions:

- bolt group center
- bolt group axis
- bbox corners
- stable bolt-driven anchor points
- hull and extreme points

Done when:

- one API call can expose reusable derived bolt points for one bolt group
- one API call can expose derived bolt points for all bolt groups of one part
- derived points are computed from the raw bolt geometry layer

### Phase 4: Connection-Aware Geometry

Status: partially addressed outside this module.

Target additions:

- bolt-to-part geometric pairing
- later contact-aware connection semantics

Current note:

- bolt-to-part pairing is currently consumed in `Drawing/Geometry/Nodes`
- a richer bolt-local connection layer is still open here if bolt-specific
  pairing rules are needed independently from nodes

## Acceptance Criteria

The roadmap is considered successfully implemented when:

- `Drawing/Geometry/Bolts` is the obvious home for bolt geometry
- raw bolt geometry is available without MCP or bridge transport concerns
- bolt-to-part relations are reusable from one stable library contract
- later bolt-driven or connection-aware geometry can be added without redesign

## Near-Term Next Step

For the drawing-dimension gap, follow the explicit logic in **Bolt dimension
logic** below. Do not add more point kinds or a second bolt reader before the
view command and the planner contract exist. Generic axis/reference-line work
remains useful to the geometry module, but it is not a substitute for deciding
which bolt dimensions the drawing actually needs.

## Open: bolt dimensions have no path to the drawing at all (2026-08-23)

`steel-rules.md`'s "Not covered" section already says bolts are out of scope for
placement: `BoltArray`/`BoltGroup` is not in the structural outline this skill reads, so
edge distances, spacing and extreme-bolt positions cannot be planned. This entry records
*why that gap matters* and what closing it actually needs, since the two are different
questions.

**Motivating case.** M.16, SectionView `2152`: a base plate with 4 bolt holes. The
column's own cross section is symmetric on both axes, and the plate can be too - nothing
in the *outline* forces one orientation over its 180°-rotated twin. If the bolt hole
pattern is itself asymmetric (unequal edge distances, an offset hole), that asymmetry is
the only thing on the sheet that can prevent the plate being welded in rotated by 180°.
Skipping bolt dimensions here is not a cosmetic gap - it can produce a wrong assembly that
passes every other check in `steel-rules.md`'s Checks section.

**What already exists.** This roadmap's own Phase 1-3 are done: `TeklaDrawingBoltGeometryApi`
reads `BoltPositions`/`FirstPosition`/`SecondPosition` in view-local coordinates today.
Confirmed live: `get_part_points_in_view` and `get_all_parts_geometry_in_view` both refuse
a bolt model id (`BoltGroup` is not a `Part`), and grepping
`TeklaBridge/Commands/` for `BoltGeometryApi`/`get_bolt_geometry` returns nothing - the
raw geometry layer is real, but genuinely unreachable from any MCP tool today. That is a
smaller, mechanical gap (bridge command + MCP wrapper, same shape as every other
`Get...InView` API already wired) - separate from and prerequisite to the larger one.

**What is still missing after that.** Wiring the existing API to a command only gets bolt
*positions* out. Steel-rules.md's own list of unmet bolt needs stays open: edge-distance
and spacing checks, extreme-bolt selection for a dimension chain, and the "which hole
pattern would a fitter actually need called out" judgment a plant applies to bolts, which
this skill has not worked out for any other geometry type either. Treat MCP exposure as
phase 1 of closing this gap, not the whole of it.

### Planned command: `get_bolt_groups_in_view <viewId>` (not built)

**Decision (2026-08-23): read via `BoltPositions`/`FirstPosition`/`SecondPosition`, not
via `BoltGroup.GetSolid()`.** A bolt hole is fully described by a centre point and a
diameter (`BoltSize` is already a plain property); nothing about it is concave or
profile-shaped the way a rolled beam cross section is. Reading it through a solid would
re-import the exact class of problem this project just spent 2026-08-23 fighting for
beams - `NORMAL` vs `HIGH_ACCURACY` facet count, a convex hull losing real shape,
polygonized fillets - for a shape that never needed any of it. `BoltPositions` is also
the canonical definition Tekla itself uses to place a bolt group, not a derived
approximation, so reading it is not "less accurate," it is the more direct source.
Build `get_bolt_groups_in_view` against the position API family already listed in
"Current relevant sources" above.

**Must not repeat Bug 1 from `ROADMAP_SECTION_CROSS_SECTIONS.md`.** That bug was
`view.GetObjects()`/`IsHidden` answering with the whole drawing database instead of what
the view actually draws, for parts. The same failure mode already reproduces for bolts
today, live: `filter_drawing_objects` with `objectType=Bolt` and `viewId=1709` (M.505, an
`EndView`) returned 28 drawing-object entries on 2026-08-23, but only **4 distinct model
ids** among them - the same 4 repeated across every view on the sheet, not the ones
`1709` draws. Whatever reads bolts for this command must go through the same
`GetDepthFilteredParts`-style depth-aware selection already built for parts, not a bare
`view.GetObjects()` pass repeated for `BoltGroup`.

**Response shape**, mirroring `AssemblyOutlineResponse`/`ViewAssemblyOutlineResult` so a
future bolt dimension chain can reuse the same completeness reasoning parts already have:

- `viewId`, `isComplete`
- `boltGroups`: one entry per bolt group actually drawn in the view - group id,
  `FirstPosition`/`SecondPosition`, individual `BoltPositions`, related part ids
  (`PartToBeBoltedId`, `PartToBoltToId`)
- `outsideDepthModelIds` - bolt groups the depth window excludes, same meaning as the
  part-level field of the same name
- `unresolvedDepthModelIds` - bolt groups whose depth relation could not be read safely
- `unread` - a bolt group whose geometry read failed outright, with a reason, same
  `UnreadPart`-shaped list already used elsewhere

**Live target for the eventual command, recorded now as a check to run once it exists.**
M.505, `EndView` `1709`: the end plate carries 4 bolts (model ids `10104522`, `10097208`,
`10100573`, `10128680`, confirmed above). `get_bolt_groups_in_view 1709` should return
exactly these 4, not the whole model's bolts, and not the base plate's own bolts from a
different view on the same sheet.

### Order of work

1. This roadmap entry (done).
2. Bridge command + MCP wrapper for `get_bolt_groups_in_view`, verified against the
   M.505/1709 live target above.
3. Edge-distance/spacing dimension logic on top of it - not started, not designed yet.

The raw `TeklaDrawingBoltGeometryApi` already exists. The bridge command, the pure
bolt-pattern planner, and dimension creation/verification remain to be written.

### Before designing phase 3: Tekla rules are the input, not our implementation

Official Tekla documentation confirms that bolt dimensions have their own rules and
must not be inferred as an ordinary structural dimension chain:

- The `Holes` dimensioning type does **not** dimension bolt holes. Tekla directs users
  to **Integrated dimensions**, which has a dedicated **Bolt dimensions** tab.
- For main-part bolt groups, the internal-dimension choice is `None`, `Internal`, or
  `All`. `Internal` means distances between bolts; `All` also adds edge distances from
  the outermost bolts to the part edge.
- For secondary-part bolt groups, Tekla has four choices: `None`, `Necessary`,
  `Internal`, and `All`. This is a Tekla setting, but the plant policy in
  `steel-rules.md` still has to decide which one applies to this project.
- Position dimensions are separate from internal bolt dimensions. Tekla can position
  secondary parts by `None`, `By bolt`, `By part`, or `By both`, and can start running
  dimensions from the main part or working points.
- Check dimensions for extreme bolts are a separate rule: between extreme bolts can
  target `None`, `Main part`, or `Assembly`; a separate option controls extreme bolts
  to work points.
- Preferred dimension side and combining equal bolt dimensions are also explicit
  settings. Therefore a future implementation must not silently choose an axis,
  chain direction, or `3*60`-style grouping.

Tekla also exposes `BOLT_EDGE_DISTANCE` and `BOLT_EDGE_DISTANCE_MIN` as template
attributes. They prove that Tekla can calculate/report bolt edge distance, but they do
**not** prove that the value can be used directly to create a drawing dimension through
Open API. Likewise, `Measure > Bolt spacing` is a temporary model measurement, not a
drawing-dimension API. Both are useful reference behavior, not an implementation path.

The Open API contract we have confirmed is lower-level: `BoltGroup.BoltPositions` are
in the XY-plane of the bolt-group coordinate system and are relative to the
transformation plane in which the group was selected. The future bridge command must
therefore preserve the selected view coordinate system and return the raw positions,
group endpoints, related parts, and read state. It must not pretend that raw positions
already encode Tekla's dimensioning policy.

**Spike done, 2026-08-23 - answered by reflection against the installed 2025.0 DLLs in
the extensions folder, not by documentation alone.** Integrated Dimensioning's bolt rules
are **not** invokable as a ready engine from this bridge:

- `Tekla.Structures.ObjectDimensioningModel.dll` is a pure data model - `DimensionSet`,
  `DimensionPoint`, `RadiusDimension`, `AngleDimension`, `ObjectProjectionRule`. No
  method on any of these types reads live model geometry or computes anything; they are
  the shape of an *answer*, not a way to produce one.
- `Tekla.Structures.ObjectDimensioningPlugin.dll` exposes exactly one public surface:
  `IObjectDimensioningPlugin.CreateObjectDimensioningModel(int mainPartId,
  ObjectProjectionRule projectionRule, string ruleName) : ObjectDimensioningModel`, plus
  an `[ObjectDimensioningPluginAttribute("name")]` for registering an implementation.
  **This is an extension point Tekla calls into, not an engine this project can call
  out to.** A class implementing it would become a selectable named rule inside Tekla's
  own *Dimensioning rule properties* dialog for a person working in the Tekla UI - but
  the None/Internal/All, By-bolt/By-part, and extreme-bolt logic above would still have
  to be written by this project *inside that plugin*, using the raw
  `get_bolt_groups_in_view` data this roadmap already targets. It changes *where* the
  logic would run (inside a Tekla-invoked plugin vs. inside this bridge), not *whether*
  it has to be written, and it only fires when a person runs Integrated Dimensioning
  from the Tekla UI - this bridge cannot call it on demand either way.

Conclusion for phase 3: implement the None/Necessary/Internal/All, position-mode, and
extreme-bolt mapping explicitly on top of `get_bolt_groups_in_view`, as already planned.
Do not build against `ObjectDimensioningPlugin` expecting it to remove that work - it
does not, and it targets the Tekla UI, not this bridge's MCP flow.

## Bolt dimension logic (design only; not implemented)

This is the logic to freeze before writing the bridge command or creating a drawing
dimension. It separates four things that are easy to mix up:

1. **which bolt groups belong to the view**;
2. **what the bolt pattern says geometrically**;
3. **which of those facts the plant wants dimensioned**;
4. **where the resulting dimension chain is placed and verified**.

### 1. Selection gate: only bolt groups belonging to this view

The input is the depth-aware result of `get_bolt_groups_in_view`, not a raw
`view.GetObjects()` enumeration and not `get_structural_chain_positions`.

The command must:

1. start from the parts selected for the requested view and collect their bolt
   groups through `Part.GetBolts()`;
2. deduplicate groups by bolt-group model id;
3. preserve `PartToBeBoltedId`, `PartToBoltToId`, and `OtherPartIds` so the group can
   be classified as main-part, secondary-part, or connection-wide;
4. apply the view depth test to the bolt group itself or to a documented equivalent
   anchor/solid test; a group is not visible merely because one related part is
   visible;
5. return excluded and unresolved groups explicitly. An unreadable group never
   becomes an included group by default.

The first live acceptance case remains M.505 `EndView` 1709: the result must contain
the four bolts of the end-plate group and none of the other groups repeated by the
drawing database. A group that is connected to a selected part but lies outside the
view depth belongs in `outsideDepthModelIds`, not in `boltGroups`.

The selection gate is complete only when every candidate group has one of these
outcomes: `Included`, `OutsideDepth`, `Unread`, or `UnresolvedDepth`. A dimension
plan must stop for that group when the outcome is `Unread` or `UnresolvedDepth`.

### 2. Normalize the raw geometry before choosing dimensions

For every included group, keep the raw positions and derive a separate, immutable
planning view:

- coordinates remain in the requested drawing view coordinate system;
- each position keeps its bolt-group id and original index, so a dimension can be
  traced back to the model object;
- duplicate projected points are collapsed only within a named geometric tolerance;
  they are not silently removed because they look close on the sheet;
- positions are projected onto the view's X and Y axes for ordinary horizontal and
  vertical chains;
- for a skewed group, the planner chooses either the part direction or the bolt-group
  direction according to policy. If the chosen direction cannot be reconstructed
  from a non-zero, stable reference, the group is `DirectionUnresolved` and no
  automatic chain is created;
- the planner never uses a convex hull as a replacement for the bolt pattern. A hull
  can describe the outside of a group but cannot preserve rows, columns, spacing, or
  an asymmetric hole pattern.

`FirstPosition` and `SecondPosition` are raw Tekla inputs, not proof that JSON order
is the dimension-chain start. For a new chain, the planner records its axis and datum
explicitly. For an existing chain, start is verified by the connectivity rule in
`AGENTS.md`; ambiguous or branched chains are `unverified`.

### 3. Derive candidate facts, not dimensions, from the pattern

For each included bolt group and each chosen dimension axis, derive these candidate
facts independently:

| Candidate fact | Meaning | What it is not |
|---|---|---|
| `InternalSpacing` | distance between consecutive bolt centers on one row/column | a distance to the plate edge |
| `EdgeDistance` | distance from an outermost bolt center to the relevant part edge | distance from hole circumference or bolt solid bbox |
| `ExtremeSpan` | distance between the two extreme bolt centers | proof that the whole part is sized by the bolts |
| `PositionToReference` | bolt or bolt-group position from the main part/reference line/work point | an internal bolt spacing |
| `PatternOrientation` | evidence that the pattern distinguishes one orientation from its 180-degree twin | a guarantee that the plate outline is asymmetric |

For a rectangular or multi-row group:

1. build independent rows and columns using the chosen direction and tolerance;
2. sort positions along each row/column;
3. create internal-spacing candidates only between adjacent centers;
4. use the minimum and maximum center on an axis for extreme candidates;
5. compute edge-distance candidates against the selected part's actual projected
   edge/contour, never against the bolt-group bbox;
6. retain the relation to the part that supplies that edge. If the correct edge is
   not uniquely identifiable, the candidate is unresolved rather than guessed;
7. do not create diagonal chains between unrelated rows or columns.

For a circular, irregular, staggered, or skewed group, the same facts may exist but
the rectangular row/column algorithm is not sufficient. Such a group needs an
explicit direction and grouping decision, or it is reported as requiring review.
The planner must not flatten it into one arbitrary sorted list.

### 4. Apply the Tekla/plant policy to candidate facts

The policy must be represented as named inputs, not hidden defaults:

| Policy input | Meaning |
|---|---|
| `MainPartBoltInternal` | `None`, `Internal`, or `All` for groups dimensioned in the main part |
| `SecondaryPartBoltInternal` | `None`, `Necessary`, `Internal`, or `All` for groups dimensioned in a secondary part |
| `PositionSecondaryPart` | `None`, `ByBolt`, `ByPart`, or `ByBoth` |
| `PositionReference` | `None`, `MainPart`, or `WorkingPoints` |
| `ExtremeBoltSpan` | `None`, `MainPart`, or `Assembly` |
| `ExtremeBoltsToWorkPoints` | yes/no |
| `SkewedGroupDirection` | no dimensions, part direction, or bolt-group direction |
| `PreferredDimensionSide` | explicit view/side choice, never inferred from point order |
| `CombineBoltDimensions` | explicit grouping mode and minimum count |

The meanings are:

- `None` removes only the corresponding internal bolt dimensions. It does not
  silently remove separately enabled position or extreme-bolt checks.
- `Internal` keeps the distances between adjacent bolt centers and does not add
  edge distances unless another rule enables them.
- `All` keeps all admissible internal spacings and edge distances for that role.
- `Necessary` keeps only what is needed to locate or orient the connection when the
  part/pattern cannot be understood without it. It requires an explicit
  `recognizableDistance` from the plant rules; without that value the planner stops.
- `ByBolt` and `ByBoth` are location rules, not internal-spacing rules. They may
  produce a chain even when internal bolt dimensions are `None`.
- `ExtremeBoltSpan` is a check dimension and must not be confused with the chain of
  every internal spacing.

For the current steel skill, the unresolved plant choice is still the internal policy.
It must be recorded separately for main and secondary bolt groups if the drawing uses
both roles. Applying `Necessary` to an `All` answer is a data loss; applying any
internal bolt dimensions to `None` invents dimensions the plant did not request.

### 5. Special rule for orientation-critical patterns

The M.16 base-plate case is not solved by asking for `All` everywhere. The planner
must first ask whether the plate/profile outline is invariant under a 180-degree
rotation. If it is, and the bolt pattern is asymmetric, at least one dimension set
must expose the asymmetry:

- unequal edge distances on one or both axes;
- an offset from the main-part centerline or working point;
- a non-symmetric internal spacing;
- or an explicitly plant-required position dimension.

If the pattern is symmetric and the outline is symmetric, bolt dimensions cannot
invent an orientation that the model does not contain. The result is
`OrientationUnresolved`, not a made-up dimension.

This rule is a planning gate, not a promise to dimension every bolt. It answers the
fabrication question: can the plate be installed in the wrong 180-degree orientation
while all displayed dimensions still pass?

### 6. Build chains only after candidate selection

The planner creates separate chains for separate roles and axes:

- one chain for each retained row/column of internal spacing;
- edge-distance chains only when `All` or an explicit position rule requires them;
- position chains from the declared main-part or working-point datum;
- extreme-bolt checks as separate checks, not silently merged into internal chains.

Before writing, every candidate receives exactly one state:
`Kept`, `Removed`, or `Blocked`, with a reason. `Removed` means a policy decision;
`Blocked` means incomplete geometry, unresolved direction/edge, unresolved orientation,
or an unanswered plant setting. A chain must never become shorter merely because the
planner discarded points without recording why.

The chain start, side, row type, closure, and datum are recorded exactly like other
dimension chains. A bolt group does not get a special permission to infer start from
the order returned by `BoltPositions`.

### 7. Verification requirements

After creating a bolt dimension, read the drawing back and verify all of the following:

1. every `Kept` bolt center or edge candidate has a corresponding dimension point;
2. every `Removed` candidate is absent for its recorded reason;
3. no dimension point belongs to an `OutsideDepth`, `Unread`, or
   `UnresolvedDepth` group;
4. the displayed value equals the planned projected distance within the declared
   tolerance;
5. the chain is on the requested side and uses the declared direction/datum;
6. combined notation is used only when the declared grouping policy allows it;
7. on orientation-critical views, the final dimensions distinguish the required
   orientation or the result explicitly remains `OrientationUnresolved`.

The first live checks are:

- M.505 `EndView` 1709: four end-plate bolts, correct two-axis pattern, no groups
  from other views;
- M.16 `SectionView` 2152: the four- or multi-bolt base-plate pattern, orientation
  decision, and behavior when the section looks along the member;
- one skewed/staggered group, to prove that the planner does not accidentally treat
  it as an axis-aligned rectangle.

### 8. Implementation order

1. Freeze the policy object and candidate/result states in unit-testable code.
2. Expose `get_bolt_groups_in_view` with raw geometry, related parts, depth outcomes,
   and read errors.
3. Add a pure bolt-pattern planner with fixtures for rectangular, asymmetric,
   symmetric, skewed, duplicate, and incomplete groups.
4. Validate the planner against the three live cases above without writing dimensions.
5. Add dimension creation only after the planner output is stable.
6. Read back every created chain and compare it with the planner decision.

No phase may claim to match Tekla Integrated dimensions until the Open API spike proves
that Tekla's own rules are callable/readable, or the project mapping has been explicitly
tested against equivalent Tekla settings.

**One remaining thread, not yet tried:** `BOLT_EDGE_DISTANCE`/`BOLT_EDGE_DISTANCE_MIN`
are report *template attributes*, the same mechanism `TeklaDrawingPartGeometryApi.cs`
already uses for `PART_POS`/`MATERIAL`/`PROFILE` via `GetReportProperty(string, ref
value)`. If a bolt or its part answers `GetReportProperty("BOLT_EDGE_DISTANCE", ref
value)` the same way, Tekla's own computed edge distance would be readable with no
plugin and no geometry math of our own for that one value specifically - worth a quick
live check before writing an edge-distance calculation by hand, even though it would not
by itself cover spacing, grouping, or extreme-bolt selection.

Sources:
- [Dimensioning properties in drawings (Integrated dimensioning)](https://support.tekla.com/doc/tekla-structures/2026/dra_general_dimensioning_properties)
- [Dimensioning rule properties](https://support.tekla.com/doc/tekla-structures/2026/dra_dimensioning_rule_properties)
- [Add automatic view-level dimensions using Integrated dimensioning](https://support.tekla.com/doc/tekla-structures/2025/dra_setting_which_dimensions_to_create)
- [BOLT_EDGE_DISTANCE](https://support.tekla.com/doc/tekla-structures/2026/bolt_edge_distance)
- [BOLT_EDGE_DISTANCE_MIN](https://support.tekla.com/doc/tekla-structures/2026/bolt_edge_distance_min)
- [Measure objects — Measure bolt spacing](https://support.tekla.com/doc/tekla-structures/2026/mod_measuring_objects)
- [BoltGroup.BoltPositions](https://developer.tekla.com/doc/tekla-structures/2026/bolt-positions-property-70998)
