# Bolt Chains Roadmap

Created 2026-11-13. Status: a roadmap only - decisions agreed with the user, Tekla's
original rules as the basis, and a work order. No code is written for it yet, and none
is to be written until the user says so.

This file owns one question: **how bolt chains are chosen automatically**, by rules
close to Tekla's own. The neighbours own the rest:

| Roadmap | Owns |
|---|---|
| [Dimensions](ROADMAP_DIMENSIONS.md#bolt-chains-and-composition-current-work-order-2026-10-02) | Delivery status of what exists: bolt preview and explicit creation |
| [Bolt Geometry](../Geometry/Bolts/ROADMAP_BOLT_GEOMETRY.md) | Bolt geometry facts, the selection gate by view depth, earlier research of Tekla's rules and DLLs |
| [Chain Composition](ROADMAP_CHAIN_COMPOSITION.md) | Merging chains of different kinds, bolts with parts included |
| this file | Classes of bolt groups, the settings, the decision to create, the `Bolt` row |

## Decisions (2026-11-13)

1. **Goal.** Chains for bolts are chosen by rules like the original rules of Tekla's
   Integrated dimensioning.
2. **Separate chains.** A bolt chain is not mixed with part chains: its own row in the
   preview, its own keys, its own dimension line. A part edge may be the datum of a
   bolt chain (edge - bolts - edge); that is not mixing.
3. **Merging comes later.** Bolt and part chains may be merged afterwards, through
   chain composition. So every point of a bolt chain keeps where it came from: bolt
   group, bolt index, part.
4. **Every captured group is considered.** A readable axis gives its class: **in
   plan**, **from the side**, or **at an angle**; a missing axis gives `Unresolved`.
   Chains are built only for supported `Plan` and `Side` patterns. Other groups keep
   their class and refusal reason, including a skewed group reference direction that
   still requires an explicit direction policy. No chain is invented for them.
5. **Creating is a separate decision.** Whether a built chain becomes a dimension is
   decided afterwards, from the class and the settings. Building a chain does not mean
   placing it.

### First release decisions

- **Defaults:** `All` for main and secondary parts; dimension both `Plan` and `Side`.
  `Skewed` and `Unresolved` stay in the preview with a refusal reason and are not
  created. Position, check and combining options are off.
- **Class tolerance:** start with `ClassToleranceDegrees = 1`, configurable in the
  settings class. Confirm it against the live examples before release.
- **Group merging:** off in the first release. Every group has its own chain;
  `GroupMergeDistance` and clustering are deferred until a concrete drawing needs
  them. No threshold is inferred from the existing part chains.
- **Repeats:** exclude bolt chains from the current segment-length-only
  `DimensionChainRepeats` comparison. Equal steps in different groups do not prove a
  repeat. Both eligible projections may be dimensioned; mirror suppression comes
  later, based on the identities of the source bolts.
- **Unverified edges:** a part edge counts as confirmed on a section only when the
  section contour of that part is read; a full-solid projection alone does not
  confirm it. Until such a read exists, no section edge is confirmed. An `All` chain
  without a confirmed edge is not created. Its steps-only replacement (as for
  `Internal`) is offered only when the group passes the class, settings, depth and
  pattern checks and has at least two distinct admissible projected positions.
  Otherwise no replacement is created. The edge chain stays in the preview with
  its refusal reason. Steps alone do not locate the group on the part.
- **Several groups on one part and side:** each group has its own `All` chain,
  using its own bolts and the confirmed part edges. Several chains may use the same
  part edge as a datum; do not remove a group's last edge datum because another
  group uses it. With both edges confirmed, each group gets `edge - bolts - edge`;
  with one confirmed edge, it keeps that edge. Without confirmed edges, the
  conditional steps-only fallback above applies. Confirm this on a live drawing.

## Tekla's original rules

Tekla dimensions bolts in two dimensioning types. *Integrated dimensions* has the
dedicated bolt rules described below, and this roadmap follows them. *Filter* dimensions
bolts too: since Tekla 2024 each bolt of a group can be dimensioned at a chosen point
(left, center, right or both). The *Holes* type does not dimension bolt holes. The
Integrated rules sit on three tabs of *Dimensioning properties* and in a few advanced
options. The help pages read are of versions 2021-2026; names may
differ a little in 2025.

### The kinds of bolt dimension Tekla tells apart

| Kind | What it measures |
|---|---|
| Internal | Steps between the bolts of one group |
| Edge distance | From the outermost bolt to the edge of the part |
| Position | Where the bolts stand on the assembly: from the reference line of the main part, or from work points |
| Check (extreme bolts) | Between the outermost bolts of the main part or of the whole assembly; optionally from them to the work points |

These four kinds are the vocabulary of this roadmap. Every chain belongs to one kind,
or to a combination Tekla names (see *Combine dimensions*).

### Bolt dimensions tab

| Tekla option | Values | Meaning |
|---|---|---|
| Main part bolt internal dimensions | None / Internal / All | `Internal`: steps between bolts. `All`: steps and edge distances |
| - Skewed bolt group | No dimensions / In part direction / In bolt group direction | Which way a skewed group is measured |
| Secondary part bolt internal dimensions | None / Necessary / Internal / All | `Necessary`: only what is needed to tell the part's orientation; see *Recognizable distance* |
| - Skewed bolt group | In part direction / No dimensions / In bolt group direction | Same, for a secondary part |
| Extreme bolts | None / Main part / Assembly | A check dimension between the outermost bolts |
| Extreme bolts to work points | Yes / No | A check dimension from the outermost bolts to the work points; needs *Extreme bolts* on |
| Preferred dim side | front view / side view | In which view bolt dimensions are placed |
| Combine bolt dimensions | single / `3*60` / `3*60=180`, with a minimum number to combine | How equal steps are written |

### Position dimensions tab

| Tekla option | Values | Meaning |
|---|---|---|
| Position bolts to | None / Main part / Working points (/ Both) | Where bolt position dimensions start |
| Main part bolt position | Off (default) / On | Check dimensions to the bolt holes of the main part |
| Secondary part | None / By bolt / By part / By both | A secondary part is located by its bolt holes, by its edges, or by both |
| Centered bolt | Internal / Position | Bolts standing centrally on a part: measure their spread, or measure them to the centre lines of the main part. Overrides the secondary internal setting for such bolts |
| Position from | work point | Start of running dimensions for a skewed clip angle or shear plate bolted to a neighbour part |
| Dimension direction | Neighbor part | Align those dimensions with the neighbour part |

### General tab

| Tekla option | Values | Meaning |
|---|---|---|
| Recognizable distance | a length | Used with `Necessary`. When a part's asymmetry is smaller than this, the asymmetry is shown by a dimension - otherwise the part could be fitted turned round |
| Combine dimensions | No / 1 / 2 / 3 / 4 / 5 / 4.5 | Which kinds share one dimension line; table below |

*Combine dimensions* in Tekla's words:

| Value | On one line | Kept apart |
|---|---|---|
| No | nothing | everything |
| 1 | part position + part internal; bolt internal + edge distances | bolt position from bolt internal |
| 2 | part position + part internal + bolt internal; bolt internal + bolt position | edge distances |
| 3 | bolt internal + bolt position | - |
| 4 | bolt group position + part position; bolt internal + edge distances | part internal and bolt internal |
| 5 | internal + bolt group position, when there are several bolt groups | - |
| 4.5 | 5 on the main part, 4 on secondary parts | - |

Bolt groups standing close together are treated as one group for dimensions and marks.

### Advanced options (category *Dimensioning - bolts* and single-part views)

| Option | Default | Meaning |
|---|---|---|
| `XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY` | FALSE | TRUE stops Tekla from combining the dimensions of bolt groups |
| `XS_DO_NOT_EXTEND_DIMENSION_LINES_THROUGH_ALL_HOLES` | TRUE | The extension line stops at the nearest hole of the group instead of running through all of them |
| `XS_BOLT_POSITION_TO_MIN_AND_MAX_POINT` | off | Adds minimum and maximum position dimensions to bolts |
| `XS_DIMENSION_SKEWED_BOLTS_IN_PART_PLANE_IN_SINGLE_DRAWINGS` | TRUE | Bolts perpendicular to the part plane are dimensioned in single-part drawings |
| `XS_SINGLE_SCREW_INTERNAL` | - | Internal bolt dimensions in single-part views of an assembly drawing: 1 shows, 0 hides |
| `XS_SINGLE_CENTERED_SCREW` | - | Centered bolts in those views: 0 spread, 5 to the main part centre lines, 6 override of the secondary internal setting |
| `XS_POSITION_DIMENSIONS_FOR_HOLES_IN_SINGLE_SECONDARY_PARTS_IN_ASSEMBLY_DRAWING` | - | Position dimensions for holes of single secondary parts; not read in detail |

### What Tekla does not tell

- How it decides that a bolt is seen on a view; the help describes which dimensions are
  made, not where bolts are visible.
- The exact test behind `Necessary`.
- The engine itself is native code with no entry from the Open API; see the Bolt
  Geometry roadmap.

The plant's own choice of all these options exists as a file in the model folder
(`+Bemassungseigenschaften ZB Schrauben.adcd`). Its numbers are not decoded yet; see the
2026-11-13 findings in the Bolt Geometry roadmap.

## Where the code is today

- The view dimension context reads bolt groups with every bolt center and tests each
  center against the view depth.
- `boltChains` proposes steps between bolts, edge distances and one chain per part
  (edge - bolts - edge), only for a rectangular group seen in plan and standing along
  the view axes. In Tekla's words: *Internal* and *Edge distance*, as with `All`.
- A group seen from the side is refused, not classified.
- No *Position* and no *Check* dimension is built.
- A proposal reaches the drawing only when its name is passed explicitly. The standard
  run of the dimension skill places no bolt chain.

Live read on M.502, 2026-11-13 (read only, nothing placed):

| View | Group | Answer |
|---|---|---|
| Bottom 1900 | 10126649, four bolts on part 10126565 | In plan. Part chains `35 - 90 - 35` and `30 - 230 - 30` |
| Bottom 1900 | 10129801, two bolts on part 10129088 | Refused: `bolt-group reference direction has no stable XY projection` |
| Section 2149 | 10126649 | Refused: `bolt centers span view depth; a face-on planar pattern is not established` |

## Target

### 1. Classes of bolt groups

| Class | Meaning | Chain |
|---|---|---|
| `Plan` | The bolt axis points at the viewer; the pattern is seen in full | Steps, edge distances, the part chain |
| `Side` | The bolt axis lies in the view plane | Built from the projected positions. Bolts standing one behind another give one point; the step along the view depth cannot be measured on this view |
| `Skewed` | Neither | None yet; the group is listed with its class |

The class is this project's own addition: Tekla's help has no such notion. It is not
Tekla's **Preferred dim side**. That option prefers a view - front or side - for bolt
dimensions; the class says how one group is turned to the view it is on, and a front
view can well show a group from the side. So there are two separate settings: which
projections are dimensioned (this project's own) and which view is preferred (Tekla's).

#### How the class is found: two coordinate systems

The class comes straight from coordinate systems; nothing is rebuilt from bolt centers.

1. **View normal** - the direction toward the viewer: the cross product of the view's
   X and Y axes (`View.DisplayCoordinateSystem`).
2. **Bolt axis** - the direction the bolts run along: the cross product of the X and Y
   axes of the group's own coordinate system (`BoltGroup.GetCoordinateSystem()`).
   Tekla defines bolt positions in the XY plane of that system, so its normal is the
   bolt axis.
3. **One number** - the dot product of the two unit vectors, taken without its sign.

Let `theta = acos(clamp(abs(dot), 0, 1))`, in degrees, for the two unit vectors.
One tolerance `t = ClassToleranceDegrees` applies symmetrically to both limits:

| Angle | Meaning | Class |
|---|---|---|
| `theta <= t` | the bolt axis points along the view normal, in either direction | `Plan` |
| `90 - theta <= t` | the bolt axis lies in the view plane | `Side` |
| otherwise | neither | `Skewed` |

Both boundaries are inclusive. Require finite `0 <= t < 45` so the classes do not
overlap; missing, non-finite or zero-length axes give `Unresolved` before this test.

Both vectors must be in the same coordinates. Bolts are already read in view
coordinates here; then the number is the absolute depth component of the unit bolt
axis. On a live drawing verify both the coordinate frame and that the computed
normal follows the actual bolts, using known face-on, side-on and tilted groups.

This works for any group, a single row and a pair of bolts included. The bolt group
data kept today (`BoltGroupGeometry`) has no such axis: orientation is judged from the
spread of the centers in depth and from `FirstPosition`/`SecondPosition`. A single row
seen from the side but lying in the view plane passes both tests and looks like `Plan`.
So the group's axis has to be read and kept in the view snapshot first; it is one more
read per group, made where the centers are read.

Source of the method: `Mpd.Quick/Similar/DrawingBoltDirectionPostFilter.cs` in the
MpdEdit repository, where the same number is used to find bolts turned the same way as
a chosen one. Its tolerance (0.05) compares two bolts with each other; the limits
between the classes here are this project's own choice.

A group that could not be read, or whose axis is missing, keeps its refusal: a
class is given only to what was read. How far from "along" or "across" a group may be
and still count as `Plan` or `Side` is a setting, not a constant in the rule.

`Skewed` here means skewed to the view. Tekla's *Skewed bolt group* means skewed to the
part, inside a view that sees the group in plan; that is a separate later step.

### 2. Chain kinds, in Tekla's vocabulary

| Kind | Exists today | Planned |
|---|---|---|
| Internal | yes, `Plan` groups | `Side` groups too |
| Edge distance | yes, `Plan` groups | `Side` groups too |
| Position | no | Two datums, kept apart: the reference line of the main part (Tekla's `Position bolts to: Main part`) and the end of the member (the plant's practice, a rule of this project, not a Tekla setting). Work points later |
| Check (extreme bolts) | no | Main part, then assembly |

### 3. Settings: Tekla's options one to one, and this project's own

One settings object for bolt chains. A setting taken from Tekla carries the name and
the values of its option, so a plant's saved choice can be carried over without
translation. A setting Tekla does not have is marked "own" and is never presented as a
Tekla option.

| Setting | Tekla option | Increment |
|---|---|---|
| Main part internal: None / Internal / All | Main part bolt internal dimensions | first |
| Secondary part internal: None / Necessary / Internal / All | Secondary part bolt internal dimensions | first; `Necessary` later, with *Recognizable distance* |
| Projections to dimension: `Plan` / `Side` / both | own | first |
| Class tolerance, degrees | own | first |
| Group merge distance and clustering policy | own | later; merging is off in the first release |
| Preferred view: front / side | Preferred dim side | later |
| Position bolts to: None / Main part / Working points | Position bolts to; `Main part` is the reference line | second |
| Position datum: reference line / member end | own; member end is the plant's practice | second |
| Main part bolt position: Off / On | Main part bolt position | second |
| Secondary part: None / By bolt / By part / By both | Secondary part | second |
| Extreme bolts: None / Main part / Assembly | Extreme bolts | third |
| Extreme bolts to work points | same | third |
| Centered bolt: Internal / Position | Centered bolt | third |
| Skewed bolt group direction | Skewed bolt group (main, secondary) | later |
| Combine bolt dimensions and minimum number | Combine bolt dimensions | later |
| Combine dimensions: No / 1 / 2 / 3 / 4 / 5 / 4.5 | Combine dimensions | with chain composition |

### 4. The decision to create

Input: the built chains, each with its kind, its class, its part role (main or
secondary), and the settings. Output for every chain: create or not, and why. The
decision is made in code, so a weak model only places what is marked.

### 5. What is offered, and under which key

For `All`, the unit is a part, a group or a cluster of groups, and a placement side
(top, bottom, left, right). One part can therefore have several chains on the same
side. The code today builds one chain per part and side over all related groups;
splitting those groups is part of this increment, not existing behaviour.

| Setting value | Chains offered | Never in them |
|---|---|---|
| `All` | Per part, group/cluster and side: its bolts and confirmed part edges; different groups may share an edge datum (see "First release decisions" for missing edges) | Bolts from another cluster |
| `Internal` | Per group, axis and row: the steps between its bolts | Part edges; a span between two groups |
| `None` | Nothing for that part role | - |

Two groups on one part have separate chains in the first release, with no invented
dimension between them. Later, with `All`, they may share a part chain when this
project's configured merge policy puts them in the same cluster. That future policy
is inspired by Tekla's handling of close groups, not a verified reproduction of its
grouping algorithm. With `Internal` groups never share a chain.

Keys in the new `Bolt` row distinguish the chain kind and every unit above:

| Chain | Key |
|---|---|
| `All`: edge - bolts - edge | `bolt-part-<partId>-groups-<groupIds>-<side>` |
| `Internal`: steps of one row | `bolt-<groupId>-<axis>-<row>` |
| Separate edge distance | `bolt-<groupId>-<axis>-<row>-part-<partId>-edge-min` or `-edge-max` |

`groupIds` is the complete, distinct list of model group IDs in the cluster, sorted
numerically and joined with `+`; a single group uses its ID alone. It is not an
enumeration index or an ID of the first group read. For part 100, two separate groups
21 and 22 on the top side have keys `bolt-part-100-groups-21-Top` and
`bolt-part-100-groups-22-Top`; merged, they have `bolt-part-100-groups-21+22-Top`.
The same members produce the same key regardless of read order. Every original group
and bolt index remains attached to the chain's points after merging.

For `Internal`, `row` is numbered after projection and coordinate-tolerance
clustering, not from the original rows in the group's plane. For an X chain, number
the distinct projected Y clusters from 0 upward; for a Y chain, use projected X.
Rows that coincide in a `Side` projection become one projected row. Merge duplicate
projected positions while preserving all source bolt indices; sorting the projected
coordinates makes the row numbers independent of the order in which bolts are read.

The explicit path keeps its existing `bolt-part-<partId>-<side>` names and behaviour
until its separately agreed removal. Those names are unique only for its current
one-chain-per-part-and-side output; they must not be reused for split part chains in
the new row. Keys are resolved within their view context as today.

### 6. Preview and creation

- A `Bolt` row in the chain preview, next to `Local` and `Inclined`, with the keys above.
- A chain that is not to be created stays in the answer with its reason; it is not hidden.
- The standard run places the chains marked for creation through the existing batch
  writer. No new write path.

## Ideas taken from Tekla's help and from detailing practice

1. **Name the chain kinds as Tekla does** - internal, edge distance, position, check.
   Tekla's settings then apply to them directly, and so do its seven *Combine
   dimensions* modes when merging comes.
2. **Merging as a short list of named modes, not free rules.** Tekla's *Combine
   dimensions* (No, 1-5, 4.5) is a ready list of which kinds may share a line. Chain
   composition can adopt it instead of inventing compatibility rules.
3. **`Necessary` as an asymmetry test.** A secondary part gets bolt dimensions only when
   it could be fitted turned round: its asymmetry is smaller than the recognizable
   distance (10 mm in the plant's file, if `recogn_dist` is that value).
4. **Centered bolts are their own case.** Bolts standing symmetrically about the centre
   line of the part are measured as a spread (the detailer's *gage*), not from an edge.
5. **Close groups count as one.** Bolt groups standing close together on one part are
   dimensioned as one group; `XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY` turns that off.
   The part chain of today (edge - all bolts - edge) does this; the first release
   keeps groups apart instead, and merging returns later by a set distance.
6. **Extension lines stop at the nearest hole** of a group
   (`XS_DO_NOT_EXTEND_DIMENSION_LINES_THROUGH_ALL_HOLES`): which bolt of a row carries
   the dimension point matters for the look of the sheet.
7. **Minimum and maximum position dimensions** (`XS_BOLT_POSITION_TO_MIN_AND_MAX_POINT`)
   are a cheap first form of the *Position* kind: the group is located by its two
   outermost bolts.
8. **Since Tekla 2024 the *Filter* dimensioning type** can dimension each bolt of a
   group with a chosen point: left, center, right or both. A possible later setting.

Detailing practice found on the web adds terms rather than rules: *gage* is the
distance between bolt lines across the member, *pitch* the step along it, *edge
distance* is measured from the hole centre to the edge. No source for the convention of
running dimensions from the member end was found; that stays with the plant's rules.

## Work order

No code until the user asks for it. When they do:

1. The bolt axis: read the group's coordinate system where the centers are read, and
   keep the axis in the view snapshot. Check on known face-on, side-on and tilted
   groups that it comes in view coordinates and follows the actual bolt direction.
   Recapture the saved bolt cases as described below.
2. Classes from the bolt axis and the view normal: a pure, tested step; the class shown
   in the `boltChains` answer. The two refusals of today become `Side` or `Skewed`, and
   a single row seen from the side stops passing for `Plan`.
3. Chains for `Side` groups.
4. The settings object with Tekla's names, and the decision step over the kinds that
   exist (internal, edge distance).
5. The `Bolt` row in the chain preview; the skill places marked chains. Only after the
   open acceptance of batch writes in the Dimensions roadmap is closed.
6. A cold Haiku run on M.502 and on the M.81 sections: result, time, tool calls.
7. The *Position* kind, then the *Check* kind, then centered bolts.
8. Decoding the plant's `.adcd` file, so the settings can be filled from it.
9. Skewed-to-part groups, `Necessary`, combined steps.
10. Merging with part chains by Tekla's *Combine dimensions* modes: in the Chain
   Composition roadmap, not here.

## Implementation plan (temporary)

**Delete this section once it is implemented.** What stays after that: the decisions,
Tekla's rules and the ideas above; the delivered state goes to the Dimensions roadmap
and the Dimensions README.

The plan follows what the code already does for `Local` and `Inclined` chains: a rule
with its settings and input, a row in the chain preview, creation by key through the
existing batch. No new write path, no new MCP tool and no new tool parameter.

Decided by the user (2026-11-13):

- **The explicit way stays for now, marked obsolete.** The `boltChains` question and the
  `boltProposal` batch entries keep working beside the new `Bolt` row, but from slice C
  on they are obsolete: `[Obsolete]` on their code entry points, the word "obsolete" in
  the tool description, the README and the skill, each pointing to the `Bolt` row. They
  are removed later, by a separate decision.
- **A settings class from the start, no constants.** Every number and every choice for
  bolts lives in `BoltChainSettings`, each with a default; the rule takes the settings
  as input and holds no constants of its own, as `InclinedPartDimensionSettings` does
  for inclined chains. A file and a window fill the same class later.

### The path of the data

| # | Step | Class | Change |
|---|---|---|---|
| 1 | Read from Tekla | `TeklaDrawingBoltGeometryApi` (exists) | Inside the scope where `BoltPositions` are read in view coordinates, also read `boltGroup.GetCoordinateSystem()` and keep the unit normal of its X and Y axes |
| 2 | Group data | `BoltGroupGeometry` (exists) | New field `Axis`: three numbers in view coordinates; empty when it could not be read |
| 3 | View snapshot | `ViewBoltGeometrySnapshot` (exists) | Carry `axis` in the frozen answer and in `Restore`, so saved view cases replay offline. A case saved before this has no axis: its groups are `Unresolved` |
| 4 | Class | `BoltGroupViewClass`, `BoltGroupViewClassifier` (new, `Geometry/Bolts`) | Enum `Plan`, `Side`, `Skewed`, `Unresolved`; one pure function from the axis and a tolerance in degrees |
| 5 | Chains | `BoltDimensionChainPreview`, `BoltPartChainPreview`, `BoltEdgeDistancePreview` (exist) | Replace the two orientation refusals with the class check. Keep the refusal `skewed bolt-group reference requires an explicit direction policy` and other unsupported-pattern checks. Supported `Side` groups use projected centers and merged coinciding points; rows follow the key contract above. Every proposal gains `viewClass` |
| 6 | Settings | `BoltChainSettings` (new, `Dimensions/Settings`) | From Tekla, with its names and values: `MainPartInternal`, `SecondaryPartInternal` (`None`, `Necessary`, `Internal`, `All`). This project's own: `DimensionedProjections`, `ClassToleranceDegrees`. Defaults follow "First release decisions"; group merging and `GroupMergeDistance` come later |
| 7 | Decision | `BoltChainRule : IDimensionRule`, `BoltChainInput` (new, `Dimensions/Rules`) | Input: the built chains with kind, class, part and part role. Output: every chain, marked to create or not, with the reason. Run through `DimensionRuleSet` like `LocalPartDimensionRule` |
| 8 | Preview row | `ViewDimensionContext` (exists) | New `BoltChains()` beside `LocalChains()` and `InclinedChains()`; row `Bolt`; keys as in "What is offered, and under which key". A chain not to be created comes with `incomplete=true` and its reason. The view-frame check applies, as for `Local` |
| 9 | Write | `ViewDimensionContextProvider` batch (exists) | Resolve a `Bolt` row key and enforce its refusal before any batch writes. `boltProposal` entries keep working and are marked obsolete |

If any selected `Bolt` row key has `incomplete=true`, reject the whole batch before
the first write, including a batch mixing bolt and part chains. Return the key and
its refusal reason. An explicit distance does not override this check; having two
or more points does not make a refused chain writable.

The part role for step 7: main when the part is the main part of the view, secondary
otherwise. The view context already knows its main part.

**Saved cases and test fixtures:** missing axes intentionally give `Unresolved`;
do not fall back to the old depth-spread heuristic to preserve old chain output.
Keep old cases to test that refusal, and recapture the M.502 and M.81 bolt examples
with axes for chain replay. Update synthetic fixtures that expect chains with their
explicit, known axes; do not infer an axis from a single row or two bolt centers.
Old cases remain readable, but their former bolt-chain output is not guaranteed.

In step 8, exclude the `Bolt` row from `DimensionChainRepeats`: its current comparison
uses segment lengths, not source bolt identities. This applies to every chain kind
in the row, including a part chain with edge endpoints.

### Slices, each one deployable

| Slice | Steps | What changes for a user |
|---|---|---|
| A | 1-4 | The `bolts` and `boltChains` answers show the class of every group. Nothing else changes |
| B | 5 | `boltChains` offers chains for `Side` groups instead of refusing them. Still by explicit choice only |
| C | 6-9 and the skill | The `Bolt` row appears; the standard run places bolt chains. Known cost: bolt chains are left out of repeat suppression, so a bottom or back view repeats the bolt chains of the top or front view until suppression by source bolts is added |

**Slice C waits for the batch acceptance.** The Dimensions roadmap keeps an open gate
on batch writes: statuses that disagree with the drawing, partial success, a repeated
batch, merged chains
([next gate](ROADMAP_DIMENSIONS.md#bolt-chains-and-composition-current-work-order-2026-10-02),
"finish live acceptance of batch writes"). The standard run already writes part chains
through that batch, so the risk is not new, but bolts add chains to every view. Slices
A and B write nothing by themselves and do not wait.

Deploy after each slice: the bridge and its two DLLs, as usual. The MCP server only
when a tool description changes (slice C).

### Tests

- `BoltGroupViewClassifierTests` (new): axis along the view normal, in the view plane,
  in between, missing; both inclusive tolerance boundaries, reversed axis direction,
  invalid axes and invalid tolerances.
- `BoltChainRuleTests` (new): each value of the two internal settings; a class switched
  off; main and secondary part.
- `ViewBoltGeometrySnapshotTests`: the axis survives freeze and restore; an old case
  without it remains readable and gives `Unresolved` with no bolt chains. Recaptured
  cases with axes replay their expected chains.
- `BoltDimensionChainPreviewTests`, `BoltPartChainPreviewTests`: a `Side` group gets a
  chain; a single row seen from the side is no longer `Plan`; unsupported skewed
  reference directions remain refused. Collapsed projected rows preserve all source
  indices and keep the same keys when the source order is reversed.
- First-release groups and keys: groups on one part and side stay separate, have
  distinct keys and contain no bolts from each other, even when close together.
  With three groups at X=20/40, 100/120 and 200/220 and confirmed part edges, the
  middle group retains an edge datum as well. Sharing an edge between chains is
  allowed. Reversing group read order does not change their keys. `Internal` never
  adds a span between groups. Test merged source IDs and the merge boundary only
  when cluster merging is implemented later.
- Section edges: an `All` chain on a section without a confirmed edge is refused.
  Its steps-only replacement is created only with at least two distinct admissible
  projected positions. A projection collapsed to one position creates no chain;
  class, settings, depth and pattern refusals cannot be bypassed by the fallback.
- Defaults and refusals: `All` for both part roles, `Plan` and `Side` enabled, the
  configurable 1-degree class boundary, and no automatic edge chain from an
  unverified section contour. Equal segment lengths in different bolt chains must
  not cause repeat suppression.
- `DimensionChainPreviewTests`: the `Bolt` row in the preview and its key written by
  the batch, after the pattern of `PreviewAddsALocalRowAndTheBatchWritesItsChain`.
  A refused `Bolt` key with two or more points rejects the entire batch before any
  writes, including mixed part/bolt batches and requests with an explicit distance.

Live expectations on M.502: bottom view 1900 - group 10126649 `Plan`, group 10129801
`Side`; section 2149 - group 10126649 `Side`, its edge chain refused (edge not
confirmed). Create its steps-only replacement only if the admissibility checks
pass and at least two distinct projected positions remain; otherwise retain the
refusal without creating a chain. Then the cold Haiku run.

### What changes in the skill

Update the canonical `.agents/skills/dimension-drawings/SKILL.md`.
The files under `src/.claude/skills/dimension-drawings/` and
`src/.codex/skills/dimension-drawings/` are adapters pointing to it; keep the
instructions in the canonical file rather than duplicating them in the adapters:

- The steps of the standard run stay as they are. Step 5 already says "every chain with
  `incomplete=false` whose key is not in `repeats`", and that covers the `Bolt` row.
- One new paragraph beside "Local chains" and "Inclined chains": the row `Bolt`, its
  keys, "reference it by key"; a bolt chain with `incomplete=true` carries the reason
  (class switched off, setting `None`, edge not confirmed). In the `Bolt` row
  `incomplete=true` means "the rule decided not to create it", not missing points:
  do not repair, extend or place such a chain by hand.
- In "Do not, in a standard run" add: do not query `boltChains` and do not build bolt
  chains by hand. Say that `boltChains` and `boltProposal` are obsolete and kept only
  for placing one chosen bolt chain.

In `references/steel-rules.md`:

- Replace the statements that bolt dimensions cannot be planned at all (the "Bolts"
  item and the two later mentions) with the delivered state.
- Add a short "Bolts" section: the settings, their defaults and the three classes.

In the description of `get_view_dimension_context`: name the `Bolt` row next to the
other rows, and mark the `boltChains` question obsolete.

Check the result the usual way: a cold Haiku run with only the task and the skill path.

## Open questions

- Future group merging: `GroupMergeDistance` is not part of the first release;
  neither its value nor its metric is chosen. When a concrete case needs it, agree whether it
  is measured in 3D or on the view projection, its units (model or paper millimetres),
  and whether it is between nearest bolt centres, group centres or group bounds.
  Also agree the boundary comparison and whether A close to B and B close to C merges
  all three when A is far from C. Settle these before implementing cluster merging;
  do not silently borrow a threshold or clustering rule from existing part chains.
- Future preferred-view selection: the first release permits both eligible projections.
  "Projections to dimension" decides by projection; Tekla's *Preferred dim side* decides
  by view. How the two work together is to be settled when preferred-view selection is added.
- A bolt hidden behind another part. The class does not answer it; see the visibility
  probe in the Bolt Geometry roadmap.
- Improving section contours remains open. Until an edge is confirmed on the section,
  its chain is refused for automatic creation, as specified for the first release.

## Out of scope for now

Mixing with part chains, circular and irregular groups, occlusion.

## Sources

- [Dimensioning properties - Bolt dimensions tab](https://help.trimble.com/doc/tekla-structures/2022/dra_bolt_dimensioning_properties)
- [Dimensioning properties - Position dimensions tab](https://help.trimble.com/doc/tekla-structures/2021/dra_position_dimension_properties_for_singlepart_assembly_and_castunit_drawings)
- [Add automatic view-level dimensions using Integrated dimensioning type](https://help.trimble.com/doc/tekla-structures/2025/dra_setting_which_dimensions_to_create)
- [Example: Combine dimensions](https://help.trimble.com/doc/tekla-structures/2021/dra_examples_of_combining_dimensions)
- [Example: Bolt dimensioning](https://help.trimble.com/doc/tekla-structures/2021/dra_example_of_bolt_dimensions)
- [Add minimum and maximum position dimensions to bolts](https://help.trimble.com/doc/tekla-structures/2025/dra_creating_minimum_and_maximum_position_dimensions_for_bolts)
- [XS_DO_NOT_EXTEND_DIMENSION_LINES_THROUGH_ALL_HOLES](https://help.trimble.com/doc/tekla-structures/2026/xs_do_not_extend_dimension_lines_through_all_holes)
- [XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY](https://support.tekla.com/doc/tekla-structures/2026/xs_dimension_all_bolt_groups_separately)
- [XS_DIMENSION_SKEWED_BOLTS_IN_PART_PLANE_IN_SINGLE_DRAWINGS](https://help.trimble.com/doc/tekla-structures/2026/xs_dimension_skewed_bolts_in_part_plane_in_single_drawings)
- [XS_SINGLE_CENTERED_SCREW](https://help.trimble.com/doc/tekla-structures/2025/xs_single_centered_screw)
- [XS_SINGLE_SCREW_INTERNAL](https://support.tekla.com/doc/tekla-structures/2024/xs_single_screw_internal)
- [Improvements in drawing dimensions (2024)](https://support.tekla.com/doc/tekla-structures/2024/rel_dra_improvements_in_drawing_dimensions)
- [Workable flange gages, Modern Steel Construction](https://www.aisc.org/globalassets/modern-steel/archives/2010/05/2010v05_workable_flange_gages.pdf)
