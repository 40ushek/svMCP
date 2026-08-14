# Plant rules for AssemblyDrawing dimensions

Read this reference while deciding which preliminary positions to keep. These
rules were measured on timber-wall drawings; do not generalize them to steel,
trusses, roof panels, or parts with cut-outs without evidence.

## Reading the preliminary chains

- Top and Bottom chains carry **X** positions.
- Left and Right chains carry **Y** positions.
- A position is a coordinate, not a drawing point. A square-on contour edge
  provides one coordinate; a tilted edge provides its corner coordinates.
- **Near-duplicate coordinates are one position.** The list carries values a
  fraction of a millimetre apart — EW.18 has `0.01 / 60.01 / 60.21` at the left
  edge and `1597.80 / 1597.84` on the left side. Collapse anything within about
  0.5 mm and keep the one anchored on the `Defining` member's face; the other
  belongs to a neighbouring part or a sheathing edge. Never print both.
- The structural outline is `Defining` parts only. `Attached` parts never widen
  the structural extent.
- The real corner of a raked panel is an endpoint. Never replace it with the
  bounding-box corner or the nearest member.

## Keep or remove

1. Remove a span that merely restates one part's own size: both anchors resolve
   to that part and its explicitly supplied, axis-aligned extent along the chain
   equals the span. Never infer this from a part bbox: on a raked part the box
   spans empty space and is not a dimensioning fact.
2. An overall chain has only its two extreme positions.
3. Keep one face for a regular stud family, **because the tape runs down one
   side** — that reason is what tells you which face, and without it the rule
   decides nothing. A doubled post is one position.
   On a raked edge the choice stops being cosmetic: the two faces of one member
   give coordinates differing by its width times the slope. Measured on EW.8,
   60 mm across a 0.414 rake moved the value 25 mm, and no defect check saw it —
   both faces are honest anchors. Take the face the position list already used;
   a coordinate you had to fetch from anywhere else is the warning sign.
4. Keep both faces that bound an opening. Clear opening width and height must
   remain readable.
4a. **A null `partExtentAlongChain` withdraws the automatic test; it decides
   nothing.** The field is fail-closed: it is absent for a part with a tilted
   edge, and equally for one whose shapes gave no usable extent at all. So a null
   says the mechanical part-size comparison cannot be run here — not that the
   part must keep a position. Judge it on the drawing instead, and do not let the
   absence of evidence read as evidence.
   Two measured cases on EWA.5 where the answer was to keep, both dimensioned by
   the plant and both dropped by an earlier pass: the diagonal brace, whose ends
   are interior, and the raked top plate, whose upper face is an outline corner
   while its underside sits 61.4 below it. In both, one face was given and the
   other followed from nothing a reader could measure. That is the argument to
   keep — not the null itself, and not a rule that every tilted part is kept: a
   contact may well settle a tilted part, and then it needs no number.
   That 61.4 was once explained by the layer-fit case under `Layers`. It is not
   that case: the raked plate is frame, not a layer laid over a finished frame,
   and the reason to keep it is the one above. The layer-fit case stands where it
   was measured — battens and sheathing — and does not extend to framing.
4b. **A contact may choose among positions that already exist. It never adds
   one.** A brace crossing the bottom plate offers three — its two landings and
   the joint with its pair — and on EWA.5 `get_contact_candidate_points` reports
   `modelObjectIds [3458147, 3457274]`, `FaceToFace`, x = 1476.5, which coincides
   with a calculated position. Prefer that one. Two conditions before it counts:
   the contact result is complete, and a calculated position already carries that
   exact coordinate. **Exact — no tolerance is applied.** Contacts and positions
   are flattened from the same geometry, and on EWA.5 the two agreed to the last
   digit (1476.50173 against 1476.50173, difference 0). A contact coordinate that
   does not land on a calculated position is a different feature, and reaching for
   the nearest one would be exactly the invention of a coordinate this rule
   forbids. If a run ever shows the two disagreeing by a hair, the number needed
   is the position coincidence tolerance, and it must be added to the response
   before this rule can use it — it is not serialized today.
   Fail either condition and the contact says nothing — neither to add a
   coordinate nor to remove one, and an incomplete contact read is not evidence
   that a joint is absent.
   Having chosen it, measure from the faces bounding the bay it falls in rather
   than from one stud family's face: 1206.5 and 1746.5 give 270 and 270, and two
   equal numbers show the joint is central without anyone measuring, where the
   stud face would read 330 and 210 and show nothing. With a single brace there
   is no contact and the choice among its positions is free.
5. Use edges, never axes. Axis evidence is a flagged last resort.
6. Put a chain on the free side of the subject and keep that convention within
   the drawing.
7. Remove a chain whose printed values are contained in another chain, except
   for an overall chain.
8. On a wall, vertical chains start at the underside of the bottom plate.

## Layers

Dimension each layer only for what remains open at that stage.

- Frame chains locate framing.
- Sheathing is dimensioned by its joints; internal joints sit on framing.
- Battens require containment within framing, not centre alignment.
- A layer over a completed frame may need its own part-size span when that span
  communicates fit or overhang.

## Contacts

Contacts distinguish a joint from a free edge. They are semantic evidence only:
they do not create chain coordinates, and an incomplete contact result cannot
prove that an edge is free.

A contact that has no selected chain position is ordinary and never demands a
dimension. Most joints are not dimensioned; contacts may justify a selected
position, but do not create either a required coordinate or a defect by themselves.

Their one decisive use so far is rule 4b: choosing which of a tilted part's
positions to keep. That is the shape of evidence they give — not a new
coordinate, but a reason to prefer one that already exists.

## Background: why these panels are dimensioned the way they are

Not a rule and not a procedure — nothing below is to be counted or applied while
choosing positions. It is here so the rules above read as something other than
arbitrary.

A stud panel is one contacted body: a stud is seated on the bottom plate, capped
by the top one, butted by noggins each side. With parts marked, such a frame
largely locates itself, and much of what is on the sheet is there to be checked
against rather than to position anything — equal 600s in which a mistake stands
out, 270 against 270 showing a joint is central, a control diagonal that carries
no position at all. An open assembly, where a part meets another on one face and
can slide along it, is a different case and was measured on one steel girder.

Neither observation decides a keep-or-remove. An attempt to drive selection from
counting contacts was tried and failed on the first panel: it cleared nearly
every stud, which the plant dimensions regardless. The measurements and that
negative result are in `HISTORY.md`, and the standing of the idea is set out in
`Drawing/Geometry/Contacts/ROADMAP_PART_FREEDOM.md` — read-only diagnostic, not
an input to dimensioning.

## Stop only for a real policy gap

Do not stop because geometry is raked: the structural outline answers its real
corners. Stop only when the drawing subject is outside the measured domain, a
position has several equally plausible subjects, or a plant convention decides
how many chains to use and the reference rules do not decide it.
