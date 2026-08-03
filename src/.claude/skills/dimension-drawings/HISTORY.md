# История разработки скилла dimension-drawings

Доказательная часть: на каких чертежах что измерено, какие числа получились, какие
версии оказались неверными и почему. Отсюда выросли правила в `SKILL.md`.

Читать, когда правило вызывает сомнение, когда надо уточнить его границы, или
когда возникает соблазн вывести правило заново — здесь видно, что уже пробовали.

## Журнал правок SKILL.md

Каждая строка — что изменилось в скиле, из-за какого чертежа и почему. Правило,
попавшее в скил без строки здесь, ничем не подтверждено.

| дата | чертёж | правка в SKILL.md | что её вызвало |
|---|---|---|---|
| 2026-08-01 | ~15 чертежей, `IW.1`–`IW.16`, `EW.1`, `EW.3`, `EW.4-1` | правила 1–10, список проверок, ловушки, «слои», «больше одного верного ответа» | вывод с нуля; доказательства ниже по этому файлу |
| 2026-08-01 | — | скил переписан с 1023 строк (~18k токенов) до 188 (~3.3k); доказательства вынесены сюда | скил грузится целиком каждый раз |
| 2026-08-02 | `EW.4-6` | проверка 5: coverage обязателен на каждой цепочке, `Missing` ловится только им; привязку правят и когда печатаемое число от неё не зависит | ассистент увидел фантом в горизонтальной цепочке и оставил, человек исправил |
| 2026-08-02 | `EW.4-6` | проверка 6: точку далеко от своей цепочки удаляют, а не терпят | оставлена точка в 960 мм поперёк, человек удалил |
| 2026-08-02 | `EW.4-6` | правило 1: исключение для накладного слоя — про длину вдоль цепочки, не про толщину поперёк | оставлены 45 в собственную толщину бруска, человек удалил |
| 2026-08-02 | `EW.4-6` | правило 11: на скате цепочка кончается реальным углом панели, у габарита концы могут быть на разных `x` | ассистент заменил габарит `3535.1` на `3110.4`, человек вернул на двух реальных точках |
| 2026-08-02 | `EW.4-6` | «стоп и спроси» про фронтоны сужено до случаев вне правила 11 | появился первый разобранный скат |
| 2026-08-02 | — | `.claude/skills/` выведена из-под `.gitignore`; этот файл вернулся в папку скила как `HISTORY.md` | скил был вне версий, откатить его правку было нечем. Теперь `SKILL.md` и `HISTORY.md` версионируются вместе. Из папки скила автоматически грузится только `SKILL.md`, поэтому на токенах это не сказывается. |

---

Всё, кроме правок от 2026-08-02, получено за один рабочий день, 2026-08-01, на
модели `Midi_1-1-1`: около десятка внутренних стен, две наружных и два накладных
слоя одной наружной — обшивка OSB и обрешётка. Кровельных панелей, ферм, деталей
с вырезами и стали не было ни разу.

---

# Dimensioning Tekla assembly drawings

Everything below was derived on real drawings in the model `Midi_1-1-1`, over one
working day, 2026-08-01. What that covers:

- **about a dozen interior walls** (`IW`), the timber frame layer — with and
  without door openings, with raked tops, with composite posts;
- **two exterior walls** (`EW`), also the frame layer;
- **two overlay layers of one exterior wall**: the OSB sheathing and the battens.

The traps are settled facts, checked against the drawings. The rules are ordered by
how well they are established: the measured ones carry the drawing and the numbers
they came from; the provisional section below them is older inference and should be
trusted less.

**Never tested:** roof panels, trusses, parts with cut-outs, steel, and any plant
other than this one. The layout conventions especially do not transfer — see the
section on factory convention.

An earlier version of this file claimed it was derived on roof panels. That was
wrong; no roof panel has been looked at.

## Scope: assembly drawings only

Check `type` from `get_drawing_context`. These rules apply to `AssemblyDrawing` and
nothing else.

The whole thing rests on one premise: **on an assembly drawing a dimension says
where a part goes, not how big it is.** The parts are already made — each carries a
mark and has its own fabrication drawing, so its size is fixed and shown elsewhere.
Dimensioning a part's own thickness on the assembly asks the fitter to check
something they cannot change, and crowds out what they do need: positions.

That premise is why the rules below thin chains so aggressively. It does not hold
anywhere else:

- **Single-part drawings** exist precisely to state a part's own dimensions. Thinning
  them by these rules destroys the drawing.
- **GA drawings** locate assemblies against grids and levels, not parts against each
  other. Different subject entirely.

If the drawing is not an assembly drawing, stop and say so rather than adapting the
rules by analogy.

## The checklist — compute these before proposing anything

This file is long and you will not re-read it while working. This section is the
part that has to be executed. Everything below it is the reasoning behind it.

Every one of these was skipped at least once and cost a wrong answer. They are
cheap: all of them come out of the two reads you already make,
`get_dimension_contexts` and `get_all_parts_geometry_in_view`. Put them in the same
script as the capture, so there is no separate step to skip.

**Before deciding anything about a drawing:**

1. **Same coordinate space?** Compare the parts' extent per axis with what the
   chains measure. If the wall's height is in the parts' Z and the chains' Y, stop —
   nothing below will work. → rule at the top of "what will mislead you"
2. **What does each chain print?** Compute *both* rows — relative gaps and absolute
   running totals — and read `TeklaDimensionType` to know which are shown. Never
   report one row as "what it reads".
3. **Rule 1, mechanically.** For every adjacent pair, do both points resolve to one
   part whose own extent along the axis equals the span? Those are redundant.
4. **Openings.** Find gaps between full-height studs larger than the usual spacing.
   For each opening, check that the chain's points sit on the faces that *bound* it
   — the jamb, the header underside — not on the far faces. Then check the clear
   width and height actually appear in a printed row.
5. **Phantom points.** Any point that matches only a bounding-box corner. On a raked
   or cut member the box corner is in empty space. `fallbackOnly` in
   `get_dimension_chain_coverage` reports exactly this.
6. **Points far from their own chain.** A vertical chain drawn on the left should
   take its points near the left. A point on the far side drags an extension line
   across the whole view.
7. **Containment.** Write out what each chain prints and compare sets. If one
   chain's values are all inside another's, it goes — except an overall dimension,
   which stays.
8. **Start point.** For an absolute row, which point is first? On a layer laid over
   a finished frame the zero belongs on the frame. This cannot be checked after the
   fact — read-back normalises the order.

**Then, and only then, propose.** State what each chain will print afterwards.

The failure mode is always the same: the drawing looks simple, the numbers look
obvious, and the checks feel like ceremony. On `EW.4` — a gable with an opening —
skipping them produced three errors in one pass: a jamb missed, a point placed in
empty space, and two chains reaching across the view.

## Reference: what will mislead you

These four cost a full session. Every one of them made a correct drawing look
broken, or a broken result look fine.

### Check that parts and dimensions are in the same coordinate space

Not guaranteed. On `IW.10` the part geometry came back in **model** coordinates
while the dimensions were in view coordinates, so nothing could be matched to
anything. Nine drawings in a row had been fine, which is exactly why it is worth
checking rather than assuming.

The tell is cheap. Compare the extent of the parts with what the chains measure:

- parts spanned 1363 in X, **100 in Y**, 3738.2 in Z;
- the chains measured 1363 wide and **3738.2 tall** — so the wall's height was in
  the parts' Z and the dimensions' Y.

`axisY` on any part says it directly: it points up the wall when the read is
correct, and it was `[0, 0, 1000]` — along world Z — when it was not.

Repeating the read does not fix it, and different views give different wrong
answers. **Stop when this happens.** Every rule in this file works by matching a
dimension point to a part face; without a shared space they are all guesswork.
Placing dimensions from the chain coordinates alone is not a fallback, it is
inventing.

### Relative, absolute, or both — and it is a setting

Two kinds of value, named for what they are — from the user: **relative is between
the points, absolute is the summation.** Which of them a chain prints is a choice,
recorded in `TeklaDimensionType`:

| type | printed |
|---|---|
| `Relative` | the gap between each pair of adjacent points |
| `Absolute` | the running total from the chain's start |
| `RelativeAndAbsolute` | both, as two rows — gaps along the line, totals rotated beneath |

Seen on the batten drawing `EW.3-6`, chain `1790` set to both: `60, 603, 559, 625,
537, 105` along the line and `0, 60, 663, 1222, 1847, 2383, 2488` beneath it.

**Compute both rows and check the type to know which are shown.** Two claims made
earlier in this session were wrong and are retracted: that a chain reads as the
gaps, and that a chain reads as the running total. Each is half the picture, and
the assistant reached the second by exporting a PDF and not noticing the other row.

So the 625 stud rhythm, the 885 door width, and small fitting gaps like the 5 mm a
batten stops short of the frame top are all **printed** whenever the relative row
is on. Nothing there has to be obtained by subtraction.

### What each row is for

Seen clearly on `EW.3-7`, the horizontal batten layer: six battens at 600 spacing
over a finished frame.

- **the relative row carries the rhythm** — `600, 600, 600`, the design intent,
  read at a glance and checked against the sheet material it serves;
- **the absolute row carries the marks** — the numbers the fitter steps off from
  the frame. They need not be round, and chasing round numbers there is a mistake.

Before thinning, that chain marked *both* faces of every batten, so its relative
row ran `35, 35, 530, 70, 530, 70, …` — the 70s being each batten's own height,
which its part mark already gives. Dropping them left `35, 565, 600, 600, 600,
129.6, 95` and the rhythm became visible where it belongs.

The assistant first set the zero on the first batten's bottom edge, because that
made the absolute row read `600, 1200, 1800, 2400` — pleasingly round, and wrong:
the fitter measures from the frame, not from a batten he has not yet placed. With
the zero moved to the frame the marks became `35, 565, 1165, 1765, 2365` and the
round numbers stayed in the relative row, which is where they were always readable.

**Round numbers in the absolute row are not a goal.** If they appear, the zero may
be sitting on the wrong thing.

### Why the absolute row exists: error does not accumulate

The cladding layer `EW.3-8` settles it. Eighteen boards, each 142 wide, laid at a
130 pitch with an overlap. The chain dimensions **every one of them** — a relative
row of `159, 130 ×17, 119` and an absolute row giving all nineteen positions.

Seventeen identical numbers look like something to thin. They are not. From the
user: the cladding is fitted at the works and the panels are shipped to site, where
they must line up with their neighbours to within a couple of millimetres, or there
are problems.

Measure board-to-board and eighteen overlaps accumulate whatever error each one
carries. **Measure every board from one origin and no error accumulates** — each
mark is independent. That is what the absolute row is for, and it is why repeated
identical relative values are not redundancy: the two rows answer different
questions, the relative one "is the pitch right" and the absolute one "is this
board in the right place".

**Do not thin a chain because its relative values repeat.** Check what the absolute
row is doing first.

The same drawing shows the other half of the arrangement: the cladding starts at
159 rather than 0 and the absolute row ends at the frame edge, 2488, not at the
last board's edge, 2511. The panel is deliberately left bare at the edges so the
covering boards over the panel joint can be nailed on site.

The user names a second reason for the precision: **windows and doors.** Cladding
has to be cut around an opening and the opening itself has to land exactly, or the
frame and the trim around it will not fit. Not observed here — `EW.3` has no
opening — so how a cladding chain is arranged around one is still unknown. Expect
it to matter; do not invent the rule.

### The start point matters for absolute dimensions

From the user, and it follows from the rest: the absolute row counts from the
chain's start, so where that start sits is part of the dimensioning, not an
accident of how the points were entered.

It ties to two things already in this file: the run counts from the start of the
reference line, and that start is fixed by the order the points are passed to
`create_dimension` — read-back order never shows it. And by rule 4 a wall's chains
start at the bottom, and rule 6's tape hook goes on the first beam. The zero of an
absolute row is that hook.

Get the order wrong and every number in the absolute row is measured from the wrong
end, while the relative row looks perfectly correct.

**On a layer laid over a finished frame, the zero usually goes on the frame** — the
user's word is *usually*, so treat it as the norm rather than a law. The reason
follows from who is reading: when the fitter marks out, the battens are not there
yet and the frame is. He can only hook a tape on what exists.

The batten drawing `EW.3-6` does it the other way: its absolute row starts at the
batten's own bottom edge and reads `0, 40, 2625, 2630`, so the frame appears at 40
rather than at zero. It is readable, but it counts from something not yet placed.

When choosing the start for an overlaid layer, prefer the frame edge.

**Reordering moves the zero and leaves the relative row alone.** The relative
segments are measured between geometrically adjacent points, so their values do not
depend on the order the points were passed. Only the absolute row is affected.

Demonstrated on `EW.3-6`. The chain was recreated with the frame's bottom edge
first instead of the batten's:

| row | before | after |
|---|---|---|
| relative | `40, 2584.6, 5` | `40, 2584.6, 5` — unchanged |
| absolute | `0, 40, 2624.6, 2629.6` counted from the batten | counted from the frame instead |

So the zero can be put where it belongs without disturbing anything else, and
`recreate_dimension` with the points in the wanted order is how.

Checked on the sheet afterwards: the frame edge prints `0` and the batten's
overhang below it prints `40` — **unsigned**. Tekla writes the distance from the
zero, not a signed offset, so a point on the far side of the zero does not appear
as a negative number. Do not expect a minus sign to reveal which side a point is
on; only the geometry says that.

### Plants may require both rows

From the user: relative and absolute can be shown together, and some plants require
it. The cases show the change being made by hand: `before` states hold one or two
`Absolute` chains, and after a person passes over them those chains are
`RelativeAndAbsolute`. On `IW.4` two horizontal chains were converted that way and
the assistant never noticed, because it compared only points.

`recreate_dimension` copies the attributes of the chain it replaces, so the type
survives an edit. Changing it is a separate act.

**When comparing two states, compare types as well as points.** A change of type is
a real change to the drawing and is invisible to a point-by-point diff.

### Reported lengths are spans, not the printed run

`get_drawing_dimensions` / `get_dimension_contexts` return `LengthList` computed by
us, not read from Tekla. Entries are distances measured **from the first point of
the array**, projected onto the dimension axis.

The sheet prints distances from the **start of the reference line**. Same numbers,
often in the opposite order. See `ROADMAP_DIMENSIONS.md`, "Known Gap: LengthList
Does Not Reproduce the Printed Run".

**Never diagnose a drawing by reading these as the printed values.** To see what is
actually printed, export to PDF and read it. `RealLengthList` is the straight-line
point-to-point distance and is a different thing again.

### Point order on read is normalised, on create it is not

Tekla returns points top-to-bottom and right-to-left no matter how the chain was
built. The order **passed to create** decides the start point — the zero its
absolute run counts from.

For walls the convention is bottom-to-top and left-to-right, so pass points that
way. Do not check the result by reading it back: the read order never shows what
the sheet does.

### Editing renumbers the set

`recreate_dimension` deletes and recreates: use `newDimensionId`.
`add_dimension_points` merges: use `mergedDimensionId`.
Automatic re-dimensioning renumbers nearly everything.

Any id captured before an edit is stale. Match chains between states by point
geometry, never by id.

### The offset side comes from the vector, not the sign

`direction` picks the axis **and** the side: `horizontal` / `horizontal-down`,
`vertical` / `vertical-left`, or an explicit `dx,dy,dz`. It is required — there is
no default, because guessing rebuilds a vertical chain as a horizontal one.

Do not flip the side with a negative distance. Measured: the same points with
`(-1,0,0)` and distance `+200` put the line exactly at `X=-200`, while `(1,0,0)`
with `-200` produced a set that did not survive.

Attributes copied from the original carry their own offset, which Tekla adds on
top, so the line can land elsewhere than asked. Check the result and settle it with
`move_dimension`, which changes `Distance` in place and is exact.

## Procedure

Run the checklist above as part of step 1, not as a separate pass.

1. **Read the drawing.** `get_drawing_context`, `get_drawing_views`,
   `get_dimension_contexts <viewId>`, `get_all_parts_geometry_in_view <viewId>`.

2. **Look for a matching example.** Cases live in
   `cases/dimension_cases/assembly/<guid>/`, each with a `meta.json` saying what
   state is what and what changed. Roughly a dozen exist, all from 2026-08-01.
   Several hold a `before` (as Tekla dimensioned it) and an `after` (as a person
   left it); a few also hold an assistant's attempt and the human correction of it,
   which is the most informative kind — the misses are recorded there deliberately.

   They are all walls from one house and one plant. If the drawing at hand is a
   roof panel, another section, or another factory, say the examples may not
   transfer rather than applying them as if they did.

3. **Work out what each chain is for.** A chain exists to locate one family of
   parts. Group the points by the part they land on: the family covered *entirely*
   is the subject. Points from other families are incidental snaps.

4. **Propose before applying.** Show which points go and what the chain will read.
   Removing a needed dimension is far worse than leaving a redundant one — the
   drawing just looks denser, and nobody loses information.

5. **Verify against the parts, not the numbers.** After editing, check what each
   point actually landed on by comparing coordinates with part bounding boxes.
   `Ambiguous` in the point associations is a real signal, not a footnote.

## Who the drawing is for

From the user, 2026-08-01, and it is the frame everything else hangs on:

> It is all very simple and determined by how people — people specifically — are
> going to assemble it.

The drawing is an instruction to a person at a table with a tape measure. Every
rule in this file is a consequence of that, and none of them is a drafting
convention for its own sake:

| rule | because |
|---|---|
| points on faces, not axes | the tape's hook needs an edge to catch |
| one face per family | the tape runs down one side |
| chains print a running total | the hook stays on the first beam |
| a wall reads from the bottom | that is where the tape is hooked |
| a point on the face bounding an opening | the fitter needs the clear size |
| no span restating a part's own size | the part is already made |
| the control diagonal untouched | it is checked on the table, in the moment |

The user put the boundary sharply: **a robot would not need the drawing at all.**
It reads the model. Dimensions exist only because a human is doing the assembly,
so every rule here is an interface rule, not a geometric one. Nothing on the sheet
is required by the geometry — it is required by the reading.

That settles what "correct" means. A dimensioning is not correct because it is
complete or unambiguous in some formal sense; it is correct when the person at the
table can build from it without working anything out. Completeness that nobody
reads is the clutter the governing principle warns about.

When a question about dimensioning has no obvious answer, ask what the person
building the panel would do with the number. That has resolved every case here.

## The governing principle

From the user, 2026-08-01:

> **The fewer dimensions on the drawing the better — provided everything is
> clear. Like good code.**

Every rule below is a way of serving this, not an end in itself. The analogy is
exact and worth keeping: the goal is not the fewest lines, it is the fewest that
still say everything. A dimension removed at the cost of a reader having to work
something out is not a saving, and a dimension kept "just in case" is clutter that
makes the rest harder to read.

**The method is not cutting until it looks sparse.** It is asking what the reader
already knows from somewhere else, and not repeating that. For a frame drawing the
part sizes are on the fabrication drawings, so only positions are stated. For a
sheathing drawing the frame is already built and the sheet is a bought size, so
only the sheet's position on the frame is stated. Remove what is known elsewhere
and what remains is the minimum — arrived at by reasoning, not by trimming.

**And the payoff is not tidiness.** From the user: when everything is simple it is
harder for a person to make a mistake, and they work faster and get it right. Each
extra dimension is another thing to misread, another number that can be taken for
the one beside it. Thinning a chain is a safety measure and a speed measure, which
is why it is worth the effort of getting right — and equally why removing
something a fitter needs is far worse than leaving one span too many.

Two practical readings:

- **When two layouts are both correct, prefer the shorter one.** That is why the
  user's second variant, with five points up top instead of nine, was offered as
  equally valid — and it is the one to imitate.
- **"Everything is clear" is the binding constraint, not a caveat.** Thinning
  stops the moment something a fitter needs stops being readable.

## The measured rules

Everything in the next section is inference. These two were measured, on
before/after pairs captured 2026-08-01: `IW.1` view 1214 and `IW.2` view 1211,
both walls. On the second pair the first rule was written down as a prediction
*before* the person edited the drawing, and then scored.

### 1. A span equal to a part's own size

> **If two points of a chain land on the same part and the span between them is
> that part's own size, the second point is redundant.**

First pair: a person removed six points from three chains. Exactly five segments
disappeared, every one 60.0 mm and none of any other length. Every removed point
was the second face of a part whose first face was kept. The span restated the
part's own section, which its fabrication drawing already gives.

Second pair, predicted in advance: three points formed 60 mm spans, and **all
three were removed**. The rule names the right points.

**Exception on layers laid over a finished frame.** On the batten zone of `EW.3-6`
the vertical chain printed `40, 2624.6, 2629.6`, and the middle value is the
batten's own length. Rule 1 would strike it; it stays, and the user confirmed the
dimensioning as correct. The reason is that on such a layer the part's own size
doubles as its positioning: those three numbers say the batten hangs 40 below the
frame and stops 5 short of its top. Drop the middle one and the top gap is gone.

So rule 1 is about a frame drawing, where a part's size is on its fabrication
drawing and nothing else needs it. Where a layer is positioned against something
already built, a span equal to a part's own size may be carrying the fit.

**60 mm is not a threshold.** Every part in both views is `60X100`; the number is
the profile width and will differ on other drawings. A length cutoff would have
worked twice by accident and broken on the first part of another section. Compare
anchors, not lengths: both points resolve to the same `modelObjectId` and the span
equals that part's own extent.

### 2. A chain has a subject, and an overall chain keeps only its ends

> **If a chain's subject is the overall size of the assembly, only its two extreme
> points survive. Everything between them goes, however honestly it sits on a
> part.**

This is where the prediction failed and the failure was the useful part. Five
points were removed, not the three predicted. The two extra came from one vertical
chain, which went from four points to two: the bottom of the bottom plate and the
top of the raked plate at that section. Both discarded points sat on real faces of
real parts — rule 1 had nothing against them.

So rule 1 is necessary and not sufficient. Before applying it, establish what the
chain is *for*. A chain of positions and a chain measuring the whole assembly are
thinned by different rules.

### 3. Stud spacing tells you where the openings are

From the user, 2026-08-01, and confirmed on the first drawing it was tried against:

> **Studs in a timber panel normally run at 600–625 mm.** The number comes from
> the material, not from the design: OSB sheet widths and insulation roll widths.
> **A gap noticeably larger than that is a signal — an opening. A door, a window.**

**The spacing comes from the sheet, but not by a single formula.** From the user:
with a 1200 sheet the studs go at 600 so the OSB can simply be nailed on. With a
1250 sheet you may still set 600 — because of the insulation — and cut 50 mm off
the sheet; or you may set 625 and cut nothing. Both are done.

So several materials pull on the same number and which wins is a decision, not a
deduction. **600 is the classic spacing, the one to expect by default**, and there
is a reason it wins:

- plasterboard is **1200** and lands exactly on a 600 rhythm — studs at 600, 1200,
  1800, a joint on every second one;
- OSB is **1250** and does not. On a 600 rhythm it has to be trimmed to 1200 — the
  50 mm the user mentioned. The trim is not tidiness, it is the condition for the
  joint to land on a stud at all;
- at 625 the reverse: OSB fits untouched, but 1200 plasterboard joints then fall
  between studs.

So 600 is the compromise that serves **both** faces of the wall, inside and out.
625 is for a panel with one sheet layer and that layer OSB.

How often: the user puts 600 at roughly **80%**, offhand and unmeasured. Treat it
as the sensible expectation, not as a figure to compute with. Their own summary is
that it depends on several things at once — so when a drawing shows something else,
look for the reason rather than assuming an error.

**Do not read the sheet size back from the stud spacing.** A 600 rhythm does not
mean a 1200 sheet — it may be a 1250 sheet trimmed. The inference only runs one
way, and even then loosely.

What does hold: every second stud carries a sheet joint, whatever the number turns
out to be. See rule 10 for why that is compulsory rather than convenient.

On `IW.3`, a wall containing a door, exactly one gap between full-height studs
exceeded 625 mm: 945 mm, and that gap is the door. Every other gap — 60, 120,
170, 310, 475 — was smaller.

This is the first thing found that helps recognise a chain's **subject**, which
rule 2 needs and does not supply. It also gives the opening a definition that does
not depend on reading part names.

Two limits seen immediately:

- **Short studs above an opening do not follow it.** The cripples over that door
  sit at 412.5 mm. The rhythm belongs to full-height studs; anything sitting on a
  header is a different population.
- **A doubled stud is one position, not two.** Faces 60 mm apart are the same
  jamb, and the wall above has several. Group before measuring the rhythm, or the
  spacing reads as 60.

### 4. On a wall, vertical chains start at the bottom

From the user, 2026-08-01, correcting a rule that had been guessed:

> **Dimension a wall from the bottom.** Start the chain at the underside of the
> bottom plate — the assembly's lowest edge — and the overall height reads off the
> chain directly.

This is not a habit, it is a requirement on **point order**. A dimension prints
distances from the start of its reference line, and that start is fixed by the
order the points are passed to `create_dimension` — see the trap above. Starting
from the bottom makes the printed values read as heights above the base, which is
what a fitter needs, and puts the overall height at the end of the run instead of
requiring a separate dimension.

Measured on `IW.3`: every vertical chain in the corrected state begins at
`-1695.4`, the underside of the bottom plate, not at `-1635.4`, its top.

**This replaces an earlier claim** that a door's clear height is measured from the
*top* of the bottom plate. That claim came from the deleted cases, was inferred
rather than measured, and the data contradicts it.

### 5. At an opening, the point goes on the face that bounds it

Measured on `IW.4`, where a person corrected the assistant's own edit. Three
independent instances in one drawing, all agreeing:

> **Where a part borders an opening, the dimension point sits on the face facing
> the opening, not on the far face of the same part.** The jamb, not the outer
> edge of the stud. The underside of the header, not its top.

- stud `T-333` spans 104.5–164.5; the point moved from 104.5 to **164.5**, the jamb;
- header `T-44` spans y 410.5–470.5; the point moved from 470.5 to **410.5**, the underside;
- stud `T-141` spans 989.5–1049.5; the point moved from 989.5 to **1049.5**, the jamb.

**The reason is that the opening's size has to be readable straight off the
chain** — from the user: you want to just see the door width, and the height too.
On the corrected drawing both are printed with no arithmetic:

- **width 885** in the bottom horizontal chain, between the two jamb faces;
- **height 2250** in both verticals, from the base of the assembly to the header
  underside. Note it is measured from the base, per rule 4, not from the top of
  the bottom plate — the plate runs through the opening here.

Put the points on the far faces instead and nobody gets either number without
adding and subtracting stud thicknesses, while the values that *are* printed
measure nothing anyone needs.

**Anchor to the framing members.** The point belongs to the stud or beam that
frames the opening — the jamb stud, the header — not to the void between them and
not to some other part that happens to pass nearby. That keeps the dimension
associated with a part that will actually be built and positioned, which is what
an assembly drawing is for.

So an opening is not merely a place where points happen to land: its width and
height are outputs the chain must deliver, anchored to the parts that form it.

**Check the output, not just the points.** After building the chains, take the
opening's clear width and height and look for them among the printed spans. If
they are not there, the job is not done — however correctly each individual point
sits.

This catches a failure the point-level rule cannot see: the points can all be on
the right faces and still land in *different* chains, so that neither 885 nor 2250
is ever printed. Machine-checkable, and worth checking, because the drawing looks
right until someone needs the number.

This refines "one face for the whole family": the family's face is the default, an
opening overrides it.

### 6. Measure to an edge, never to an axis

From the user, 2026-08-01, and it explains several rules at once:

> Picture the table. The beams are laid out, and you measure with a tape — not to
> some imagined centreline of a part, but to its edge. And between them lie the
> spacers.

**A timber beam has no centreline.** It is a construct that exists in the model
and nowhere on the table. There is nothing there to hook a tape on, so a dimension
to it cannot be checked by the person building the panel. An edge is a physical
face you can put the tape against.

The tape's hook is the whole mechanism. It catches on an edge; there is nothing
for it to catch on at a centreline, and nothing to hold it at a point in mid-air.
Whatever you dimension to has to be something that hook can sit against.

**And the layout is sequential.** Parts are laid out in order and measured off
from one end — the hook goes on the first beam and the tape is pulled along the
whole run from there, without being moved.

That fixes the chain's zero, and it is not arbitrary: it is the outer face of the
first beam, which on these panels is the edge of the assembly. Horizontally that
is x=0, the outer face of the end stud; vertically it is the underside of the
bottom plate, which is what rule 4 says. That is why the chains print a running
total rather than gaps: the numbers on the sheet are the positions you mark, one
after another, without moving the tape or adding anything up. The printing style
is not a drafting preference — it matches how the panel is actually set out.

This is the reason behind three things that were previously just rules:

- **why points sit on faces** rather than on the part's axis;
- **why the same face is used across a family** — the tape runs down one side;
- **why the gap between two studs matters**: it is the length of the spacer that
  goes in there. Dimension the faces and the fitter reads the piece he has to cut.

Consequence for the candidate layer: `AxisStart` and `AxisEnd` are correctly
ranked `ReferenceGeometry`, below face and vertex candidates. Now the reason is
clear — they are geometrically real and physically unmeasurable. A planner should
prefer a face candidate whenever one exists, and treat an axis point as a last
resort worth flagging, not as an equal alternative.

Untested: whether this holds outside timber. In steel a centreline can be a real
thing on the shop floor — a bolt gauge, a marked axis — so the ranking may not
transfer. Everything above was derived on timber panels only.

### 7. A chain goes on the side its subject is on

From the user, 2026-08-01:

> Above or below is a matter of taste for horizontal chains — but if the part is
> at the top, then in cases like that always at the top, so that it looks alike.

Two halves, and they must not be confused.

**Free.** Which side a chain goes on, considered alone. Either can be right.

**Not free.** Once the chain's subject sits on one side, the chain goes on that
side — and the same choice is then made the same way on every drawing. The point
is not the individual sheet, it is that a stack of them reads alike: a person
who has learned where to look on one drawing finds it in the same place on the
next.

So the question is never "top or bottom?" in the abstract. It is "where is what
this chain locates?", answered once and then held to.

**This is the rule for a single horizontal chain.** When one is enough, its side
follows its subject. When two are needed they take opposite sides, and the same
question answers it again for each: each chain goes to the side its own subject is
on. `IW.5` came out that way — the stud rhythm at the bottom, where the bottom
plate and the studs' feet are, and the opening with its cripples at the top, where
the header and the short studs are.

So the rule does not decide *how many* chains there should be. It decides where
each one goes once you know what it carries.

This also gives a check on a chain you are about to move or rebuild: if its points
are up among the top parts, its line belongs above them, not below the wall on the
far side of everything it measures.

### 8. A composite post is marked by its `60X100` member

Measured on `IW.7`, a wall whose four posts are each a `60X100` stud paired with a
`45X120` board. The question was which face to mark — the stud's, the board's, or
the outside of the pair. The answer, from the corrected drawing:

> **The `60X100` member's face, on the family's chosen side. The board is not
> marked at all.**

The corrected horizontal chain prints three values for a wall with four posts:
`404.5`, `1054.5`, `1420` from a start at `0`.

- the two intermediate posts are marked at the stud's left face — `404.5` and
  `1054.5` — even though in one of them the board comes first and in the other the
  stud does;
- the two end posts are not marked at all: the assembly edges `0` and `1420`
  already locate them. Same as on `IW.5` and `IW.6`, where the last stud was left
  to the edge.

So a doubled post is one position, and the position is the structural member.

### 9. If one chain's values are contained in another's, it goes

The sharpest form of "one chain per direction and subject", and the mistake made
three times in one session — each time by keeping a chain that turned out to say
nothing new.

**The test is mechanical.** Write out what each chain prints — the running totals,
not the gaps. If every value of one chain also appears in another, the first
carries no information and should be deleted.

- `IW.8`: the left vertical printed `2430, 3008.4`, the right printed
  `2430, 2590, 3008.4`. The left is a subset. Deleted.
- `IW.7`: a second horizontal printed `60, 1240, 1420`, appearing to locate a
  noggin and a board, while the main chain already located the same posts by the
  stud faces. Deleted.
- `IW.6`: a bottom chain repeated the stud rhythm measured from the right faces
  instead of the left. Deleted.

Two things this catches that the point-level rules do not: a chain that measures
the same parts **from the other face**, and a chain that is a **coarser version**
of another. Neither shows up as a redundant point, because every point in them is
honest.

**Check it before proposing, not after.** All three were left in place by
judgement and removed by the user in the next breath.

**Exception: overall dimensions.** An overall width or height is contained in the
main chain by construction — the assembly's full span is that chain's last running
total. It is kept anyway. Its subject is different: it is the one number read at a
glance, on its own line, without following a chain. Rule 9 is about chains that
duplicate *work*, not about a value appearing twice.

Seen on `IW.9`: after deleting a redundant bottom chain, the overall width
surfaced printing `2467.5`, which is also the last value of the main chain. The
containment test flags it; it stays.

One consequence worth knowing: a chain fully contained in another may not even
appear in `get_dimension_contexts` — the read model folds it away. If the drawing
reports more dimensions than the context lists, the difference is a containment.
On both `IW.8` and `IW.9` deleting a redundant chain *revealed* an overall
dimension that had been hidden inside it, so a missing overall is worth looking
for before concluding the drawing lacks one.

### 10. A sheathing layer is dimensioned by its joints, and every joint sits on a frame member's centreline

Measured on `EW.3`, whose two zones are the same wall: zone 0 the timber frame,
zone 2 the OSB nailed to it. The sheet drawing dimensions **the sheets**, and the
frame does not appear in a single chain.

**Because the reader is a step later in the process.** From the user: by this point
the frame has been made and the sheet is being laid onto it. So the frame is not
something to measure out — it is already there, in front of the fitter, and it is
what everything else is measured *from*. That is why not one chain locates a stud,
and why the sheet's own size is never printed: the frame is built, the sheet is
bought, and the only open question is where one goes on the other.

Each layer has its own reader at its own moment. The frame drawing speaks to
someone laying out timber on a table; this one speaks to someone standing over a
finished frame with a board in his hands. Applying one layer's rules to another
misses that, and it is the first thing to check when a sheathing chain looks
strangely sparse.

> **The sheets are nailed to the frame. Both sheets meeting at a joint need wood
> to nail into — half the member each. So an internal joint lands on the
> centreline of a frame member, never between them.**

**And the reason is structural, not tidiness.** From the user: the sheathing works
as a diaphragm against shear — wind load on the panel is carried by the boards, not
by the frame alone. The nails transfer that shear from sheet into frame, so an edge
with nothing behind it does not merely look unfinished, it carries nothing. And the
sheets are laid **staggered** because a joint line running the full height or width
would be a line the diaphragm folds along.

This raises the stakes on the dimensions: a chain locating a sheet joint is
locating a structural decision. Joint positions are not to be tidied, rounded or
nudged for a neater number.

Verified on every joint of that wall:

- the vertical joint at `1238` is the centre of the stud spanning `1208..1268`;
- the left column's horizontal joint at `−925.2` is the centre of the noggin row
  at `−955.2..−895.2`;
- the right column's at `885.2` is the centre of the row at `855.2..915.2`.

**The direction of causation matters.** The frame is laid out to suit the sheet,
not the other way round. From the user: the noggin layout is set by the physical
size of the OSB or plywood sheet. The sheet is a bought item of fixed size; the
frame is cut to fit it. So when a frame drawing shows an odd stud position or a
noggin row at a strange height, the explanation is usually in the layer above.

Three things follow, and they explain features of the *frame* drawings that had
been recorded as bare facts:

- **stud spacing is half a sheet.** `2 × 625 = 1250` on this wall; a 1200 sheet
  gives 600 the same way. Every second stud carries a joint. The user gave the
  reason early — OSB and insulation widths — and this is the arithmetic behind it;
- **the noggin rows exist to back horizontal joints.** On nine wall drawings the
  noggins sat in two rows at different heights and nothing explained it. They back
  the joints; the joints are staggered so the diaphragm has no continuous fold
  line; therefore the rows must sit at different heights. Three facts, one cause;
- **you cannot move a stud for convenience.** Shift it and a sheet edge is left
  with nothing behind it.

**The chain is referenced to the frame, not to the sheet's own size.** On `EW.3-2`
both vertical chains start at the sheet's bottom edge and print `40` first: the
sheet hangs 40 mm below the frame. At the top there is no overhang — sheet and
frame finish together. So the first value is not a sheet dimension at all, it is
how far to drop the sheet against a frame that is already built. Sheet sizes are
known from the material; what has to be told is the position relative to the frame.

**The staggering is not in every direction.** On this wall the horizontal joints
are offset between the two columns (−925.2 against 885.2), while the single
vertical joint at 1238 runs the full height. So "no joints on one line" applies to
the joints there are more than one of; a lone vertical joint has nothing to stagger
against. Do not read the rule as forbidding every continuous seam.

**Battens obey a related but different rule — and the test is containment, not
centring.** Measured on `EW.3-6`, the batten zone. Only one of five battens sat on
a stud centreline; the others were off by 6 mm, and none of the spacings matched
the stud rhythm. That looks like freedom, and it is not.

Every batten lies **entirely within** a frame member:

| batten | behind it | slack |
|---|---|---|
| 60..105 | a 45×45 frame piece, exactly | none |
| 662.5..707.5 | stud 655..715 | 15 |
| 1221.5..1266.5 | stud 1220..1280 | 15 |
| 1846.5..1891.5 | stud 1845..1905 | 15 |
| 2383..2428 | a 45×45 frame piece, exactly | none |

The battens are nailed **through** the sheet layer and the nail must still reach
the frame. From the user, and it settles the whole question: *a batten may be
shifted a little, but you obviously cannot drive a nail into empty space* — and a
nail will not hold in plasterboard or OSB either. The sheet is something the nail
passes through, never what it grips. Only the frame holds.

That is why the assistant's first reading here — "the sheet supports the batten, so
no stud is needed" — was wrong at the root, even though the numbers it rested on
were real. The batten is 45 and the stud is 60, so there is 15 mm of play, and a
6 mm shift is harmless — from the user, a batten being slightly off is normal. Not
so at the ends, where the backing is a 45×45 piece the same width as the batten:
no slack, so the fit is exact. Those pieces appear to exist for that purpose.

So test **containment, not alignment**: every batten must sit inside the width of
some frame member. Checking centre against centre reports false errors, as it did
here on four battens out of five.

**Checkable mechanically**: for a sheathing layer, each internal joint coordinate
must equal a frame member's centreline. Do not run the same check on a batten
layer — it will report false errors. Nothing else in this file crosses between
layers; this rule does, so it needs both drawings of the same assembly.

Rule 6 still holds and is worth re-reading here: the point is on an **edge** — the
sheet's — which coincides with a member's centreline by design, not by choice. The
centreline is where the joint goes; the edge is what you measure to.

### A note on stopping

On the same drawing the assistant declined to touch a chain because it followed a
raked top — a case this file says to stop on. That chain also contained a phantom
point on a bounding-box corner, and the phantom had nothing to do with the rake.
Stopping on one ground silently blocked an unrelated fix the person then had to
make by hand.

**Judge each defect separately.** "Stop and ask" applies to the specific question
you cannot answer, not to everything else in the same chain.

### There is more than one correct answer

From the user, 2026-08-01, and demonstrated the same day: they produced two
different acceptable dimensionings of the same wall, and said plainly that neither
is the only truth and other variants are not wrong.

The two differed in **organisation**, not in point selection:

- variant 1: the stud rhythm repeated at the bottom, the middle cripple inserted
  into the top chain, nine points up top;
- variant 2: the same rhythm at the bottom, the top chain reduced to the opening
  and its cripple, five points up top.

Both used the same faces, the same thinning, the same reading direction.

Two consequences.

**Do not present one layout as the correct one.** Propose it as *a* correct one,
and say what the alternatives trade off.

**Do not grade by exact reproduction.** A plan that matches a reference point for
point is sufficient evidence of correctness, never necessary. Judging a proposal
means checking it against the rules and against what the sheet has to show — not
diffing it against one person's output. The roadmap's `2b` acceptance is written
as "reproduce the chain point for point"; that is too strong and needs revisiting.

Where the variants agreed is where the real constraints live: which points are
redundant, which face each point sits on, where the chain starts, and what the
sheet must show. Where they differed is where judgement is allowed.

### Some of it is factory convention, and cannot be derived at all

From the user, 2026-08-01:

> Some dimensioning variants are different simply because that is how the plant is
> used to doing it, or because of the production technology.

This splits the rules into two kinds that must not be mixed.

**Derivable from the drawing.** A span that only restates a part's own size is
redundant; a point belongs on the face bounding an opening; a wall reads from the
bottom; a chain prints a running total. These follow from geometry and from what a
fitter needs, and more drawings will sharpen them.

**Not derivable, ever.** How many chains, which one carries what, where they sit,
which of two acceptable layouts to prefer. These come from the plant's habit and
from how the panel is actually built. No amount of studying drawings yields them,
because they are not properties of the drawing — they are properties of the
factory that ordered it.

Three consequences.

- **Examples do not transfer between plants.** Every case captured here comes from
  one model and one set of habits. Treat the layout conventions in them as local,
  and say so rather than presenting them as general.
- **Convention belongs in configuration, not in inference.** A planner needs
  somewhere to be *told* the local convention. Trying to learn it from geometry
  produces confident nonsense.
- **When a layout question has no geometric answer, ask.** "Two chains or one" is
  frequently such a question, and guessing it is what went wrong twice today.

### What is still unproven

- Only two drawings, both interior walls, both `60X100`. Nothing here tests a
  different section or a roof panel.
- Whether a chain crossing a part along its **length** behaves like one crossing
  its width. Both views contain long members; neither case was decided by the
  human's edits.
- How the subject of a chain is recognised in the first place. Rule 2 says what to
  do once you know it, not how to know it.

### Two things that replicated

- **Every junction point was removed — six of six, across both drawings.** Where
  two parts met and both owned geometry at one place, the point went. A junction
  is a symptom of a redundant point, not a choice to adjudicate.
- **Automatic dimensioning plants the same phantom point.** In both `before`
  states a chain ended on the bounding-box corner of the raked top plate, at
  identical coordinates, where no part exists. The person removed it both times;
  on the second drawing they replaced it with the plate's real vertex. This is
  what `fallbackOnly` in `get_dimension_chain_coverage` detects.

**Anchor coverage sees none of the redundancy.** Every removed point in both pairs
was `matched`, on the associated part, with complete solid geometry. Checking that
a point is anchored to something real says nothing about whether it should exist.

## Rules (provisional)

- **One point per grid position, not per part.** Two parts butted together share a
  position. What the chain carries is the stud rhythm, not an inventory. This is
  the same observation as the measured rule above, stated before it was measured —
  prefer the measured form.
- **The point sits on the part being located**, even when that pulls it off the
  chain's line. Round numbers are secondary — a chain reaching an inset noggin was
  corrected back to the noggin by hand.
- **One face for the whole family.** Bottom faces of the horizontal members on roof
  panels; left faces of the studs on walls.
- **One chain per direction and subject.** Duplicates go entirely. Overlapping
  chains are fine when they serve different zones — two horizontals sharing 12 of 17
  positions were kept because the extra parts live at different heights.
- **Openings get their own points.** Door and window jambs are dimensioned because
  they are openings, not because parts sit there. Measured on `IW.3`: both jambs
  appear in *both* rebuilt horizontal chains, and the header underside appears in
  two verticals. An opening is dimensioned deliberately and more than once.
- **Exclude by prefix**: `T` timber is structural, `R` insulation and `M` fittings
  are not. `materialType` says the same thing (5 timber, 6 misc, 1 steel).
- Overall dimensions are never touched — their subject is the whole assembly.
- **The control diagonal is not a dimension of position at all.** From the user:
  it exists so the assembly's geometry can be checked simply and quickly — **on
  the assembly table, while the panel is being built**. Take a tape, measure the
  diagonal, and you know on the spot that the frame is true. It answers a
  different question from every other chain on the sheet, and it answers it to a
  different reader at a different moment, so none of the thinning rules apply and
  it is never touched.

  That is also why it has to be *quick*: it is an in-process check, not something
  read afterwards. A value that needs looking up or adding together would not be
  used.

  That also explains why it usually comes in pairs: two diagonals that agree mean
  the frame is square. Treat a lone diagonal as intentional rather than as half a
  pair — `IW.5` has one while `IW.1`, `IW.2` and `IW.4` have two.

## Raked tops: EW.4-6, the first worked gable

Drawing `[EW.4 - 6]`, guid `5cf600c9-…`, view 1229. A gable frame with the batten
layer already laid over it, one slope rising left to right (0.2217), an opening
`512..1602`, and two raked members whose bounding boxes span the entire panel —
`T-733 45X195` reads `x 0..1880, y 1248.6..1728.8`. **Every bbox check on such a
drawing lies**, and that is what makes the case worth keeping.

The assistant ran the whole checklist before proposing, found four defects, acted on
one and a half, and talked itself out of the rest. The person then corrected three of
the six chains. Each correction reverses an excuse:

| what the assistant found | what it decided | what the person did |
|---|---|---|
| `(0, 1728.8)` phantom in the *horizontal* top chain | left it — "the chain prints x, the number is honest" | re-anchored to `(0, 1312.0)`, the panel's real top-left corner. Printed values unchanged. |
| `268.7 → 313.7 = 45`, the batten's own thickness | left it — "exception to rule 1 for an overlay layer" | removed it |
| a point 960 mm across from its own chain | left it — "the panel is wide, the batten tops must show somewhere" | removed it |
| overall height `3535.1` sits on two bbox corners | replaced it with `3110.4`, the left edge, and asked whether gables carry an overall at all | restored `3535.1` on `(15, −1806.3)` → `(1880, 1728.8)` |

The last row is the substantive lesson. The assistant reasoned: *the lowest real
point is at `x 15..60`, the highest at `x 1880`, therefore `3535.1` exists as a
vertical on no part, therefore it is a bounding-box artefact.* The premise is sound
and the conclusion does not follow. **Tekla measures the projection between a
chain's points, so the two ends of a vertical need not share an x.** An assembly
overall runs from the lowest real material to the highest, wherever each happens to
sit. "No single part is that tall" says nothing about an overall — it is the test
for rule 1, applied where it does not belong.

What the assistant got right and the person kept: the zero on the frame with the
40 mm overhang stepped down from it, on the left vertical chain.

Two further readings from the corrected state:

- The left chain's opening point moved from `(527, 313.7)` — the batten's face and
  top — to `(512, 268.7)`, the jamb and the header underside. The frame bounds the
  opening even on a batten drawing.
- `1304.1` and `1312.0` were both kept, 7.9 mm apart: the batten top and the panel
  edge above it. Closeness is not redundancy when the two belong to different layers.

**Coverage caught what the hand-rolled checks missed.** The assistant's own script
tested each point against part bounding boxes and reported the width-overall's two
points as "on nothing", then dismissed them. `get_dimension_chain_coverage` returned
`Missing` — no candidate at all, not even a box corner — a class the bbox test cannot
name. Coverage belongs in the mandatory checklist, not in the doubt path.

## Stop and ask

- **Unfamiliar assembly type.** The rules are from roof panels and walls. On a
  truss, a beam or a plate with cuts, the subject of a chain has to be worked out
  from scratch.
- **Raked or gable tops**, beyond what EW.4-6 settled above. Stud ends sit at
  different heights, and a chain following them cannot show the spacing. Placing it
  at a constant height instead is a guess that has been wrong before.
- **A point on a junction of several parts.** On a wall almost every level is a
  junction — a stud always meets the plate it stands on — so strict uniqueness is
  unachievable and cannot be used as a filter. But a point where several parts each
  end is meaningless: the value says "to what, exactly?".
- **Anything the cases do not cover.** Say so instead of inventing a rule.

## Capturing a case

Snapshots go to `cases/dimension_cases/assembly/<drawing-guid>/`, which is
gitignored — these are real production drawings. The folder is currently empty.

```bash
cd "C:/TeklaStructures/2025.0/Environments/common/extensions/svMCP"
./TeklaBridge.exe get_dimension_contexts <viewId>            > .../dimension_contexts.json
./TeklaBridge.exe get_all_parts_geometry_in_view <viewId>    > .../parts_geometry.json
./TeklaBridge.exe get_part_candidate_points_in_view <viewId> <modelId> > .../candidates_<modelId>.json
./TeklaBridge.exe get_dimension_chain_coverage <viewId> <dimensionId>  > .../coverage_<dimensionId>.json
```

Capture **before and after** a person edits the dimensions. The difference is
where the rules come from, and with coverage that difference is now readable in
anchor keys rather than in raw coordinates — which point was dropped, and what it
had been sitting on.

Two things the last set of cases got wrong, worth avoiding:

- **A drawing that merely happened to be open is not a reference.** Record in
  `meta.json` whether a human actually passed over the chains, and what they
  changed. Without that the snapshot proves nothing.
- **States that were not saved cannot be re-captured.** Three of the six previous
  cases had `before` states that no longer existed anywhere else, because the
  drawing had been edited and saved over. Capture `before` first, and check it
  landed on disk, before touching anything.

Calling the bridge directly avoids the MCP timeout on slow commands and is how all
of this was verified.
