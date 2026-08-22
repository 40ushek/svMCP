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

`log_skill_event` has no MCP tool; call `TeklaBridge.exe` directly from the
Tekla extension folder for this one. Every other bridge command named in this
skill has an MCP tool of the same name — use it when one is available, and
fall back to the bridge only in a session that has none. Every bridge command
is logged separately either way; do not duplicate those log lines manually.

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

**First pick the rule set, from the subject.** Read the parts with
`get_drawing_parts` and decide once for the drawing:

| Subject | Rule set |
|---|---|
| Timber panel or wall — framing under prefixes `T`/`GLB`, sheathing `S` | `references/plant-rules.md` |
| Steel assembly — a main member with plates and gussets fixed to it | `references/steel-rules.md` |

Neither fits the drawing, or both seem to: stop and say so. Do not blend the two
— on a part's own size they say opposite things, and a blend is not a compromise
but an unrecorded third rule set. State the chosen set in the final response.

For each requested base view:

1. Read drawing context and confirm `AssemblyDrawing`.
2. Run `get_structural_chain_positions <viewId>`, passing the rule set's
   exclusions. **Every part the view draws takes part unless you exclude it.**
   Nothing is filtered in code: a mark prefix and a material name are the
   plant's own convention in the plant's own language, so the rule set names
   what to leave out and the call carries it:

   ```
   get_structural_chain_positions <viewId> <excludePrefixes> <excludeMaterials>
   ```

   Both lists are comma-separated and both may be empty. The response echoes
   `exclusions`, so a chain that came out short is answered by reading them
   first. `mainPartModelIds` names the assembly's main part - the base a
   secondary part is measured from on a beam or a column, and nothing at all on
   a panel of many equal members.
3. `isComplete=false` stops automatic placement for that geometry group. Report
   its issues and continue independent complete views and groups.

   Three things make it false, and they want different fixes:

   - **a part whose properties could not be read** (`role:<id>`). It is measured
     over like any other, but an exclusion that should have caught it could not
     fire, so nothing in the set is a fact. Read the parts with
     `get_drawing_parts` and report what came back.
   - **every part excluded** (`structural`). The filter is wrong for this
     drawing, not the drawing. Say which exclusions were passed.
   - **an outline error or a part the view does not draw** (`outline:<id>`).

   An unfamiliar mark prefix is none of these and blocks nothing. That was the
   old failure: a prefix table in the code answered one timber model and turned
   every part of the next assembly into a blocker - nine parts of one steel
   column, all prefixed `P`, produced no geometry at all.
4. Read existing dimensions with `get_drawing_dimensions <viewId>`.
5. Use `draw_structural_chain_positions <viewId> <side>` when the visual side
   or a raked edge needs confirmation.

`get_structural_chain_positions` is the coordinate source. Do not rebuild its
positions from bounding boxes, axes, raw solid vertices, or contacts. Contacts
can justify a retained position as a joint; they do not add coordinates.

**The reference body.** Every assembly has one body that everything else is
located against, and every chain closes on that body's extent along the chain's
axis. Which body it is comes from the rule set - the main part on steel, the
frame on a timber panel - and its ends are the extreme positions whose supports
carry one of its `modelId`s. That is derived from the response and from nothing
else: the group extent covers every included part and is a different number.

A member and a panel differ in shape, not in rule. A column is long, so one
chain runs along it and the other sides carry little; a panel is a plane, so all
four sides carry work.

**Which end is start.** Two different questions, two different sources - do not
answer either by guessing from a dimension's JSON point order.

- **A chain this skill is creating.** Tekla's own zero for a running dimension is
  the reference body's own model `StartPoint` (already read by
  `get_part_geometry_in_view`/`get_all_parts_geometry_in_view`), unless the plan
  states the plant reverses it - Tekla calls that flag `Reversed direction for
  running dimensions`. Read the `StartPoint`, do not infer an end from the
  drawing.
- **A chain already on the sheet**, when matching or recreating its convention.
  Here the same rule as marks in `AGENTS.md` applies: JSON point order does not
  confirm the true start, because Tekla normalizes it for display. Recover it
  only from a single, connected segment path via `Segment.StartPoint`/
  `EndPoint`. A branched, broken, or otherwise ambiguous chain has no
  recoverable start - mark it unverified and do not build an absolute row, or a
  "counts from" claim, on top of it.

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

All other keep/remove choices remain drawing judgement under the rule set chosen
in step 1 — [`references/plant-rules.md`](references/plant-rules.md) for timber,
[`references/steel-rules.md`](references/steel-rules.md) for steel.

The three checks above are written from the timber rules. On steel, check 1 is
**inverted**: a span restating a part's own size is often the dimension the sheet
exists for. Run the walk all the same, but what decides each pair depends on the
plant's `Internal` policy, which the steel file requires you to have asked for:

| `Internal` | How the walk decides a secondary part's internal position |
|---|---|
| `None` | `Removed`, every one of them, reason `Internal=None`. The checks do not run |
| `Necessary` | Keep what the shape cannot tell, drop what it can. Needs `recognizableDistance`; without it, stop |
| `All` | `Kept`, every admissible one. The "Necessary" principle is **not** applied as a filter here; only the position rules that hold under every policy do — near-duplicates are one position, and clutter control still applies |

Applying `Necessary` under an answer of `All` deletes numbers the plant asks for, and
applying anything at all under `None` creates numbers it does not. The policy is
therefore part of the plan, not a detail of it: state it beside the plan and in the
final response.

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
recreated chain has been read back successfully. On steel, a section showing a
connection is a requested view in its own right, and a side may legitimately
resolve to "no chain" because that feature is dimensioned in section, as the
steel file allows. Do not substitute analysis,
an overlay, or a partial list of IDs for this gate.

Only these conditions may leave a side unresolved: incomplete structural
geometry, a Tekla write/read error, or a policy question not covered by the
references. State the exact side and blocker; do not claim completion.

## Final response

For `place`, return only:

- created, changed, and deleted dimension IDs;
- one short verification status for `Top`, `Bottom`, `Left`, and `Right`;
- the rule set used, and every setting the plan was built under - on steel that is
  `Internal`, the anchor, `recognizableDistance` when `Necessary` was chosen, closure,
  datum and row type, plus any check that could not be run;
- any unresolved side and exact blocker.

For `review`, return the requested findings only. Do not include an execution
essay unless the user asks for it.

## References

- [`references/plant-rules.md`](references/plant-rules.md): measured timber
  drawing rules, openings, layers, raked tops, and when to stop.
- [`references/steel-rules.md`](references/steel-rules.md): steel assemblies —
  one logic (everything measured from the main part), the settings where people
  legitimately differ, five arithmetic checks, and what is not covered.
- [`references/placement-and-verification.md`](references/placement-and-verification.md):
  Tekla point-order, direction, offsets, reflow, and read-back traps.
- [`HISTORY.md`](HISTORY.md): historical experiments only; never treat it as a
  required execution checklist.
