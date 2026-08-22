# Steel rules for AssemblyDrawing dimensions

Read this reference instead of `plant-rules.md` when the drawing subject is a steel
assembly — a rolled or welded main member with plates, gussets and stiffeners fixed to it.

**Evidence status differs from the timber file, and that difference is the point.**
`plant-rules.md` was measured on five real drawings the plant had dimensioned. This file
is derived from Tekla's own documented dimensioning model plus one read drawing (`M.80`,
a HEB160 column). Every rule below carries its class:

| Class | Means |
|---|---|
| `[tekla]` | Tekla's own automatic dimensioning names this and says what it does |
| `[seen]` | observed on a real dimensioned drawing, named there |
| `[guess]` | adaptation reasoned from the two above; not yet confirmed on a sheet |

A `[guess]` may be used to build a plan, but it is not evidence for changing code, and it
must never be promoted to `[seen]` without a drawing behind it.

## The subject and the roles

A steel assembly is one **main part** with everything else fixed to it. That is not a
convention here, it is the model: `Assembly.GetMainPart()` answers it directly, and the
answer reaches this skill as `mainPartModelIds`. `[tekla]`

- **Everything is in the structural geometry: pass no exclusions.** A steel assembly is
  the member and what is welded to it, and all of it fixes the size. `[guess]`
- **The mark prefix decides nothing here.** On `M.80` all nine parts — column, base plate,
  head plate, four gussets, two flange plates — carry prefix `P`. A prefix cannot separate
  the member from what is welded to it, which is why nothing in the code reads one. `[seen]`
- **The main part is a flag, not a filter.** `mainPartModelIds` in the
  `get_structural_chain_positions` response names it, read straight from the assembly. It
  removes no part from the geometry; it says which part the others are measured from. `[tekla]`
- **Gate, before any steel plan is built: `mainPartModelIds` must hold exactly one id and
  `mainPartUnresolvedModelIds` must be empty.** Fail either and stop — every rule below
  measures from the main part, and neither "no main part" nor "the assembly could not be
  asked" leaves anything to measure from. The second is why the two fields are separate:
  a part whose assembly threw reports `isMainPart=false` exactly like a part that simply
  is not one, and a plan built on that would measure from whatever was left while the
  response still called itself complete.
- The profile is a corroborating signal only (`HEB160` against `BLE10*40`), never the
  decision: a welded plate girder's main part is a plate too. `[guess]`

## Base of measurement

Tekla positions secondary parts **from the main part**, and offers exactly three bases:
the main part's reference line, its working points, or a secondary part's own edge or bolt
holes (`Position parts/bolts to`). `Measure from` in a dimensioning rule offers the same
choice between `Assembly`, `Main Part`, `Part Name`, `Current Part`. `[tekla]`

Adapted:

1. **A secondary part's position is measured from the main part, not from the assembly's
   outer extent.** `[guess]` This is the steel form of the anchor rule the timber file
   states under "Reading the preliminary chains": an anchor is its own support point.
2. **One base per side, held for the whole drawing.** Mixing "from the column face" and
   "from the outer edge of a gusset" in one chain hides which face the fabricator sets
   out from. `[guess]`

## What earns a dimension

Tekla's rule types, mapped onto what this project already reads. The mapping is the
useful part: no new geometry is needed for any row marked with a source.

| Tekla dimension type | Subject | Our source |
|---|---|---|
| `Overall dimensions` | assembly bounding size | extreme positions of `get_structural_chain_positions` |
| `Main part work points` | check dimensions between outermost work points | `place_control_diagonals` |
| `Main part shape` | how the member's end is cut | tilted-edge corner positions |
| `Secondary parts` (`Internal`) | where a welded part sits | positions whose support carries a secondary's `modelId` |
| `Holes` / `Recesses` | openings | supports with `isHole` |
| `Edge shape` | contour of the visible faces | axis-aligned edge positions |
| `Bolts` (`Filter`, Bolt dimensions tab) | edge distances, spacing, extreme bolts | **none — see Gaps** |
| `Neighbor parts` | connecting members | out of scope for an assembly drawing |

## The "Necessary" principle

Tekla's `Internal` setting for secondary parts is `None` / `Necessary` / `All`, and
`Necessary` has a precise documented meaning: a dimension is added when the part's
**asymmetry is smaller than the `Recognizable distance`** — that is, when a fabricator
could fit the part the wrong way round without the number, the classic case being a
rectangle nearly as long as it is wide. `[tekla]`

This is the single most useful thing the Tekla model gives us, because it names *why* a
number is on the sheet: **dimension what cannot be told by looking.**

**Which of the three the plant runs is not known, and it decides how many numbers belong
on the sheet.** Rules 3 to 5 below are the `Necessary` judgement and only that one, so
they may not be applied until the policy is stated for this drawing:

> Ask the operator, in these words: does this plant dimension secondary parts `None`,
> `Necessary` or `All`? Record the answer in the final response and log it as its own
> `task` line.

All three answers are real answers, and each builds a different plan:

| Answer | What the plan does with a secondary part's internal dimension |
|---|---|
| `None` | **Create none.** Every internal position of a secondary part is `Removed`, with `Internal=None` as its reason. Rules 3 to 5 are not consulted: there is nothing for them to choose between |
| `Necessary` | **Apply rules 3 to 5** — keep what the shape cannot tell, drop what it can |
| `All` | **Keep every admissible internal position.** Rules 3 to 5 are not a filter here: `Necessary` is the judgement this plant declined, and applying it anyway would quietly delete numbers the plant asks for. The other rules still apply — near-duplicates are still one position (see "Reading the preliminary chains"), a span is still not printed twice, and clutter control 6 to 8 still hold |

`None` is not a mistake to talk the operator out of. A plant that marks its secondary parts
and dimensions only the overall is dimensioning by the mark, which is exactly the argument
the timber file makes under "Background".

Without an answer, stop before writing any secondary-part dimension — see Stop conditions.
Overall dimensions, main-part shape and work points do not depend on the policy and are
not blocked by it under any of the three.

Adapted (rules 3 to 5 apply under `Necessary`; see the table above for the other two):

3. **Keep a dimension that resolves an ambiguity; drop one the shape already answers.**
   `[guess]` A gusset set 5 mm back from a flange edge needs the number; one that runs
   flush to an outline corner does not.
4. **A part's own size may be printed on steel.** `[seen]` On `M.80` the front view carries
   exactly one chain, `150 · 160` — the head plate against the flange width of the HEB160.
   Timber rule 1 deletes that span as "restating one part's own size"; here it is the only
   dimension on the view, and it is what tells the fabricator the plate is set in 5 mm each
   side. **The timber rule is inverted, not relaxed**, and this is the sharpest reason the
   two rule sets cannot be merged.
5. **Symmetric parts still need one number.** `[guess]` Follows from 3: symmetry is what
   makes a part impossible to place wrong, so the number that stays is the one locating it
   along the member, not the one across it.

## Clutter control

Tekla suppresses and merges rather than printing everything: dimensions combine by name,
position number, or coordinate within a tolerance (**default 50 mm**); bolt runs print as
`3×60` or `3×60=180` above a minimum count; a `Minimum dimension length` suppresses short
dimensions altogether. `[tekla]`

Adapted:

6. **Do not print a short segment unless it bounds a hole, an edge distance, or an offset
   a fitter must hold.** `[guess]` On `M.80` a 10 mm segment is printed twice, and both
   times it is a gusset thickness at the plate edge — a bounded feature, not clutter.
7. **Repeated equal spacings belong on one line, not as separate chains.** `[guess]`
8. **The 50 mm combining tolerance is a layout number, not a geometry one.** It groups
   dimensions onto a shared line; it never merges two positions into one. Position
   coincidence stays at the arithmetic tolerance the calculation reports. `[tekla]`

## Where the dimensions live

On `M.80`, 22 of 24 dimensions are in the **section views** (1:5): the base views carry one
each, and the bottom view carries none. `[seen]`

9. **Sections are a first-class subject on steel, not an afterthought.** `[seen]` The
   timber loop — resolve `Top`, `Bottom`, `Left`, `Right` of a base view — does not describe
   this drawing. A steel run resolves the base views *and* each section that shows a
   connection.
10. **A side may legitimately end with no chain.** `[seen]` The bottom view of `M.80` has
   none, and nothing is missing: what it would show is already dimensioned in section.

## Gaps — do not invent these

- **Bolts have no coordinate source.** `BoltArray` is not part of the structural outline,
  so edge distances, spacing and extreme-bolt checks — a whole Tekla tab — cannot be
  planned from what we read today. This is a code gap, not a rule gap. `[seen]`
- **Section view coordinates.** Dimensions read from section `I` on `M.80` sit near
  `x = -43369` while the view occupies `x = 50…172` on the sheet. Before anything is
  written into a section, the coordinate system `create_dimension` expects there must be
  established. `[seen]`
- **Which `Internal` policy the plant runs** — `None`, `Necessary` or `All` — is
  unknown, and it decides how many secondary-part dimensions belong on a sheet. Until a
  plant states it standingly it is asked per drawing (see "The Necessary principle") and
  it is a stop condition, never a `[guess]` to build a plan on. Reading more of that
  plant's drawings is what settles it for good. `[guess]`
- **Welded plate girders, trusses, and braced frames** are not covered. `M.80` is one
  column. `[seen]`

## Stop conditions

Same shape as the timber file, plus these:

- the main-part gate above fails: no id, more than one, or any unresolved part;
- a secondary part's internal dimension is in question and the plant's `Internal` policy
  has not been stated for this drawing — `None`, `Necessary` or `All`. Place what does not
  depend on it, say which side is unresolved and why, and ask. An answer of `None` is not
  a blocker: it resolves the side with no chain and a stated reason;
- a bolt dimension is required (no source, see Gaps);
- a section must be dimensioned before its coordinate system is settled.

Sources for the `[tekla]` rows: Tekla User Assistance — *Dimensioning rule properties*,
*Dimensioning properties (Integrated dimensioning)*, *Part dimensions tab*, *Position
dimensions tab*; Tekla Open API — *Assemblies*.
