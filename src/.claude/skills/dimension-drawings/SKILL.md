---
name: dimension-drawings
description: Place, clean up or review dimensions on a Tekla assembly drawing — thinning redundant chain points, removing duplicate chains, fixing snap points, or dimensioning a drawing from scratch. Use whenever the task touches drawing dimensions in this project.
---

# Dimensioning Tekla assembly drawings

Derived 2026-08-01 on model Midi_1-1-1: about a dozen interior walls, two exterior
walls, and two overlay layers of one exterior wall (OSB sheathing, battens).
**Never seen:** roof panels, trusses, parts with cut-outs, steel, any other plant.

Evidence — which drawing, which numbers, which earlier versions were wrong, and a
changelog of this file — lives next door in `HISTORY.md`, which is not loaded with
this one. Read it when a rule looks doubtful or you are about to invent one.

## Scope

AssemblyDrawing only — check type from get_drawing_context. On single-part drawings
these rules destroy the drawing; GA drawings have a different subject.

The premise: **a dimension says where a part goes, not how big it is.** Part sizes
live on fabrication drawings. Exception on overlay layers — see rule 1.

## Checklist — compute all of these before proposing anything

They come out of the two reads you already make. Put them in the same script as the
capture: a separate step gets skipped, and each of these has cost a wrong answer.

1. **Same coordinate space?** Parts extent per axis against what the chains measure.
   If the height is in the parts Z and the chains Y — stop, nothing else will work.
2. **Both rows.** Compute relative (gaps between adjacent points) and absolute
   (running totals from the start). TeklaDimensionType says which are printed:
   Relative, Absolute, or RelativeAndAbsolute meaning both.
3. **Rule 1 mechanically.** For each adjacent pair: do both points resolve to one
   part whose own extent along the axis equals the span?
4. **Openings.** Stud gaps larger than the usual spacing. Points must sit on the
   faces that bound the opening. Clear width and height must appear in a row.
5. **Phantom points.** Run get_dimension_chain_coverage on every chain — not only
   when in doubt. fallbackOnly means the point matched a box corner; Missing means it
   belongs to nothing at all, which a hand-rolled bbox test does not catch. Re-anchor
   either way, **including when the printed value does not depend on the anchor** — a
   horizontal chain hanging off a phantom y still gets fixed.
6. **Points far from their chain.** A chain drawn on the left takes points near the
   left, not across the view. Remove them; a wide panel is not an excuse.
7. **Containment.** Compare what each chain prints. All values inside another's and
   it goes. Overall dimensions are the exception.
8. **Start point.** Which point is first? On a layer over a finished frame the zero
   belongs on the frame. Unverifiable afterwards — read-back normalises the order.

Then propose, stating what each chain will print.

## Traps

- **Read-back normalises point order** (top-to-bottom, right-to-left). The order
  passed to create_dimension fixes the absolute zero and is not recoverable.
- **LengthList is neither row** — computed here from the normalised order.
- **Never difference adjacent points to say what a chain reads.** Compute both rows
  and check the type.
- **Editing renumbers**: use newDimensionId / mergedDimensionId. Match states by
  geometry, never by id.
- **Offset side comes from the direction vector**, never from a negative distance.
  direction is required — guessing rebuilds a vertical chain as horizontal.
- **move_dimension moves by a delta**, it does not set a value.
- **create_dimension measures its distance from the points**, not from the part
  edge — a chain whose points sit inside the wall draws its line inside too.
- **Creating a dimension reflows its neighbours.** Re-read their offsets after.
- **Deploy before testing.** dotnet build writes to bin/; the bridge runs from the
  Tekla extensions folder. Copy both TeklaBridge.exe and TeklaMcpServer.Api.dll
  after stopping the process.

## Who it is for

A person at a table with a tape measure. Every rule follows from that; a robot
would not need the drawing at all. When a question has no obvious answer, ask what
the fitter would do with the number.

| rule | because |
|---|---|
| points on faces, not axes | the tape hook needs an edge; timber has no centreline |
| one face per family | the tape runs down one side |
| running totals | the hook stays on the first beam; error does not accumulate |
| wall reads from the bottom | that is where the tape is hooked |
| face bounding an opening | the fitter needs the clear size |
| no span restating a part size | the part is already made |
| control diagonal untouched | checked with a tape on the table, in the moment |

**Fewer dimensions is better, provided everything is clear — like good code.** Not
by trimming, but by finding what the reader already knows elsewhere and not
repeating it. Fewer numbers means fewer to misread. Removing something needed is
far worse than leaving one span too many.

## The measured rules

**1. A span equal to a part's own size is redundant.** Compare anchors, not lengths
— both points on the same part, span equal to its own extent. The numbers differ by
profile (60, 120, 160 all seen); no length threshold works. *Exception:* on a layer
laid over a finished frame the part's own size may carry the fit — a batten's
length gives its overhang and its top gap together. The exception is about length
along the chain, **not thickness across it**: 45 between a header underside and the
batten top came straight out.

**2. A chain has a subject.** An overall chain keeps only its two extreme points,
however honestly the middle ones sit on parts.

**3. Stud spacing tells you where the openings are.** Rhythm is 600 to 625; a gap
noticeably larger is an opening. 600 is the classic, roughly 80% offhand: it suits
1200 plasterboard exactly and 1250 OSB trimmed by 50. Do not read sheet size back
from spacing. Cripples above an opening do not follow the rhythm; a doubled stud is
one position.

**4. On a wall, vertical chains start at the bottom** — the underside of the bottom
plate, where the tape is hooked. A constraint on point order, not a habit.

**5. At an opening, the point goes on the face that bounds it** — the jamb, the
header underside, not the far face. Anchor to the framing members. The clear width
and height must be readable, and an opening is dimensioned more than once.

**6. Measure to an edge, never to an axis.** Timber has no centreline. Axis
candidates rank below face and vertex ones and are a last resort worth flagging.
Untested outside timber.

**7. A chain goes on the side its subject is on.** Which side is free; once chosen,
hold to it across drawings so a stack reads alike. Two chains take opposite sides,
each by its own subject. Says nothing about how many chains there should be.

**8. A composite post is marked by its 60X100 member** — not the board, not the
outer face of the pair. End posts are covered by the assembly edges.

**9. If one chain's values are contained in another's, it goes.** Test on printed
values, not points; this catches a chain measuring the same parts from the other
face. Check before proposing — missed three times in one session. *Exception:* an
overall dimension, unless the chain it duplicates is only two values long.

**10. A sheathing layer is dimensioned by its joints, and every internal joint sits
on a frame member's centreline** — both sheets need wood to nail into, half the
member each, and the sheathing works as a diaphragm against shear. Joints are
staggered so there is no continuous fold line, and the noggin rows exist to back
them. The frame is cut to suit the sheet, not the reverse. *Battens differ:* nailed
through the sheet, they need only lie inside some frame member's width — test
containment, not centring. A 45 batten on a 60 stud has 15 mm of play.

**11. On a raked top the chain ends at the panel's real corner, and an overall may
span two different x.** Tekla measures the projection between the points, so the
overall height runs from the lowest real point to the highest wherever they sit —
"no single part is that tall" proves nothing about an assembly overall. The corner
of the raked member is the anchor, not the top of the nearest batten under it. One
worked case (EW.4-6).

## Layers

Each layer has its own reader at a later moment and dimensions only what is still
open. A frame drawing locates parts; a sheathing or batten drawing locates its own
layer against a frame already built, and never re-dimensions the frame.

- **relative row = the rhythm; absolute row = the marks stepped off from the frame.**
  Round numbers in the absolute row are not a goal — if they appear, the zero may be
  sitting on the wrong thing.
- **Do not thin a chain because its relative values repeat.** Cladding is dimensioned
  board by board so error does not accumulate and the boards meet the neighbouring
  panel to within a couple of millimetres.

## More than one correct answer

The user produced two acceptable dimensionings of one wall and said neither is the
only truth. They agreed on which points are redundant, which face each sits on,
where the chain starts and what the sheet must show; they differed in how the work
was split between chains.

**Do not grade by exact reproduction**, and do not present one layout as the correct
one. Prefer the shorter of two correct answers.

**Some of it is factory convention and cannot be derived** — how many chains, which
carries what, where each sits. That comes from the plant's habit and the production
technology, not from the drawing. It belongs in configuration, never in inference,
and captured examples are local to one plant. When a layout question has no
geometric answer, ask.

## Stop and ask

- **Raked or gable tops beyond rule 11** — stud ends at different heights, and the
  bounding box of a raked member spans the whole panel, so every bbox check lies.
- **Unfamiliar assembly type** — truss, beam, plate with cuts.
- **A point where several parts each end** — the value says "to what, exactly?".
- **Anything the examples do not cover.** Say so instead of inventing a rule.

Judge each defect separately: stopping on one ground must not block an unrelated fix
in the same chain.

## Capturing a case

cases/dimension_cases/assembly/<drawing-guid>/, gitignored — real drawings.
Commands: get_dimension_contexts, get_all_parts_geometry_in_view,
get_part_candidate_points_in_view, get_dimension_chain_coverage — called on
TeklaBridge.exe directly from the extensions folder, which avoids the MCP timeout.

Capture before and after a person edits, and record in meta.json whether a human
actually passed over the chains: a drawing that merely happened to be open is not
reference material. Capture the before state first and check it reached disk —
states that were not saved cannot be re-captured.
