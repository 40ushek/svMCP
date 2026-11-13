# Bolt Chains Roadmap

Created 2026-11-13. Status: the first release (work order steps 1-5) is implemented
on 2026-11-13 and read live on M.502; nothing was placed on a drawing yet. Open: the
cold Haiku run (step 6), a live write of the `Bolt` row, and steps 7-10.

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

- The bolt axis (normal of the group's own XY plane) is read with the centers, in view
  coordinates, and kept in the view snapshot (`BoltGroupGeometry.Axis`). A case saved
  before has no axis: its groups are `Unresolved` and give no chain.
- Every group gets a class (`BoltGroupViewClassifier`): `Plan`, `Side`, `Skewed`,
  `Unresolved`. The `bolts` and `boltChains` answers show it.
- `boltChains` (obsolete) builds chains for `Plan` and `Side` groups; the two old
  orientation refusals are gone, the skewed-reference refusal stays.
- `BoltChainSettings`, `BoltChainCandidates` and `BoltChainRule` build and decide the
  chains of the `Bolt` row in the chain preview. A key from it is written by the batch
  like a `Local` key; a key marked not to create rejects the whole batch.
- The skill places the `Bolt` row in the standard run; it takes no part in `repeats`.
- No *Position* and no *Check* dimension is built.

Live read on M.502, 2026-11-13 (read only, nothing placed):

| View | Group | Answer |
|---|---|---|
| Bottom 1900 | 10126649, four bolts on part 10126565 | `Plan`. Created: `35 - 90 - 35` (Top) and `30 - 230 - 30` (Right) |
| Bottom 1900 | 10129801, two bolts on part 10129088 | `Side`. Edge chain refused: `axis ray aligns with a contour vertex` (under `All` no steps are offered) |
| Section 2149 | 10126649 | `Side`. Edge chain refused (edge not confirmed); steps `230` created instead |

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
7. The *Position* kind, then the *Check* kind, then centered bolts.
8. Decoding the plant's `.adcd` file, so the settings can be filled from it.
9. Skewed-to-part groups, `Necessary`, combined steps.
10. Merging with part chains by Tekla's *Combine dimensions* modes: in the Chain
   Composition roadmap, not here.

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
