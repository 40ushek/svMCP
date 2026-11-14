# Bolt Chains Roadmap

Created 2026-11-13. Status: the first release (work order steps 1-5) is implemented
(commit `8c71946`) and placed live on M.502 by a cold Haiku run. Open: the M.81
sections run (rest of step 6), fresh-placement acceptance of the refined section-edge
rule (step 6a), and steps 7-10. The refinement is implemented: ends are confirmed
independently, square edges or facing corners are used, and every source bolt must
belong to the same outer contour before any end is admitted.

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
   their class and refusal reason. No chain is invented for them. Skewed is the class
   only: bolt axis neither square to the view nor in its plane. A `Plan` or `Side` group
   whose row is turned in the view plane (bolts along a slanted plate edge, M.156 P/1341),
   staggered or incomplete is not skewed: it gives no steps (`stepReason`), its edge
   chains along the view axes stay (agreed 2026-11-13; before, the whole group was
   refused as a "skewed bolt-group reference").
5. **Creating is a separate decision.** Whether a built chain becomes a dimension is
   decided afterwards, from the class and the settings. Building a chain does not mean
   placing it.

### First release decisions

- **Defaults:** `All` for main and secondary parts; dimension both `Plan` and `Side`.
  `Skewed` and `Unresolved` stay in the preview with a refusal reason and are not
  created. Position, check and combining options are off.
- **Class tolerance:** start with `ClassToleranceDegrees = 1`, configurable in the
  settings class. Confirm it against the live examples before release.
- **Group merging (agreed 2026-11-13, replaces "off in the first release"):** Tekla
  merges the groups of a part unless `XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY` is TRUE.
  The setting `BoltChainSettings.GroupMerge` (own) says which groups share one edge
  chain: `Separate`, `SameHole` (default: same hole diameter, hole type and slots) or
  `SameBoltSize`. Groups merge only on the same dimensioned parts, of the same class and
  measured axes; the chain key names every group (`bolt-part-10-groups-21-22-Bottom`).
  Only groups with every center admitted on the view merge or make a twin: a blocked
  group keeps its own refused chain (review 2026-11-13).
  Merging is decided per chain axis (agreed 2026-11-13, M.156 bottom view): along an
  axis groups share a chain when one stands wholly beyond the other along it, or when
  their bolts stand at the same coordinates on it. Groups overlapping along the chain
  with different spacings keep their own chains, so a chain across a beam never mixes
  rows at +-45 and +-50 into `30 - 5 - 90 - 5 - 30`. Twins need the same measured
  axes too: two `Side` groups in the same XY but with crossed bolt axes are both kept.
  A merged group keeps its own steps (`Internal`). No distance limit yet: on M.156 four
  groups along the beam flange gave four full-length rows; now one. A distance limit
  ("close together") comes when a drawing needs it.
- **Bolt and hole data (agreed 2026-11-13):** the bolt read keeps the hole tolerance,
  `HoleOnly` (Tekla's `Bolt` off, e.g. `+LOCH`), hole type and slots; the hole diameter
  is bolt size plus tolerance. Every chain of the `Bolt` row carries `holes`: standard,
  bolt size, hole diameter, hole only, type and slots of each group it measures. Data
  only: the diameter is not written into the dimension text, the bolt mark shows it.
- **The hole is the one in the part (agreed 2026-11-13):** Tekla's hole type and slots
  act only on the parts whose flag `Hole1..Hole5` is on (part to bolt to, part to be
  bolted, then the other parts), with a per-part override in `BoltHolesAttributes`; the
  other parts get a plain hole. The read keeps `PartHoles`; `SameHole` and `holes`
  use the hole in the dimensioned part. Found on M.156: group 10385171 has
  `SlottedHoleY = 30` on the group but round holes on the beam, so it stayed apart.
  Reading the flags is not yet checked live.
- **Repeats:** exclude bolt chains from the current segment-length-only
  `DimensionChainRepeats` comparison. Equal steps in different groups do not prove a
  repeat. Both eligible projections may be dimensioned; mirror suppression comes
  later, based on the identities of the source bolts.
- **Section edges (agreed 2026-11-13):** off sections and end views a part edge is
  confirmed. On them an end of an edge chain is confirmed only when it
  coincides, along the chain axis, with supports belonging to that same part in the
  source profile/location chains of this view (`Top`/`Bottom` for X, `Left`/`Right`
  for Y). Supports are taken before merging or formatting the answer; `chain` and
  `chainDetails` give the same eligibility decision. The points come from the same view context, so the
  part chains are computed first and the bolt chains after them; nothing is read
  again from Tekla. Placing order is unchanged: one batch per view. Each end is
  confirmed on its own (see "Ends of an edge chain"); an `All` chain with no confirmed
  end is not created and stays in the preview with its reason. Every bolt must stand
  inside one and the same outer contour of the part, checked on all original sources
  before any end is admitted, not on the representatives after projection merging;
  otherwise no chain on either axis.
- **Parts seen flat on a section (agreed 2026-11-13):** a part lying flat in the
  section plane is drawn whole, as on an ordinary view: its edges count without a
  section-chain support.
- **Ends of an edge chain (agreed 2026-11-13, M.501 fin plate):** each end is taken
  on its own. An edge met at the bolt's height counts only where the part's edge runs
  square to the chain there; a slanted edge gives no measure. Then the end is the
  corner of the part's edge facing the dimension line (on a fin plate with a slanted
  side: the corner of its top edge), as a detailer measures it. An end the view does
  not confirm is dropped: one end left gives `edge - bolts`; none, a refusal.
- **Facing corner only for a slanted edge (agreed 2026-11-13):** the edge search ends
  as found, ambiguous or invalid. Found and square gives the corner of that edge; found
  and slanted gives the facing corner. Ambiguous (a contour vertex at the bolts' height,
  a center on the edge) or invalid geometry gives no end. A plate with cut corners and
  bolts at the height where the cuts start is refused, not measured to the cut.
- **Bolts on the contour line (agreed 2026-11-13, provisional):** a center within the
  coordinate tolerance of the contour line is on the boundary, whichever side rounding
  put it. A `Side` group there is measured when all its bolts lie on one edge running
  along the chain: from one end of that edge through the bolts to the other. A `Plan`
  group there, or bolts not on one such edge, is refused. Not yet checked on a drawing.
- **One chain per set of holes (agreed 2026-11-13):** two groups of the same class
  whose projected centers all coincide on the same dimensioned parts (bolts and a
  `+LOCH` hole group in one end plate, say) are dimensioned once, by the first group;
  the other is refused with a reference to it.
- **No steps without a datum (agreed 2026-11-13):** under `All` steps between bolts
  never stand in for an edge chain, on any view: tied to no edge or axis of symmetry
  they locate nothing. Steps alone come only from the `Internal` setting. A datum on
  an axis of symmetry belongs to the centered-bolts step (work order 7).
- **Several groups on one part and side:** each group has its own `All` chain,
  using its own bolts and the confirmed part edges. Several chains may use the same
  part edge as a datum; do not remove a group's last edge datum because another
  group uses it. With both edges confirmed, each group gets `edge - bolts - edge`;
  with one confirmed edge, `edge - bolts`; with neither, no `All` chain.
- **A group on several parts:** a group joining the main part and secondary parts
  gets an edge chain on every secondary part; the main part takes it only when
  nothing else is bolted. Under `Internal` the steps of the group are offered once.
- **Line placement:** the line stands one paper gap beyond the nearer bound of its
  part, and beyond every other part it would cross or come within one gap of along its
  span (2026-11-13: the end plate P/1617 past the beam end on the M.156 bottom view; a
  part only touching an end of the span does not count).
  A part whose edge falls on the line itself is in the way too (review 2026-11-13).
  Open: a part starting just beyond the line (by less than a gap) still leaves the line
  close to its edge; the full rule would ask for a gap on the outer side as well.
  Both sides are tried (2026-11-14, M.175 front view): the line goes to the other side
  when, past the parts in the way there, it stands nearer the bolts. An end plate at the
  left end has its right bound nearer its bolts, but the whole beam stands beyond it: the
  line went past the right end of the beam, 4 m of extension lines across a shortened
  view, and the view no longer fitted the sheet. A side whose chain is refused is not
  compared; the steps of the group follow the side chosen for its first part.
- **A key marked `incomplete` refuses the batch, whatever its points (review
  2026-11-13).** A repeat keeps its `pointIds`, but its row may already be given to a
  bolt line; written anyway it lay on that line. Lines are kept clear of each other, of the `Local` and `Inclined` lines, and
  of the rows of the side chains on the same side (one row per chain to be placed;
  repeats are marked incomplete first and keep no row): a line within one gap of a taken line whose span
  overlaps moves to one gap beyond that line (2026-11-13; before, it moved by whole
  gaps from its own place, and bolts 5 mm off the outline lost a row).
- **Repeats keep no row (agreed 2026-11-13, replaces "open: the row of an overall in
  `repeats`").** A chain read with `repeats` marks each repeat `incomplete=true` (with
  `repeat` and the reason) before the bolt lines take their rows; the batch writes from
  that same answer. On the M.156 bottom view the bolt line stood beyond two empty rows
  kept for the repeated `Top-location` and `Top-overall`.
- **Bolts without an edge of their part go onto the location chain (trial, 2026-11-13).**
  A `Plan` bolt chain left without any edge of its part (M.156 front view: P/1341, edges
  slanted at the bolts, the facing edge a single tip between them) gives its bolts to the
  location chain of the same side when they lie inside its span: the two become one chain
  `<location key>-bolts` in the `Bolt` row, at the location chain's place, and the location
  chain is marked not to be placed. Not on sections. A first step toward Tekla's
  *Combine dimensions*; other choices (the nearest square edge, the main part end) open.
- **Open idea: bolt chains before part chains.** The user is weighing whether bolt
  lines should stand nearest the part, with the part chains beyond them. Not decided.
- **Open: mirror bolt chains.** On a bottom or back view the bolt chains repeat those
  of the top or front view; Haiku asks about it every run. A repeat by the same source
  bolts seen from the other side is the next candidate rule.
- **Open: reading a dimension on a mirrored, shortened view.** On the M.156 bottom view
  `get_drawing_dimensions` reads the right bolt chain at distance 398.3 and x 3880.9
  (compact: 1212.4), while it was written at 85.2 and stands at the beam end on the sheet.
- **Open: vertical chains of a long part.** A chain across a beam stands beside the
  nearer part end, up to 1.3 m from its bolts (M.156 top view). Tekla's help does not
  say where it puts that line; merging leaves one such chain per side.
- **Incomplete bolt read:** no bolt chain is created; the `Bolt` row carries
  `readIncomplete` with the reason and the unread parts, even when no group was read.

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

- The bolt axis (normal of the group's own XY plane) is read with the centers, in view
  coordinates, and kept in the view snapshot (`BoltGroupGeometry.Axis`). A case saved
  before has no axis: its groups are `Unresolved` and give no chain.
- Every group gets a class (`BoltGroupViewClassifier`): `Plan`, `Side`, `Skewed`,
  `Unresolved`. The `bolts` and `boltChains` answers show it.
- On sections, each end of an `All` chain is confirmed independently by same-part,
  same-axis source section supports, or by the face-on part rule. One confirmed end
  suffices; no confirmed end means refusal. A slanted crossing can use the facing
  edge's corner; all source bolts must first pass the single-contour check.
- `boltChains` (obsolete) builds chains for `Plan` and `Side` groups; the two old
  orientation refusals are gone; a turned, staggered or incomplete pattern refuses its
  steps only (`stepReason`).
- `BoltChainSettings`, `BoltChainCandidates` and `BoltChainRule` build and decide the
  chains of the `Bolt` row in the chain preview. A key from it is written by the batch
  like a `Local` key; a key marked not to create rejects the whole batch.
- The skill places the `Bolt` row in the standard run; it takes no part in `repeats`.
  It reports an incomplete bolt read from `readIncomplete`.
- No *Position* and no *Check* dimension is built.

Placed live on M.502, 2026-11-13, by a cold Haiku run (all dimensions deleted and
placed again; 19 dimensions on 3 views, 13 tool calls, about 45 s, no failures):

| View | Group | Placed |
|---|---|---|
| Front 1417 | 10126649 on part 10126565 | `35 - 90 - 35` |
| Front 1417 | 10129801 on part 10129088 | `180 - 60 - 40` and `50 - 50` |
| Bottom 1900 | 10126649 on part 10126565 | `35 - 90 - 35` (Top) and `30 - 230 - 30` (Right) |
| Bottom 1900 | 10129801 on part 10129088 | Refused: `axis ray aligns with a contour vertex` (`Side`; under `All` no steps off sections) |
| Section 2149 | 10126649 | Steps `230` (edge chain refused; run before the `WhollyInDepth` rule) |

The first run put bolt lines on the lines of neighbouring chains (`Local`, `Top`);
after the line placement rule the user checked the section and the bottom view: no
overlaps. On the bottom view one empty row stays between `Top-location` and the bolt
line, reserved for `Top-overall`, which is not placed as a repeat.

For M.501 Section C-C, the user reports the fin-plate chain `70 - 40` and the right
chain `41 - 90 - 35` remain on the sheet after deployment of the contour checks.
Those dimensions are from the previous run: this confirms their continued presence,
not a fresh placement/read-back acceptance of the current implementation.

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
side. The `Bolt` row builds one chain per part, group and side; the obsolete
explicit path still builds one per part and side over all related groups.

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
   Done 2026-11-13 by `GroupMerge` (same hole by default), without a distance limit yet.
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

Steps 1-5 done on 2026-11-13.

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
5. The `Bolt` row in the chain preview; the skill places marked chains. Implemented
   while the acceptance of batch writes in the Dimensions roadmap is still open: the
   standard run already writes part chains through that batch, bolts add more.
6. A cold Haiku run on M.502 and on the M.81 sections: result, time, tool calls.
   M.502 done 2026-11-13 (see above); M.81 postponed by the user.
6a. Section edges: implemented, fresh-placement acceptance remains open. Each end is
   checked independently against unmerged same-part, same-axis supports or the
   face-on part rule; one confirmed end suffices. Square crossings or facing corners
   supply the ends, and all original bolts must belong to one outer contour on both
   axes. Regression tests cover merged-away outside bolts, split contours, answer
   detail and foreign supports. Replaces `WhollyInDepth`; direct matching to a face
   seen edge-on was set aside.
7. The *Position* kind, then the *Check* kind, then centered bolts.
   Reference sheet: M.298 front view (riegel UPE200, 4720 long, two plates P/1631 bolted
   to it), dimensioned by hand (2026-11-14):
   - along the beam, one chain of the positions of every bolt group, the beam's own and
     the plates', from the beam end: 40 / 260 / 760 / 60 / 100 / 660 / 2800; the plates
     are located by their bolts (`Secondary part: By bolt`), with no chain of their own;
   - across the beam, a short chain at each group, beside it: 60 / 80 / 60 at the left
     end (line at x -86), 137.5 / 62.5 at x 979, 60 / 80 / 60 at x 1951 and at the right
     end (x 4803) (bolt internal `All`, per group);
   - the overall 4720;
   - the plates are located by their edges on the bottom view only (260 / 75 / 850 / 75 /
     3460), and dimensioned on the section (10 / 80, 55 / 80 / 54, 5 / 189 / 6).
   The hand-made sheet is kept (restored by the user after a test run); read it again
   rather than trusting these numbers if it changes.
   The current `Bolt` row gives instead an edge - bolts - edge chain per part and group
   (four chains along the beam, the plates measured from their own edges), the plate
   location chain and local chains beside, the cross chain of the middle group at the
   beam end and the two end groups merged into one cross chain on one side.
   Scope agreed with the user (2026-11-14): this step is bolts only. Combining bolt
   positions with part dimensions (Tekla's *Combine dimensions*) is a separate step with
   its own setting, step 10.

   **Decision (user, 2026-11-14): a setting, on by default, that can be switched back.**
   Two settings in `BoltChainSettings`, named and valued as on Tekla's Position dimensions
   tab: `PositionBoltsTo` (None / MainPart / WorkingPoints) and `SecondaryPart` (None /
   ByBolt / ByPart / ByBoth). Default `MainPart` + `ByBolt`, as the plant draws (M.298);
   `None` + `ByPart` gives back today's chains unchanged, so a sheet where the new chain
   is worse is fixed by the setting, not by code.
   The Tekla names are supported in part, and the answer says so: in Tekla `Main part`
   measures from the main part's reference line, here from its physical end (the plant's
   practice; the reference line is a later value); `ByBolt` / `ByPart` choose the holes
   or the edges of the secondary part, here `ByBolt` only adds its bolts to the position
   chain and its location by edges stays. `WorkingPoints` refuses with a reason until it
   is implemented.
   The setting must reach the sheet, not only the code: today the context builds
   `new BoltChainSettings()`. The chosen values travel with the chain read into the
   context and from it to the batch (the batch writes from the same answer, as the
   repeats do), and the bolt-chain cache key includes them.

   Plan:
   1. A new chain kind *position* in the `Bolt` row, one per view axis along the main
      part: from the datum through every projected bolt column, in order, ending at the
      last column (the detailer's chain ends at 4680, the overall gives 4720). The datum
      is a point on the confirmed physical end contour of the main part. `StartPoint`
      and `EndPoint`, carried into the view, determine which end is the start; they are
      axis points, not proof of where the physical end lies. On a mirrored view or a
      member drawn the other way the chain starts at the same end of the member, not
      at the smallest view coordinate. If the direction or the physical end cannot be
      confirmed, the position chain refuses and today's chains stay. Measuring from
      the reference line start is a later option. Columns within the
      coordinate tolerance are one point; the point of a column is its bolt nearest the
      line, as for the other chains. Key `bolt-position-<axis>`.
   2. Which bolts: `Plan` groups of the main part, and with `SecondaryPart = ByBolt` or
      `ByBoth` the groups of the secondary parts bolted to it. Bolted to it is read from
      the model links of the group (its parts include the main part), never from its
      projection standing over the main part. A group joining secondary parts only stays
      out for now. `Side` and `Skewed` groups stay out, as now.
   3. What it replaces, with `PositionBoltsTo = MainPart`: along the main part, the
      edge - bolts - edge chains of the groups the position chain covers (the main
      part's, and with `ByBolt` the secondary parts'), marked as given by it. Only once
      the position chain is finally created: after the settings, the view frame and the
      bolt read, as for the bolts over bolts; a refused position chain leaves every chain
      of today in place. Across the main part nothing changes: one chain per group,
      beside it. Carrying edgeless bolts into the location chain stops only for the bolts
      the created position chain holds; on unsupported views and when it is refused, the
      carry works as today.
   4. The side: the side of the main part the bolts stand nearer to on the view (the
      detailer's top on M.298); the row and the clearing as for the other bolt lines.
   5. Repeats: compared like a location chain (sameView, mirror), as the carried chain
      is today.
   6. Not in this step: dropping the secondary part's location by edges with `ByBolt`
      (the part location chain is the steel rule's, and the detailer keeps it on the
      bottom view of M.298); working points; an upright member (the riegels M.40 and
      M.294 run up the sheet: the axis comes from the main part, not from the sheet).
   7. Tests, the first one the most important: the position chain refused (outside the
      view frame, incomplete bolt read) leaves today's chains and the carry as they were;
      the saved M.298 front view gives 40 / 260 / 760 / 60 / 100 / 660 / 2800 on top;
      `None` + `ByPart` gives today's keys and segments; the member drawn the other way
      starts the chain at the same physical end; when the axis start differs from the
      physical end, the datum is on the confirmed end contour; an unconfirmed direction
      or end refuses the position chain and preserves today's chains;
      a plate whose bolts have no edge joins
      the position chain; a group joining secondary parts only does not; `WorkingPoints`
      refuses with a reason; the settings chosen reach the batch.
      Live: M.298 (restore the hand-made sheet afterwards), M.175, M.119.
   Decided (review, 2026-11-14): the chain does not close to the far end when the overall
   is given on another view; it is on the sheet. When the overall is missing because it
   was refused or its write failed, the answer reports the uncovered span instead of
   changing what the chain holds.
8. Decoding the plant's `.adcd` file, so the settings can be filled from it.
9. Skewed-to-part groups, `Necessary`, combined steps.
10. Merging with part chains by Tekla's *Combine dimensions* modes: in the Chain
   Composition roadmap, not here.

## Open questions

- **Bolts over bolts are dimensioned twice (user, 2026-11-14, M.298).** A plate P/1631
  bolted to the beam has its own bolt group (11229963) standing on the same points as a
  group of the beam (11221297). Each part gets its edge - bolts - edge chain: 60 / 80 / 60
  from the beam flanges and 54 / 80 / 55 from the plate edges, beside them the plate's
  location 6 / 189 / 5. The plate has its own drawing. To do:
  - groups of different parts standing on the same projected points are dimensioned
    once on a view, from the main part; the secondary part's chain is marked as given by
    the main part's (reason names the group). Done in source 2026-11-14, not deployed:
    a secondary part chain whose every bolt stands on a bolt of a main-part chain on the
    same axis is linked to it when the chains are built (`OverMainPartChain`); in
    `ViewDimensionContext.BoltChains`, after the settings, the view frame and the bolt
    read, it is refused only when that main-part chain is created, "the same bolts are
    dimensioned from the main part by <key>", and rows go to the chains placed after that
    (review: decided at build time, a main chain refused later by `MainPartInternal=None`
    or by the view frame left no chain at all). On the saved M.298 front view both chains
    of the plate at x 300 are refused;
    the plate at x 1220 keeps its chains, the beam has no group under its bolts;
  - a setting to dimension bolts to the main part, as Tekla's *Position bolts to: Main
    part* with *Secondary part: By bolt* (Position dimensions tab), so the bolts of a
    secondary part are measured from the main part whether or not the main part has a
    group under them. `SecondaryPartInternal = None` alone only drops the plate's chains;
    it gives no chain from the main part. This is the *Position* kind, step 7.

- **Merging far groups does harm (user, 2026-11-14, M.298).** Equal groups at the two
  ends of a beam (60 / 80 / 60 each) are merged across it into one chain on one side.
  A dimension standing far from its bolts reads as if those bolts had no dimension, and
  its extension lines run through the whole member. The detailer gives each group its
  own cross chain beside it. To do: merge across only groups standing close along the
  member, with a threshold like Tekla's *Combine dimensions: Min distance* ("if the
  distance between two details is less than the Min distance, the dimensions are
  combined"; `XS_SINGLE_COMBINE_MIN_DISTANCE` for single-part views in assembly
  drawings, a decimal such as `200.0`, units not stated, blank by default;
  `XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY` turns merging off). Proposed default: one
  dimension-line offset on paper (8 mm, 80 model mm at 1:10). This settles the
  `GroupMergeDistance` question below for the cross direction.
  Done in source 2026-11-14, not deployed: `BoltChainSettings.CombineMinDistance` (model
  mm; null is one line offset); groups share a chain only when they stand no farther
  apart across it. And a group standing in the middle of its part (the part reaching more
  than one gap past it on both sides) gets its line beside it, one gap past its outer
  bolt, with the edge points moved across to it along edges running straight past it;
  with no paper gap given, the line stays past the part. Beside the group the line clears
  the parts in the way exactly as past the part (review 2026-11-14: a plate standing next
  to the group took the line), before the two sides are compared. Unlike past the part, a
  part reaching over the group is in the way too (second review: a plate x 300..600,
  y 60..110 over a column at x 440 was crossed at x 360): inside the part there is room
  past it. Replayed on the saved M.298
  front view: one cross chain per group, each beside its group (980 against the
  detailer's 979; 1800 against 1951, the other side of the group); one cross chain more
  than the detailer's, the plate bolts through the beam at x 300.
- **The extension line starts at the nearest bolt (user, 2026-11-14, M.298).** When
  bolts of a merged chain share a position across the member, the chain keeps the first
  one, here the far group's bolt: the extension lines start 2800 mm away, cross the
  near group, and the near bolts get no line of their own. The point of a chain must be
  the bolt nearest the dimension line, as Tekla's
  `XS_DO_NOT_EXTEND_DIMENSION_LINES_THROUGH_ALL_HOLES` does (idea 6 above); this holds
  for one group with several rows too, not only for merged groups.

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
- The row reserved for an overall that is not placed as a repeat leaves an empty row
  before the bolt line. Reserve only the chains that will be placed?
- Bolt lines are kept clear by rows only; text collisions and lines of dimensions
  already on the sheet are not checked.

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
