---
name: dimension-drawings
description: Place, repair, clean up, or review dimensions on a Tekla AssemblyDrawing in this project. Use whenever a request touches drawing dimensions, including a request to dimension an active assembly drawing completely.
---

# Tekla Assembly Dimensioning

Use this skill only for `AssemblyDrawing`. Confirm the drawing type before any
write. Do not change project code, role rules, roadmaps, or this skill while
dimensioning a drawing unless the user explicitly asks for that separate work.

## Choose one mode

| User intent | Mode | Writes dimensions? |
|---|---|---|
| Review, inspect, compare, explain | `review` | No |
| Place, fix, recreate, or dimension completely | `place` | Yes |

Do not start in `review` and call it a completed `place` task. If the user says
"fully", "completely", or asks to place dimensions, use `place`.

## Progress log

Before the first bridge read, start a run. Log each transition between reading,
planning, applying, and verifying. Finish once.

```powershell
TeklaBridge.exe log_skill_event start dimension-drawings <runId>
TeklaBridge.exe log_skill_event task dimension-drawings <runId> "<step>"
TeklaBridge.exe log_skill_event finish dimension-drawings <runId> "<summary>"
```

Call `TeklaBridge.exe` directly from the Tekla extension folder. Every bridge
command is logged separately; do not duplicate those log lines manually.

- **Mark before you act, not after you learn.** The line goes out when you decide
  to do something, describing what you are about to do. A mark written after a
  check has already told you what is wrong records a reaction, not a step, and
  the interval before it holds work nobody logged.
- **Name a failed command in your own mark.** `bridge-exec` records the
  exception, but the step mark stays silent and the step reads as clean. If a
  call errors, say so in the next `task`.

## Review mode

1. Read the drawing context and requested view dimensions.
2. Use `get_dimension_contexts` only when a particular anchor needs checking.
3. Use `get_dimension_chain_coverage` only for that specific anchor question.
4. Report facts; do not create, move, delete, or recreate a dimension.

When a case snapshot exists, it is evidence for review, not a prerequisite for
placing dimensions. Do not let a missing case block a `place` task.

## Place mode — mandatory loop

The LLM chooses the dimensions. The bridge only reads, creates, and verifies
the LLM's decisions.

### 1. Read the facts

For each requested base view:

1. Read drawing context and confirm `AssemblyDrawing`.
2. Run `get_structural_chain_positions <viewId>`.
3. `isComplete=false` stops automatic placement for that geometry group. Report
   its issues and continue independent complete views and groups.

   **`Unknown` is not `Ignored`.** It means the role was never established, so
   the part may still turn out to be `Defining` — and a `Defining` part moves
   not only the overall but every interior position on whichever side it
   touches. Until the role is settled nothing in the set is a fact, so there is
   no safe subset to place "meanwhile".

   Identify the parts with `get_drawing_parts <viewId>` and report them by
   `partPos`, type, and material, so the role can be added outside this run.
   That reading is evidence for settling the role; it does not settle it, and
   it does not license placement.

   Placing anyway takes an explicit instruction from the user naming the
   condition, for this drawing only. Something of this shape:

   > For this drawing treat every part with prefix=W as Ignored. If
   > `get_structural_chain_positions` is incomplete only because of those,
   > continue placing. Any other Unknown remains a blocker.

   Given one, do all four:

   - check that **every** `Unknown` falls under the stated condition, by reading
     the parts rather than assuming — the instruction grants what it names and
     nothing beside it;
   - continue only if none is left over; a single `Unknown` outside the condition
     stops the group as before;
   - leave the role rules in code alone. A per-drawing permission is not evidence
     for a rule, and a rule guessed from one drawing speaks for every future
     assembly — see the W entry in `PartRoleClassifier.DefaultRules`;
   - log it as its own `task` line, and say in the final response which parts
     were excluded and on whose instruction.

   This is the practical route. Inventing a universal prefix rule to unblock one
   drawing is the failure it avoids.
4. Read existing dimensions with `get_drawing_dimensions <viewId>`.
5. Use `draw_structural_chain_positions <viewId> <side>` when the visual side
   or a raked edge needs confirmation.

`get_structural_chain_positions` is the coordinate source. Do not rebuild its
positions from bounding boxes, axes, raw solid vertices, or contacts. Contacts
can justify a retained position as a joint; they do not add coordinates.

### 2. Decide all four sides before writing

Create an internal plan for `Top`, `Bottom`, `Left`, and `Right`.

For every calculated position, choose exactly one:

- `Kept` — give the drawing reason;
- `Removed` — give the drawing reason.

For every side, choose exactly one outcome:

- create or recreate a chain from its `Kept` positions;
- retain a verified existing chain that already expresses those positions;
- intentionally no chain, with a concrete reason.

Never silently drop a position. Never replace a multi-position chain with a
shorter one unless every removed position has a reason.

**Check the plan before it is written.** Walk each side's `Kept` list in order
and put every adjacent pair or cluster through all three checks below, not just
the first one a part-size flag happens to catch. This must be decided before
writing: verification only compares the drawing with the plan, so it cannot find
an error the plan itself selected — confirmed independently when a second run of
this skill, on a fresh session with no memory of this one, skipped checks 2 and 3
below and produced a plan neither caught afterward.

1. **A span merely restating one part's own size.** Mechanical only when the two
   positions have a common non-null `modelId` support and that support's
   `partExtentAlongChain` equals their gap within the response's
   `partSpanMatchToleranceMm`. The field is present only for a part whose whole
   projected contour is axis-aligned. It is `null` for a raked part, whose bbox
   would invent a span through empty space, and equally for one whose shapes
   yielded no usable extent — it is fail-closed, so its absence names no cause. A
   support on `structural-boundary` has neither field and never qualifies. Remove
   one endpoint for a confirmed span; a null `partExtentAlongChain` is not a
   confirmation of anything and that pair stays a judgement, decided under check 2.
2. **Two positions on the same regular family, where one number should carry
   both.** When adjacent positions sit a member-width apart on the same kind of
   stud or post, keep one face and remove the other — `plant-rules.md` rule 3
   says which face and why. This is exactly the case check 1 cannot settle
   mechanically, since the member's own `partExtentAlongChain` is frequently
   `null` on a plain stud too; the walk still has to ask it of every such pair.
   Measured on EWA.5: a plan that skipped this question kept both faces of every
   stud on the bottom chain (546.5 and 606.5, 1146.5 and 1206.5, and so on),
   doubling the row without adding a reading.
3. **More than one candidate position on a part with no `partExtentAlongChain`.**
   Rule 4a says such a part keeps a position because nothing else on the sheet
   supplies one; it does not say to keep every position the list offers for it.
   Where more than one appears, check `get_contact_candidate_points` before
   choosing — never delete down to one without checking.

   **The check can only confirm; it can never remove anything on its own.**
   Remove all candidates but one only when all three hold at once, readable
   straight off the response: `selectionComplete` is `true`; the shape's
   `contactKind` is `FaceToFace` and its `contactState` is `Touching`; and that
   contact's coordinate matches a calculated position exactly — the same
   conditions rule 4b states. Anything else the check can come back with - empty,
   `selectionComplete=false`, `contactKind` of `FaceToEdge`/`EdgeToEdge`,
   `contactState` of `Gap`/`Overlap`, a coordinate that does not land on a
   calculated position - proves nothing about the candidates
   and removes none of them: the choice stays a 4a judgement on the drawing, not
   an automatic deletion. A filtered call answers only for the named parts and
   is silent about everything outside that set, so its silence is never grounds
   to remove a position either. Measured on EWA.5: a plan that skipped this
   question entirely kept a diagonal brace's two landings and its joint with its
   pair, all three, instead of the one confirmed contact - the failure this
   check exists to catch is skipping the question, not necessarily reaching a
   confirmed answer every time it is asked.

   **Call it as `get_contact_candidate_points <viewId> <draw> <modelIds>`, named
   to the specific two or three parts in question — never with `modelIds` left
   off.** An unfiltered call on a real view answers in tens of kilobytes across
   every part the view draws, not the one pair being checked. Measured on EWA.5:
   a run that called it unfiltered spent most of its time trying to pull one
   contact out of that by hand, and the plan it produced that day mixed a
   coordinate from the brace's joint into an unrelated side's chain - the filter
   exists so this check is cheap enough to run every time it applies, not
   something to reach for only when the view is small.

   Before reading a result at all, check `selectionComplete`: `false` means some
   named id never entered the search — a typo or a stale id — and the read
   proves nothing either way. The command itself refuses a filter naming fewer
   than two distinct parts, for the same reason: with zero or one body there is
   no possible pair, so it would answer empty regardless of what that part
   actually touches.

All other keep/remove choices remain drawing judgement under
[`references/plant-rules.md`](references/plant-rules.md).

### 3. Apply the plan

Create or recreate only the planned `Kept` positions. Preserve an existing
chain when it already matches the plan. If replacing it, capture the returned
new ID: Tekla renumbers edited dimensions.

Choose the placement direction from the existing reference line or from the
requested side. The direction keyword sets the side; a negative distance does
not. Read [`references/placement-and-verification.md`](references/placement-and-verification.md)
before the first create/recreate in a run.

### 4. Verify and iterate

Immediately re-read `get_drawing_dimensions <viewId>` after every write that
can reflow neighbouring dimensions. Verify against the plan:

- every `Kept` coordinate is present;
- every `Removed` coordinate is absent for its stated reason;
- the reference line is on the planned side with a sane offset;
- relative/absolute rows match the intended type;
- the chain endpoints are real outline corners where required.

Correct mismatches and repeat the read-back. A successful create response is
not verification.

There is no dimension-defect auditor. Make selection mistakes visible in the plan
before the gate below instead.

### Completion gate

Do not answer that placement is complete until every requested view has a
resolved `Top`, `Bottom`, `Left`, and `Right` outcome and every created or
recreated chain has been read back successfully. Do not substitute analysis,
an overlay, or a partial list of IDs for this gate.

Only these conditions may leave a side unresolved: incomplete structural
geometry, a Tekla write/read error, or a policy question not covered by the
references. State the exact side and blocker; do not claim completion.

## Final response

For `place`, return only:

- created, changed, and deleted dimension IDs;
- one short verification status for `Top`, `Bottom`, `Left`, and `Right`;
- any unresolved side and exact blocker.

For `review`, return the requested findings only. Do not include an execution
essay unless the user asks for it.

## References

- [`references/plant-rules.md`](references/plant-rules.md): measured timber
  drawing rules, openings, layers, raked tops, and when to stop.
- [`references/placement-and-verification.md`](references/placement-and-verification.md):
  Tekla point-order, direction, offsets, reflow, and read-back traps.
- [`HISTORY.md`](HISTORY.md): historical experiments only; never treat it as a
  required execution checklist.
