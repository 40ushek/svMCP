# Part degrees of freedom

## The question

For each part visible in a view: is its position on the sheet already settled by
what touches it, or can it still move? And for the assembly as a whole: does the
freedom run out on its own, or does it need numbers?

Not a new question — it is what a sketch constraint solver answers when it says
"under-constrained, 2 DOF remaining". A part in the view plane has three degrees
of freedom: X, Y and rotation. Contacts with other parts remove some, its own
size carries determinacy across it, and a dimension removes whatever is left.

## What this is, and what it is not

It is a **read-only diagnostic**: which parts a drawing leaves able to move. No
command answers that today, and it is worth answering on its own.

It is **not a way to decide dimensions**, and the evidence below says so plainly.
The tempting reading — freedom left means a dimension is owed, freedom gone means
a dimension is redundant — is contradicted by the first drawing it was tried on.
On EWA.5 nearly every stud counts as fully constrained and the plant dimensions
them regardless. Any proposal to drive selection from this count, or to replace
the part-size and containment rules with it, needs data that does not yet exist.

The kinship with mechanism and redundancy is real but stays an observation. Note
too where the analogy stops: in structural analysis redundancy is ordinary and
often wanted, while on a drawing an extra number is one more thing to misread.
Same bookkeeping, opposite sign.

## Measured so far

**EWA.5, a timber stud panel.** Counting contacts per part, the freedom went to
zero on nearly every part, including studs the plant does dimension. Not an error
in the count: a stud is seated on the bottom plate, capped by the top one and
butted by noggins each side, and the frame is contacted throughout, so one
dimension would fix the whole panel. Correct and useless — nobody derives a
position by tracing twenty joints. Where the contacts close, the numbers on the
sheet are there to be checked against, not to position anything.

**M.53, a steel girder.** The same count was right on all seven parts: four had
one free direction each and each was dimensioned in exactly that direction, while
the three constrained on both axes carried no dimension of their own.

```
part      contacts   free   dimensioned
10069168  X          Y      yes, angled 191.04
10202029  X          Y      yes, vertical 190
10504747  Y          X      yes, x = 4237.8
10685755  Y          X      yes, x = 8767.6
9499628   XY         -      no (it is the datum)
10066508  XY         -      no
10066688  XY         -      no
```

**The split is not the material.** A braced steel frame closes at its nodes as a
stud panel does; a timber part hung on one face stays as free as a plate on a
flange. What separates the two runs is whether the count saturates, and that is
readable from the contacts themselves — the analyzer never has to be told what it
is looking at.

## What it must not depend on

**Part roles.** On M.53 the structural group came back empty: the steel marks
(`P/1283`, `P/1050`) match no role rule, so nothing was `Defining` and the chain
calculation refused outright. The contacts read fine on the same view. Freedom
has to be computable from contacts and geometry alone, or it will be unavailable
on exactly the assemblies where it decides the most.

**Loads and stiffness.** This is mobility, not analysis. No stiffness matrix, no
displacements — bodies, joints, and how much freedom is left.

## Inputs that already exist

- contacts with both owners, their shape and direction — `SolidContacts` and
  `DrawingContactCandidatePointBuilder`;
- a part's extent along an axis where one could be proven — `AxisAlignedModelExtent`,
  surfaced as `partExtentAlongChain`. It is fail-closed: absent for a tilted part
  and equally for one whose shapes yielded no usable extent, so a null withdraws
  the evidence rather than asserting anything about the part;
- the structural outline, when roles are known, as one candidate datum.

## What `Stage 1` actually gates on

Two guards were added after the first pass classified every touch as support,
which is not what `SolidContacts` itself claims:

- **Kind.** Only `ContactKind.FaceToFace` counts. `FaceToEdge` and `EdgeToEdge`
  are real touches with no area behind them — `SolidContacts`' own words call
  `FaceToFace` "the only kind that carries real support".
- **State.** Only `ContactState.Touching` counts. A `Gap` inside the search's
  broad tolerance is not zero, and an `Overlap` is a modelling error; neither is
  a position two parts have settled into.

A contact excluded by either guard is reported as `NotLoadBearing`, never folded
into "free" — a part with nothing counted against it may still have a contact
that touched and was rejected, and the two must stay distinguishable.

Direction is read from the contact's own plane normal (`Contact.Plane.Normal`,
in the same view coordinates as everything else — `ViewSolidAdapter` never
converts frames), not from the flattened shape. A face-on contact can flatten to
a narrow sliver polygon by accident of the projection, and a shape-span reading
took that sliver for a clean vertical support removing an axis nothing physical
supported. The normal does not make that mistake: a face seen face-on has a
normal pointing out of the view plane regardless of the patch's outline. A
normal whose in-plane component is negligible constrains neither in-plane axis;
of what remains, a normal close to X removes X, close to Y removes Y, and one
genuinely diagonal in-plane is `AmbiguousDirection` rather than forced onto the
nearer-looking axis. The angle used is the same 0.1° `CalcDimensionChains` uses
for a part's own extent, applied as a pure angle here — a unit normal's
components are direction cosines, not millimetres, so the 0.01 mm absolute term
that also applies there does not carry over.

`Contact.Id` is documented by `SolidContacts` as a stable key, not a proven-
unique one, so the analyzer never looks a contact back up by it. Kind, state and
normal are copied onto `ContactShapeInView` once, at flattening, while the
association with the one `Contact` that produced it is still unambiguous — see
that type's remarks on `ContactId`.

## What is still missing

- **Rotation.** Only X and Y were counted. Rotation is not hypothetical: the
  inclined end plate on M.53 is dimensioned with an angled dimension, which is
  exactly a rotation being pinned. The result carries the third axis from the
  start, even while it is always unanswered — an empty field is honest, a silent
  two-axis count reads as "fixed" for a part that can still turn.
- **A datum.** Freedom is relative to something. Candidates: the structural
  extent on a panel, the main profile's end on a girder. Until it is chosen,
  the assembly-level answer cannot say the whole thing is fixed to anything —
  only `AllPartsHaveObservedAxisContacts`, which is true exactly when every
  part's local axis gaps happen to be closed. A rigid group bolted only to
  itself reads identically to one bolted to the world; the name says only what
  was actually checked.
- **What `FaceToFace` + `Touching` still does not prove.** The Kind/State gate
  keeps out the two easy mistakes; it does not make the reading a proven
  constraint model. Two parts merely resting face to face restrain less than two
  bolted through, and neither the joining method nor the fixings are in the
  contact set to tell them apart. `FreeX`/`FreeY` name what a face contact reads
  as, not what the joint is engineered to hold.
- **Bolts.** A bolt array through two parts is a constraint and is not in the
  contact set. On M.53 one dimension is owned by a bolt array.

## Staging

1. Report only: per part, which axes are free and what constrains each, plus
   an assembly-level flag that is honest about not having a datum. No opinion
   about dimensions.

That is the whole of it for now. Whether the report should ever reach position
selection is exactly the question EWA.5 answered in the negative, and nothing
further gets planned until a drawing shows the count agreeing with a person on an
assembly whose contacts close. Rotation and a datum come first in any case; this
flag on an ungrounded read is not something to build selection on.

## Naming

`ContactGraph` is taken — it is the raw topology in `SolidContacts`, and confusing
"what touches what" with "what follows from it" would be expensive. The result is
`PartDegreesOfFreedom`, carrying `FreeX`, `FreeY`, `FreeRotation` and what
constrained the rest; the assembly-level answer is `AllPartsHaveObservedAxisContacts`,
not `IsSaturated` — the first name says only what was checked, the second would
claim a verdict about the whole assembly that nothing here is grounded to make.

Keep the name factual. "Which axes have no observed qualifying contact" is a fact
and belongs here - stated that carefully rather than as "which axes are free",
which claims more than a `FaceToFace`/`Touching` reading with no joining method,
no fixings and no datum has earned. "Which positions are required" is policy and
belongs in the dimensioning skill — the same split that keeps the chain
calculation from deciding what a correct drawing is.

## Prior art

Sketch constraint solvers in CAD answer this exact question, and report
under-constrained, fully constrained and over-constrained with the entity that
causes each. The mobility criterion (Grübler–Kutzbach, `M = 3(n−1) − 2j₁ − j₂` in
the plane) is the same count on bodies and joints. Look there before inventing a
walk, including for the hard part: naming which constraint is the redundant one.

The direction is reversed from a sketch, and that is worth remembering. A sketch
starts unconstrained and a person adds constraints until nothing moves. Here the
geometry is already built, and the question is which numbers are missing to
explain it.
