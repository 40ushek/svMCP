---
name: dimension-drawings
description: Place, repair, clean up, or review dimensions on a Tekla AssemblyDrawing in this project, from one requested chain to a complete drawing. Do not treat code-only work as permission to change the drawing.
---

# Tekla Assembly Dimensioning

Use only for `AssemblyDrawing`; confirm the active drawing and target view before
writing. Code/skill review or editing is not a drawing-placement run. Do not
change code, rules or roadmaps during placement without a separate request.

## Scope and mode

- **Review / explain:** read only; no dimensions or debug objects.
- **Place / fix:** finish the requested changes, not just an analysis.
- **One dimension, side or part:** plan that scope; other sides are untouched,
  not implicitly approved, deleted or declared complete.
- **Whole view / complete drawing:** resolve `Top`, `Bottom`, `Left`, `Right`
  for every requested view. Include relevant steel connection sections.

Use the user's already stated policy, attributes and tolerances for the same
task. Ask missing settings together, once. Do not ask again unless the subject,
plant convention or request changes. Distinguish `recognizableDistance` from
`minDimensionLength`: an answer of 1 mm to the former does not change the latter.

## Efficient read route

Read only the relevant references, once per run; reuse them while unchanged.

| Subject / action | Read |
|---|---|
| Timber frame/panel | [plant-rules.md](references/plant-rules.md) |
| Steel member with plates/gussets | [steel-rules.md](references/steel-rules.md) |
| First drawing write of the run | [placement-and-verification.md](references/placement-and-verification.md) |
| Compact context response and snapshot refresh | [structured-plan.md](references/structured-plan.md) |

Do not blend timber and steel rules. If neither applies or the subject is
ambiguous, ask. Do not routinely read HISTORY, roadmaps, code or the internet
during placement; consult them only for an actual unresolved capability/rule.
A missing historical case does not block placement.

For each target view:

1. Establish active drawing identity/type and view identity/type. Use a current
   response already in the run if sufficient; do not fetch the same context
   under several command names. Report the human view label as well as its ID.
2. Read `get_drawing_parts` if needed to choose the subject/rule set or resolve
   unread roles. Reuse an existing current parts response.
3. When deployed, use `get_view_dimension_context` with `questions="points,edges,scale"`
   and the sides in scope. It shares the structural candidates and snapshot with
   `get_structural_chain_positions` and `create_dimension`. Use the latter for
   full evidence (`verbose=true`) or on an older bridge. Timber uses its rule-set
   exclusions; steel excludes nothing unless agreed otherwise. Check `exclusions`,
   `isComplete`, issues and main-part identity. Pass the SAME exclusion lists to
   `create_dimension`; empty lists mean no exclusions, not the last query's filters.
   For a measured timber wall elevation only, request its preliminary chain with
   `questions="chain", ruleSet="panel"`; the default remains `steel`. Do not use
   `panel` for roofs, floors or an unknown assembly type without an applicable rule.
4. Read `get_drawing_dimensions <viewId>` once to identify chains to retain or
   change. These and the chain positions are the normal planning inputs.

**Do not automatically add** structural outline, all solid geometry, contacts,
dimension contexts, coverage or debug overlays. Each extra read must answer a
specific outstanding question. For review, begin with context and dimensions;
fetch structural positions only if the question requires them.

The bridge treats source geometry as fixed while working on one view. Reuse it
for all sides. After external model/view edits or uncertain source identity,
request `refresh=true` on a context or chain-position read. Switching requested
view/drawing clears the snapshot; different filters resolve separate snapshots.
External edits are not automatically detected during the run.
A dimension-only write invalidates the dimension snapshot because of reflow;
it does not by itself require another full solid/outline read.

`isComplete=false` stops automatic placement for that group, not independent
complete groups. Report the actual issue: unread properties, no included parts,
or failed/missing outline geometry. An unfamiliar mark alone is not a blocker.
Depth selection is separate from prefix/material exclusions and is not proof
that full-solid projection equals the clipped section contour.

## Plan before writing

The LLM chooses dimensions; tools validate/read/write those choices. A write
that matches a bad plan does not make the plan good.

For each side **in scope**, record create, recreate, retain an already verified
chain, or intentionally no chain with a reason. For every candidate position
on a side being planned, record `Kept` or `Removed` and a short drawing reason.
Do not silently shorten an existing chain. For a full view, settle all four
sides before writing; for a narrow request, do not expand to other sides.

Keep one compact plan, not repeated prose copies:
- purpose: size, location or overall;
- reference body and measured subject, identified by model IDs;
- selected support at each kept position; reasons for removed positions;
- closure endpoints, first point/datum, side, offset, attributes and row type;
- chosen rule set and its policy settings.

**Coordinates:** structural candidates from `get_view_dimension_context` or
`get_structural_chain_positions` are the source. Use each
position's own real support point, including its own cross-axis coordinate.
Never reconstruct positions from bbox, raw vertices, axes or contacts.
Geometry reads may verify a support; they do not invent replacement coordinates.

**Reference is not closure.** The steel main part or timber frame is what
locates the subject. For longitudinal/main-frame chains, use its real ends as
the rule set requires. A transverse steel plate-location chain can close on
the plate's edges while retaining the main-profile edges between them. State
that choice. A bare plate width/height does not prove its location.

**Datum:** `points[0]` chooses the start of a new chain. For running dimensions,
follow the rule-set main-part-start convention or the explicitly agreed reverse;
read the main part's model `StartPoint` only when needed to establish that datum.
For an existing chain, recover start only from a unique connected
`Segment.StartPoint/EndPoint` path. Normalized JSON order is not evidence.
Ambiguous start is `unverified`; do not claim absolute values/counts-from.

**Policy:** apply the selected reference's checks, not timber's removal rules
to steel. Steel `Internal=None / Necessary / All` is explicit; `Necessary`
needs `recognizableDistance`. `All` is not filtered by `Necessary`.
Do not change the user's chosen row type just to fit a tool.

**Targeted contacts:** only when a keep/remove decision requires contact
evidence. Use `get_contact_candidate_points <viewId> false <modelIds>` with
the specific two or three parts, never an unfiltered search. Reuse the result
for the same pair/snapshot. `selectionComplete=false` proves no absence.
A contact can justify an existing candidate, never demand/add a coordinate.
For timber's reduction to one position, use the exact rule 4b gate.

**Visual checks:** no debug lines by default. Section/end views still require
the visual confirmation in steel-rules. Use the current matching view image;
an overlay is a drawing mutation, not a read. Only create one if genuinely
needed and authorized; track and remove only this run's own overlay objects.
Never clear unrelated/pre-existing annotations. Avoid a fixed-name overlay
command if its automatic group cleanup could erase an earlier overlay.

## Apply and verify

Read the placement reference before the first write. Preserve matching
dimensions. Direction chooses the side; distance is non-negative. Keep the
returned new ID when Tekla renumbers a chain.

Use available, known working tools; source code existing is not proof that the
running bridge contains it. The experimental structural preview/apply commands
were removed. Use `create_dimension`; omit `distance` for the automatic 8-paper-mm
gap, or supply `paperGapMm`. Do not supply both distance and paperGapMm.

After each write that can reflow neighbours, re-read
`get_drawing_dimensions <viewId>` and compare to the plan:
- kept coordinates present, removed ones absent from the planned chain;
- each witness point uses its own intended support;
- reference line on the correct side/offset: inspect `writeState.RenderedLine`
  when available. `matched` checks this chain's rendered offset; `not verified`
  needs the remaining visual check. Calculated `referenceLine` and stored-value
  `Verified=true` alone are not independent evidence (placement reference);
- intended relative/absolute rows and closure;
- affected neighbouring chains still correct.

Use this one read-back for all those checks. Do not repeat full model reads
unless it reveals a specific geometric doubt. A successful create response
alone is not verification; a verified-write response does not replace the
neighbour check.

On write error, keep all returned IDs and `writeState`; inspect the view before
retrying. If `writeState.newDimensionRemoved` is true the failed replacement is
gone and the original stands; otherwise an old and new dimension may both exist
(a failed cleanup says so). Never delete the old chain merely because a new ID
exists. Correct a demonstrated
mismatch, then verify again. If the same failure repeats without new evidence,
stop that operation and report the state; do not loop speculative writes.

Coverage is a targeted anchor diagnostic, not a mandatory call per dimension.
Its reliability is unresolved. If it fails, report that check and inspect
already-read geometry or make one targeted geometry read; do not retry the
same failing call across every chain. A failed diagnostic is not evidence that
the point is valid or invalid.

## Progress log

Only actual drawing review/placement runs need bridge logging; skill/code-only
work does not. Before the first bridge read:

```powershell
TeklaBridge.exe log_skill_event start dimension-drawings <runId>
TeklaBridge.exe log_skill_event task dimension-drawings <runId> "<next phase>"
TeklaBridge.exe log_skill_event finish dimension-drawings <runId> "<summary>"
```

Mark reading/planning/applying/verifying transitions **before** acting, not
every individual tool call; commands already log themselves. Name failures in
the next task mark; finish once. Use the extension-folder bridge for
`log_skill_event` (no MCP tool); use available MCP tools for other operations.

## Completion and response

A narrow request is complete when its requested dimensions are verified; state
that other sides were not reviewed. A full-view/drawing request is complete
only when every requested side has a verified chain or a justified no-chain
outcome. An overlay, preview or partial ID list is not completion.

For placement, report briefly:
- created/replaced/deleted IDs and verification for the requested scope;
- rule set and actual settings: on steel Internal, anchor, recognizableDistance
  if applicable, closure, datum and row type;
- unresolved side/check and exact blocker.

For review, answer the question with checked findings only. Do not claim live
verification from unit tests, historic examples or a successful write response.
