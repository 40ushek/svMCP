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

## Stop only for a real policy gap

Do not stop because geometry is raked: the structural outline answers its real
corners. Stop only when the drawing subject is outside the measured domain, a
position has several equally plausible subjects, or a plant convention decides
how many chains to use and the reference rules do not decide it.
