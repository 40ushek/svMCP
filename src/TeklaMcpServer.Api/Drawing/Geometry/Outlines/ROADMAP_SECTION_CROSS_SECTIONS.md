# Section and end view geometry

Two separate bugs turned out to share one cause: nothing in this codebase asks
a `SectionView` or `EndView` *where along the member's length* it is. One bug
now has an explicitly broad-phase implementation: positive-volume overlap of
view-aligned solid and restriction boxes includes the part, while a boundary
touch stays unresolved. The other was investigated in depth and deliberately
not fixed - kept as a record, not a plan.

## Bug 1 (implemented conservatively): the part list itself was wrong

`get_all_parts_geometry_in_view` / `get_drawing_view_context` on a section or
end view return every part associated with the drawing, not the ones actually
shown there.

Confirmed on `M.505`, view `1709` (an `EndView`): the call returned **9**
parts - the whole assembly. The user, looking at the actual sheet, counted
**2**.

Root cause: `Tekla.Structures.Drawing.Part.Hideable.IsHidden` is a manual
"hidden" flag. It says nothing about whether a part's geometry falls inside
the view's own depth window - Tekla's drawing engine clips that visually, at
render time, without ever marking the part `IsHidden`.

**Three call sites read "which parts are in this view," and two of them share
the candidate helper.** `DrawingViewParts.CandidateModelIds(View)` reads
`view.GetObjects()` and the `IsHidden` flag only - no `Solid`, no bbox
(`TeklaMcpServer.Api/Drawing/Geometry/DrawingViewParts.cs`) - and is called
directly by `TeklaDrawingViewContactApi.GetContactGraph` (behind
`get_contact_candidate_points`) and `TeklaDrawingPartRoleApi.GetRolesInView`
(behind the role/structural-outline path). `get_drawing_parts` is not a
view reader: it deliberately returns all model objects referenced by the
active drawing and accepts no `viewId`.
`TeklaDrawingPartGeometryApi.GetAllPartsGeometryInView` (behind
`get_all_parts_geometry_in_view` and, through `DrawingViewContextBuilder`,
`get_drawing_view_context`) does not call the shared helper at all - it has
its own, separate inline loop over `view.GetObjects()`/`IsHidden`, reading
full per-part geometry as it goes.

An earlier version of this note proposed fixing only the fourth one, on the
reasoning that it is the one already holding a bbox to filter. That leaves
the other three - the ones `get_contact_candidate_points` and
`get_drawing_parts` actually run on - answering from the same 9-of-9 list as
before. Contacts on a section view is the specific thing the user asked for
after "срезы солидов вообще не нужны"; a fix that leaves contacts unfixed
does not close that. The depth check has to sit where all three can reach it,
which means routing all three readers through one depth-aware entry point
rather than only patching one caller.

### Open API limitation and an API-native experiment (forum-confirmed)

This is not an inference from our implementation. In the Tekla Structures
Open API forum, Carlos Herrero (Open API Specialist, Product Development)
states that `View.GetObjects()` returns the drawing-view database, not just
objects rendered in the view, and that **Open API has no direct method that
returns only visible parts**. The same answer was repeated for
`GetModelObjects()`/`GetObjects()` in a second thread:

- [View.GetObjects() (26–27 February 2025)](https://forum.tekla.com/topic/38026-viewgetobjects/)
- [Get drawingparts in a view (29–30 April 2025)](https://forum.tekla.com/topic/38573-get-drawingparts-in-a-view/)

The specialist's suggested workaround is to call `GetRelatedObjects()` for
every drawing `Part` returned by `view.GetObjects()`; only visible parts are
said to have related drawing objects. It is not documented as a complete
renderer contract, and the original reporter said it was not accurate enough
for their case. Therefore do **not** replace the depth result with it or call
it an exact visibility API without a live test.

**Live result — not a selection gate.** `DrawingViewRelatedObjectsProbe` in
`TeklaMcpServer.Host` selects every drawing `Part` before reading its
relations, then logs model id, `PART_POS`, `Hideable.IsHidden`, and the count
and types from `GetRelatedObjects()`. It was run on 22 August 2026 with the
header `View: id=1709, type=EndView`; this is the `M.505` view in question.
`View.GetObjects()` returned these nine non-hidden parts:

| Model id | Part position | `GetRelatedObjects()` |
|---:|---|---|
| 9532104 | `P/5005` — visible main beam | empty |
| 10097493 | `P/5007` | empty |
| 10097601 | `P/5007` | empty |
| 10100321 | `P/5008` | empty |
| 10100607 | `P/5007` | empty |
| 10100714 | `P/5007` | empty |
| 10104380 | `P/5010` — visible end plate | `Mark#1795` |
| 10128715 | `P/5027` | empty |
| 10128826 | `P/5027` | empty |

The two sheet-visible parts are `P/5005` and `P/5010`; `P/5005` has no
related object. Thus a non-empty relation is not necessary for a visible
part and filtering on it would silently drop the main beam. Keep
`GetRelatedObjects()` available only as a positive diagnostic signal; it is
not an API-native visibility gate and must not replace the depth/solid
selection.

### The fix, confirmed live

**Correction to an earlier note in this file:** `View.RestrictionBox`
(`AABB`, `MinPoint`/`MaxPoint`) was first read from its documented summary -
"the dashed frame around the contents of a drawing view... resize it to show
just a specific part of the view contents" - and set aside as a 2D display
crop. That summary describes what a person uses it for; it does not describe
what the property actually holds. Read live, after `view.Select()`, on
`M.505` view `1709`:

```
restrictionBoxMin: [-130.66, -170.66, -400]
restrictionBoxMax: [ 130.66,  170.66,   50]
```

X/Y match the view's 2D extent (`PartsBounds` from `get_drawing_view_context`:
-130..130, -170..170) almost exactly. **Z is the depth window** - and Z is
exactly the axis nothing in this codebase has ever asked about for these two
view types.

Tested against all nine parts' own bboxes, already read in this same view's
local frame by the existing (unmodified) part-geometry call:

| Part | Z span (view-local) | Inside `[-400, 50]` |
|---|---|---|
| Träger (main, `9532104`) | 0 .. 5194.6 | yes |
| Stirnplatte `P/5010` (base, `10104380`) | -20 .. 0 | yes |
| Stirnplatte `P/5008` (far, `10100321`) | 5194.6 .. 5204.6 | no |
| Rippe x6 (three pairs) | 204 .. 5062 | no |

Two of nine, matching the user's count on the sheet. **This is a broad-phase
test, not proof Tekla actually draws the part.** Two AABBs overlapping does
not mean the real geometry does: a raked or inclined part's bbox can extend
past where its solid actually reaches, producing a false include; a long,
shallow part could in principle have a bbox that misses the box while a
sliver of its true geometry does not, producing a false exclude - not
observed here, but not ruled out either, since every part on `M.505` is
axis-aligned. This roadmap does not get to claim "exactly the visible parts"
as a general property of the bbox test from one axis-aligned assembly. It is
a confirmed, working candidate on the case measured; a raked or curved part
needs checking before it is trusted beyond that.

### Where it goes

The implemented entries on `DrawingViewParts` have deliberately different
names so a cheap candidate enumeration cannot be mistaken for a visible-part
answer:

```csharp
CandidateModelIds(View view)                    // IsHidden only; not visibility
GetDepthFilteredParts(Model model, View view)   // depth-aware answer
    -> { ModelIds, OutsideDepthModelIds, BoundaryAmbiguous, Unread }
```

`GetDepthFilteredParts` starts from the same `view.GetObjects()`/`IsHidden` pass.
For a base-projected view it returns exactly that list with an empty
`Unread`, at the cost of one unused `Model` parameter - no plane switch, no
solid read, same cost as today. Only for `SectionView`/`EndView`
(`LooksAlongMemberLength(view.ViewType)` - see Bug 2 for where that check
already lives) does it go further:

- **Same plane discipline as every existing solid read, not a new one.**
  Guard with `DrawingViewPlane.IsModelPlane(view.ViewCoordinateSystem)`
  first, exactly as `TeklaDrawingPartSolidGeometryApi
  .GetPartSolidGeometryInView` and `TeklaDrawingPartGeometryApi
  .GetAllPartsGeometryInView` already do, and on the same reason: reading
  right after that check without switching planes would return a bbox in
  whatever plane happened to be current - the model's own, another view's,
  anything - compared against a `RestrictionBox` that is always view-local.
  Two API calls read `Point`/`Solid` data relative to whatever plane is
  *currently set* (a piece of global state), so **both must be read under
  the same, deliberately-set plane**: set
  `workPlaneHandler.SetCurrentTransformationPlane(new
  TransformationPlane(view.ViewCoordinateSystem))` before `view.Select()`.
  `ViewDepthWindow` then stores the one resulting `RestrictionBox` snapshot;
  the candidate filter and cache key receive that same snapshot.
- **Use a view-aligned broad phase deliberately.** With the model work plane
  set to `view.ViewCoordinateSystem`, `solid.MinimumPoint`/`MaximumPoint` are
  the solid's AABB in that same frame. Treating the two AABBs as OBBs with the
  view axes would be mathematically identical; the code keeps the simpler
  `DepthBox` comparison. A strictly disjoint box is `OutsideDepth`; a boundary
  touch is `BoundaryAmbiguous`; positive-volume overlap is included. This is
  deliberately allowed to produce a false include for a skewed, curved, or
  heavily cut part. It must never be described as Tekla's exact renderer
  visibility result.
- **For each candidate, a read failure is not a silent answer either way.**
  `SelectModelObject`/`GetSolid()`/the bbox read can each throw. Treating a
  failure as "exclude" would produce a quiet false negative - a part
  missing from the list with no sign why; treating it as "include" would
  produce a false positive dressed up as success. Neither is acceptable,
  which is exactly the shape `ViewContactsResult.Unread` and
  `PartRoleReadResult.Unread` already exist to name (`UnreadPart(modelId,
  reason)`, established elsewhere in this codebase). The depth-aware result
  separates confirmed `ModelIds`, definite `OutsideDepthModelIds`, and
  `BoundaryAmbiguous`/`Unread` candidates; the latter two reuse
  `UnreadPart` so existing incomplete-result paths keep their details.

`TeklaDrawingViewContactApi.GetContactGraph` and `TeklaDrawingPartRoleApi
.GetRolesInView` call `GetDepthFilteredParts` - both already hold the `Model`
reference this needs. Contacts expose requested ids that are hidden/not drawn,
definitely outside depth, or unresolved depth separately; roles append the
unresolved cases to their existing `Unread` list. `TeklaDrawingPartGeometryApi
.GetAllPartsGeometryInView` switches its own inline `view.GetObjects()`/
`IsHidden` loop to source candidate IDs from the depth-aware result too, and turns
each of its `Unread` entries into the same `PartInView { Success = false,
ModelId, Error = reason }` shape it already produces for a failed geometry
read - one filter, reused, not two that could drift apart, and no new
result shape for this consumer either.

**Scope, stated precisely rather than implied.** This closes
`get_contact_candidate_points`, `get_all_parts_geometry_in_view`, and
`get_drawing_view_context` - every current consumer that asks "which parts
does this view draw." It does **not**
close `TeklaDrawingPartGeometryApi.GetPartGeometryInView(viewId, modelId)` -
the single-part lookup takes any caller-supplied model ID and never checks
view membership at all, by design: a caller that already names a part is
presumed to have a reason, and enforcing view membership there would change
what that method is for.

It does **not** touch `get_structural_chain_positions`/`get_assembly_outline`'s
own fail-closed gate - correct, because Bug 2's flatten-without-clip problem
is still real and that gate still guards it. A correct part list does not
make `ProjectedOutlineBuilder.BuildPart` clip a beam's full-length solid to
one cross-section; it only makes sure the *count* of parts offered to it is
right. The two bugs share a cause, not a fix.

**Caching.** `ViewDepthWindow.Read` captures `RestrictionBox` once under the
view transformation plane before `TryGetAll`. That immutable snapshot is
passed to `TryGetAll`, `GetDepthFilteredParts`, and `StoreAll`; `BuildKey`
only serializes the six snapshot coordinates and never calls `view.Select()`.
A depth-only change therefore changes the key, while a cache miss no longer
performs a second or third IPC read of the same box.

### Debug command

`debug_view_restriction_box` answered the live question above and has been
removed (`TeklaBridge/Commands/DrawingCommandHandler.Geometry.cs` and its
entry in the outer switch in `DrawingCommandHandler.cs`). It shipped in the
production command dispatcher for one commit with no test and no MCP
wrapper - a throwaway probe belongs in `TeklaMcpServer.Host`, this
project's own local/debug utility, not in the bridge's command switch. Note
for next time rather than a live problem now.

## Bug 2 (investigated, not fixed): outline geometry has no concept of depth

`get_structural_chain_positions` on a section/end view does not just list the
wrong parts - the *shape* it would compute for a correctly-listed part is
also wrong, because `ProjectedOutlineBuilder.BuildPart` (`SolidContacts.Core`)
flattens a face's loop by keeping only X/Y and dropping Z outright, with no
clipping. Correct for an elevation or plan, where nothing meaningful varies
along the short depth axis. Wrong here, where the depth axis is the member's
length - flattening a beam's full uncut solid along it collapses every
cross-section into the same silhouette regardless of where the section is.

Confirmed on `M.505`: three distinct cuts - A-A (base plate, four bolts), B-B
(far plate, none), C-C (a rib pair) - returned byte-identical coordinates
from `get_structural_chain_positions`, to the decimal, before this was
gated off.

`View.ViewTypes.EndView` is bucketed under `ViewSemanticKind.BaseProjected`
by `ViewSemanticClassifier` - correct for the layout question that
classifier answers (does fit-to-sheet treat it as a base view), wrong for
this one. The two questions must not share a check; see
`TeklaDrawingAssemblyOutlineApi.LooksAlongMemberLength`, added for this
reason and reused by Bug 1's design above.

### Shipped: fail-closed, and it stays

`TeklaDrawingAssemblyOutlineApi.GetAssemblyOutline` refuses
(`Unavailable`, not a silent wrong answer) when
`LooksAlongMemberLength(view.ViewType)` is true. Covered by
`TeklaDrawingAssemblyOutlineApiTests.OnlyViewsLookingDownTheMemberAreRefused`
(a real unit test - `View.ViewTypes` is a plain enum, needs no live Tekla).

### Decided: not worth fixing

A real fix was designed and reasoned all the way through against the
installed API (2025.0), then deliberately not built, because the need
behind it turned out not to exist. What a section view actually needs
dimensioned - on `M.505`, four bolts in a plate at A-A, nothing at B-B/C-C
beyond marks already carrying every length - comes from contacts and
per-part geometry (`get_contact_candidate_points`, `get_part_geometry_in_view`,
correctly scoped once Bug 1's design is built), not from a true cut
silhouette. Nothing in this skill's dimensioning loop has needed the
flattened 2D shape of a cross-section itself; that need was assumed, not
observed.

The designed fix, kept as a record in case the need ever does show up (a
genuine reason to draw a cut's own contour - a profile's fillet on a detail
view, say):

- `RestrictionBox` gives the volume, but clipping a 3D solid against a
  6-sided box is a real Boolean operation (cap the faces the box cuts
  through, close the new boundary) that `SolidContacts.Core` - a 2D,
  Clipper2-based library - does not have and should not grow just for this.
- `Tekla.Structures.Model.Solid.IntersectAllFaces(Point, Point, Point)`
  (confirmed by reflection to be the only overload in the installed 2025.0
  API) is **not** that operation and was never going to be, and this file
  said otherwise before this correction: it intersects a solid with one
  infinite plane, defined by the three points, and hands back the resulting
  polygons - "the first list is the outer polygon, the rest are holes." A
  plane cut is not a box clip; it has no notion of the box's other five
  sides and cannot stand in for one. It is the right primitive for a
  different, narrower need - a cut's own contour, unbounded in depth - not
  for reproducing what `RestrictionBox` actually bounds.
  `GetAllIntersectionPoints` is the faster sibling that skips arranging the
  result into polygons, which is the one thing a contour would need, so it
  is not the one to call if this is ever picked back up.
- `TeklaDrawingPartSolidGeometryApi.GetPartSolidGeometryInView` already sets
  the transformation plane to `view.ViewCoordinateSystem` before reading a
  part's solid. Calling `IntersectAllFaces` on that same solid while the
  plane is still current needs only three simple local points -
  `(0,0,0)`, `(1,0,0)`, `(0,1,0)` - no point computed from
  `ViewCoordinateSystem.Origin`/`AxisX`/`AxisY`, no result projected back
  afterward: solid and plane already share a frame.
- Ruled out along the way, each against the installed API rather than
  documentation alone (2026 docs describe members this installation does not
  have): `View.ViewDepthUp` exists, but on `Tekla.Structures.Model.UI.View`
  (the 3D viewport camera), not `Drawing.View`. `CreateSectionView`'s
  `depthUp`/`depthDown` are write-only creation parameters with no readable
  sibling anywhere - checked `View`, `SectionMark`, and their internal
  structs (`dotGrView_t`, `dotGrSectionMarkAttributes_t`) field by field.

Do not build this without a concrete need in hand.

## Status

Bug 1 is implemented conservatively and verified live on the original
axis-aligned case: `M.505`, view `1709`, still returns only `P/5010` and
`P/5005`; its unrestricted contact search names the same pair.

The implemented guard is intentionally not an exact solid-vs-box Boolean:
it includes positive-volume overlap between the view-local AABB of a solid and
`RestrictionBox`, rejects a strictly disjoint box, and reports a boundary
touch as incomplete. This deliberately avoids losing a long member that a
section cuts between its end vertices. It can include a skewed, curved, or
heavily cut part whose view-local AABB overlaps the window while its real
solid does not; that is an accepted, documented trade-off for now. A live
raked or inclined example remains required before relying on it more broadly.

Bug 2's fail-closed gate stays as the permanent answer, not an interim one.
