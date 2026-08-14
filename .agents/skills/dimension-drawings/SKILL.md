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
and ask of every adjacent pair whether the span merely restates one part's own
size. This must be decided before writing: verification only compares the drawing
with the plan, so it cannot find a redundant span the plan itself selected.

This comparison is mechanical only when the two positions have a common non-null
`modelId` support and that support's `partExtentAlongChain` equals their gap within
the response's `partSpanMatchToleranceMm`. The field is present only for a part
whose whole projected contour is axis-aligned. It is `null` for a raked part,
whose bbox would invent a span through empty space, and equally for one whose
shapes yielded no usable extent — it is fail-closed, so its absence names no
cause. A support on `structural-boundary` has neither field and never qualifies.
Remove one endpoint for a confirmed part-size span. A null `partExtentAlongChain`
is not a confirmation of anything — the field is fail-closed, so it withdraws the
test rather than answering it, and that pair stays a judgement.
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
