# Answer Size Roadmap

Created 2026-11-14, revised the same day after review.
Status: current refactor implemented, reviewed and tested. The bridge and API are
updated; the MCP server with the new tool schema is not rebuilt yet (details by
previewPlanId). One cold Haiku run on M.394: no write errors, `bolt-position-Bottom`
placed, but an eligible chain skipped by the model (open, see "Live run").

Current scope: shorten the skill, separate the stored write plan from its answer,
and remove unnecessary fields from the short answer. Preserve existing rounding
and write parameters. Step 5 is deferred and is not part of this refactor.

This file owns one question: **how much a model has to read to place dimensions**,
and how to cut it without changing a single placed dimension. The rules that choose
chains belong to [Dimensions](ROADMAP_DIMENSIONS.md) and [Bolt Chains](ROADMAP_BOLT_CHAINS.md).

## Why

- **Tokens.** A standard run reads the skill and one context answer per view. Most of
  that text is not needed to choose preview keys (baseline below).
- **Missed chains.** In 2 of 3 cold Haiku runs on M.394 the model skipped
  `bolt-position-Bottom`, reporting it had no `viewClass`, although the answer has
  `"viewClass":"Plan"`. The chain stands at the end of a long `Bolt` row, and the skill
  never names this chain kind.
- **Time (hypothesis, not proven).** One M.116 run took about 41 s, of which tool calls
  took about 9 s. The rest is model time, but it is not established how much of it
  reading costs. Speed is measured separately and is not the acceptance criterion.

## Baseline (2026-11-14)

Measured with the skill's own query: `get_view_dimension_context`,
`questions=chain,scale,repeats`, `ruleSet=steel`, on all 13 views of M.298, M.187,
M.116 and M.394. Sheet state at measurement: 17, 18, 16 and 18 dimensions; repeats
depend on it. A measurement without `repeats` gave 95 400 characters, the same picture.

| Part | Characters | Share |
|---|---|---|
| All 13 answers | 97 800 | 100 % |
| Chain fields a placement decision needs | 23 700 | 24 % |
| `Bolt` row | 67 500 | 69 % |
| Other rows (Top, Bottom, Left, Right, Local) | 23 100 | 24 % |
| Top level (`boltSettings`, `repeats`, ids, statuses) | 6 800 | 7 % |

Chain fields counted as needed: `key`, `chainKind`, `segments`, `incomplete`,
`incompleteReason`, `viewClass`, `repeat`, `remark`. The required top-level fields
(step 4) come on top.

Inside the `Bolt` row:

| Field | Characters | Needed to choose keys |
|---|---|---|
| `holes` | 22 000 | no |
| `sources` (full precision, e.g. `3564.8536421105091`) | 14 400 | no |
| `points` | 3 800 | no |
| `null` fields (`remark`, `incompleteReason`) | 2 000 | no |
| `direction`, `boltGroupId`, `partId`, `distance`, `attributesFile`, `kind` | ~11 000 | no |
| `key`, `chainKind`, `viewClass`, `segments`, `incomplete`, `incompleteReason` | ~12 000 | yes |

The skill `.agents/skills/dimension-drawings/SKILL.md` is another 23 400 characters
(363 lines), read in full at the start of every run. On M.394 (25 100 characters of
view answers) it is about half of what the model reads.

Displayed numbers carry geometry noise: `2345.002`, `549.999`, `1820.003`, `5.001`.

## Constraints found

- **The write reads the published answer.** With a `previewPlanId`,
  `create_dimensions_batch` resolves a preview key from the published JSON:
  `points`, `distance`, `direction`, `attributesFile` and `pointIds`
  (`ViewDimensionContextProvider.Batch.cs`, preview resolution). Removing a field from
  the answer would break the write.
- **The plan is stored already rounded.** It is published after
  `Freeze(..., display: true)`, so bolt and local coordinates are written with three
  decimals today. "The same dimensions" means these current write parameters. Writing
  source precision instead would change written values; that is a separate decision,
  not part of this roadmap.
- **`chainDetails` is a different plan variant, not a longer answer.** On a section it
  switches the consolidated chains to separate profile and location chains, with other
  keys (`ViewDimensionContext.cs`, section consolidation under `!detailed`). Details
  of a short answer must come from the same stored plan, without switching variant.

## Work order

### 1. Fix the reference — existing automated reference reused

- Reuse the checked-in full-answer fixtures and batch regressions to preserve points,
  distance, direction and attributes. Do not regenerate their expected answers.
- Additional measurements on the four drawings are optional future benchmarking,
  with a recorded initial sheet state. They are not required before this refactor.
- For any later model benchmark, record tokens, calls, time and skipped eligible
  chains across repeated runs; one run is not evidence.

### 2. Shorter standard part of the skill — implemented

- Split the skill: a short standard-run part (target: under 8 000 characters) and a
  reference part read only for repair, review or debugging.
- Name `bolt-position-*` explicitly: place it when `incomplete=false`.
- Ask for a short final report.
- **Automated gate:** retain the existing completeness/class policy and explicitly
  include eligible position chains. Live model skip-rate measurements are separate.

### 3. Separate the stored plan from the answer — implemented and tested

- The published plan keeps the current write parameters exactly as written today.
- The model's answer becomes a projection of that plan; batch resolution reads only the plan.
- A detailed projection of the same plan is available without changing the plan
  variant (the section `chainDetails` switch stays a separate question).
- **Done when:** batch tests and the compatibility corpus pass unchanged; the answer
  text is still identical at this step.

### 4. Shorter fields — implemented and tested

- Required in every short answer:
  - top level: `contextId`, `previewPlanId`, `isComplete`, the section refusal and
    `sectionPreviewStatus`, `repeats` when asked;
  - `Bolt` row: `readIncomplete`;
  - per chain: `key`, `chainKind`, `viewClass`, `segments`, `incomplete`,
    `incompleteReason` (when set), `repeat` (when set), `remark` (when set), and every
    field the skill names (`skippedPartIds`, `offeredOnOtherSidePartIds`,
    `droppedShortPointIds`) unless the skill changes in the same step.
- Moved to the detailed projection: `holes`, `sources`, `points`, `direction`,
  `boltGroupId`, `partId`, `distance`, `attributesFile`, `kind`, `null` fields,
  `boltSettings` and tolerances.
- Expected: the `Bolt` row from about 67 500 to about 15 000 characters on the baseline set.
- **Automated gate:** full and compact selections produce identical write parameters;
  full-answer fixtures pass unchanged. Summary-field differences are checked in
  separate tests rather than silently regenerating the full-answer corpus.

### 5. Rounding of displayed numbers — deferred, outside the current scope

- Do not implement this step now. It requires a separate decision after the current
  refactor; completing the answer-size work does not depend on it.
- Possible later change: round displayed `segments` and coordinates to 0.1 mm
  (`549.999` would show as `550`). Until then, retain the existing rounding.
- This is display only. Printed precision on the sheet depends on the dimension
  attribute files and is not changed here.
- **Guarantee:** rounded numbers take no part in repeat selection, checks or writes;
  those keep reading the plan. A test proves it.

### 6. Acceptance — automated checks passed; live benchmarking optional

- **Automated criterion:** no eligible key is dropped by projection, required safety
  status remains, and write parameters do not change. Existing fixtures stay intact.
- Optional later model runs on M.298, M.187, M.116 and M.394 can measure skips,
  characters/tokens, calls and time from a recorded sheet state. No live write is
  part of this implementation task.

## Implementation and verification (2026-11-14)

The user's subsequent direction simplified acceptance: proceed with the refactor,
use tests and existing captured answers to preserve write parameters, and defer
rounding. Additional cold-model runs and live placements are not prerequisites for
this implementation and were not performed. The reference-drawing observations
above remain historical measurements, not a claim that this delivery was run live.

- **Skill:** the standard entry point is 4547 characters / 73 lines, down from
  23027 characters / 363 lines. Focused repair, timber and review instructions
  moved to `references/workflows.md`; they are not read for a standard steel run.
  Eligible `bolt-position-*` chains are explicitly included under the existing
  completeness and Plan-class checks. The default report stays short.
- **Stored plan:** capture the full, already formatted API answer alongside the
  immutable rows. Batch resolution still reads those rows and keeps its existing
  precision, scope, expiry and sheet-state checks. No calculation or write path
  consumes the compact answer. Public API `Query` stays full for compatibility.
- **Compact answer:** the bridge projects ordinary `chain` responses to selection
  fields, including refusals/repeats and Bolt.readIncomplete. It omits write-only
  points/IDs/offsets/attributes, hole/source metadata and diagnostic lists. Top-level
  safety/error/status fields remain. `boltSettings` and tolerance metadata remain
  available when diagnostics is requested. `chainDetails` remains its existing full
  split-section variant; obsolete explicit bolt-proposal responses are unchanged.
- **Same-plan details:** call `get_view_dimension_context(viewId, previewPlanId=...)`.
  Other preview options are ignored. The saved full answer is returned with its
  ruleSet/chainView; there is no geometry read, recalculation or variant switch.
  Unknown/expired plans are refused. Never use chainDetails as a detail toggle.
- **No rounding change:** compact fields are copied as JsonElements; no new numeric
  formatting is applied. Full source precision and current write coordinates stay
  in the captured full answer. Step 5 remains deferred.

Release API, bridge, MCP/controller and tests built in `.codex-build/answer-size/`.
1753 tests passed, 1 skipped. Existing full-answer fixtures were not regenerated.
New tests compare all selection fields, retained safety status, unchanged decimals,
same-plan section detail retrieval without geometry reads, transport of the plan ID
and identical write points/direction/distance/attributes for full and compact
selections (front, reversed back and tall-beam cases).

Eight synthetic scenarios produced 63566 characters of full answers and 18533 of
compact answers, about 3.4 times smaller. These are synthetic character counts,
not the four-drawing live baseline or measured model tokens/latency. Results and
per-case counts are in `.codex-build/answer-size-results/answer-size.trx` locally.

Review fixes (2026-11-14): the compact answer keeps `carries` and `restoredBesides`;
a plan is the whole published answer, so another question set gets its own plan and
details always return the answer that named the plan; details are refused, like a
batch, once the sheet the plan was read on has changed. 1756 tests passed, 1 skipped.

Committed and pushed as `4e0110e`. The bridge and API are updated. The MCP server
with the new tool schema is not rebuilt, so the optional previewPlanId detail argument
appears only after that; placement does not need it.

## Live run after the bridge update (2026-11-14)

Cold Haiku run on M.394 after deleting all 18 dimensions: 18 placed in 46 s,
15 tool calls, 61 000 tokens, no write errors. The same count is not the same set:
one chain came, one went.

- Confirmed: `bolt-position-Bottom` placed (skipped in 2 of 3 runs before the skill change).
- **Open: the model drops eligible chains on its own.** `Top-location` on the bottom
  view "Unten" was eligible and not marked as a repeat, but the model skipped it as a
  "probable duplicate" of the front `Bottom-location`. It stood in the previous set.
  The answer-size work is done; this problem is not.
  First establish on the drawing whether this chain is really redundant. Only then
  choose a fix; these are different decisions:
  - if it is redundant, the repeat rule has to say so. Today the code compares a
    `BottomView` only with the `TopView` (`DimensionChainRepeats.cs`, mirror pairs), not
    with the `FrontView`. Do not widen the rule just to remove the model's doubt;
  - if it is needed, the skill forbids dropping an eligible chain by the model's own
    judgement.
- View answers: 9 260 characters on M.394, down from 25 100. Skill: 4 920.

Where the 61 000 tokens went:

| Part | Tokens, approx. |
|---|---|
| Agent environment before the first step (Claude Code prompt, every tool description, project instructions) | 39 600 |
| All tool answers (22 600 characters, table below) | 7 000 |
| Model output, mostly reasoning | 6 700 |
| Rest (turn overhead) | ~8 000 |

| Tool answers | Characters |
|---|---|
| `get_view_dimension_context`, 4 views | 9 260 |
| `create_dimensions_batch`, 4 views | 5 160 |
| Skill | 4 920 |
| `get_drawing_views` | 1 850 |
| File and tool search | 820 |
| Active drawing, dimension check | 460 |

The first table counts the environment once. Every turn re-reads the whole
conversation from cache. Split by token kind (Haiku prices: cache write x1.25,
cache read x0.1, output x5 of a fresh input token):

| Kind | Tokens | Weight in fresh-input tokens |
|---|---|---|
| Cache writes (new content) | 59 600 | ~74 500 |
| Cache reads over 13 turns | 609 500 | ~61 000 |
| Output | 6 700 | ~33 700 |

Cutting 13 turns to about 5 removes roughly half of the cache reads: about 13 % of
the run's cost. The environment alone costs about 49 500 on its first write plus
about 39 600 across its cache reads: roughly half of the run. So fewer turns is not
the largest saving; a smaller environment is.

### 7. Next: fewer steps and shorter remaining answers — proposed, not started

Agreed order: check the running tool schema, then 5 and 2, then 3, then shorter tool
descriptions (keep how each parameter is used; describe obsolete `boltChains` in one
line, drop repeats and long examples first), then a "delete all" command (later; it
must name what it deletes: today only `StraightDimensionSet`, not angle or radius
dimensions, be bound to the expected drawing and report partial deletion). Item 1
needs its own design (below).

Savings below are hypotheses, not measured acceptance results, and overlap: do not
add their percentages. Compare runs with the same drawing state and selected keys,
counting fresh input, cache writes, cache reads and output separately. Measure the
final report separately from the model's other output; the proposed 5-10 % saving
from a shorter report is unverified, and not all output tokens belong to that report.

1. **Fewer turns.** Read all views in one call and write all views in one call:
   about 5 turns instead of 13, about 13 % of a run's cost (see above). Keeping the
   contexts of all views is not enough:
   - plans are bound to the state of the whole sheet; writing the first view changes
     it, and `RequireSameSheet` then refuses the plans of the other views;
   - repeats of later views depend on the dimensions already placed.
   A batch over views must order the views and re-plan after each write, or plan them
   together against the sheet they will produce. Switching off the staleness guard is
   not a solution.
2. **Short write answer.** `create_dimensions_batch` repeats null fields
   (`mergedIntoDimensionId`, `error`, `renderedLineStatus`) and lists every created
   dimension again in `finalDimensions`. Keep per chain the key, the dimension id and
   the status (`created`, `retained`, `merged`, `uncertain`, `failed`, `skipped`) with
   its error, `mergedIntoDimensionId` when merged, and `finalReadError`. Keep
   non-empty warnings too: the API result names a substituted attributes file
   (`AttributesFileUsed`), which the bridge does not pass on today. Only the
   representation shrinks; the internal re-read that turns a write into `created`,
   `merged`, `uncertain` or `failed` stays. Expected about 1 000-1 500 characters
   instead of 5 160. Return one result for every selected key, including every
   non-success status. A non-empty `finalReadError` means final verification did not
   complete, even if individual results look successful; the agent must report it.
   Preserve non-empty diagnostic warnings, including rendered-line limitations and
   provisional bolt verification. Keep a full write response available for repair
   and debugging; `previewPlanId` stores the pre-write plan, not the actual write
   outcome, so it cannot replace that response.
3. **Short view list.** Placement needs id, type and name of each view, about 300
   characters instead of 1 850. Add a short representation; the full answer stays
   for layout operations.
4. **No doubtful chains.** The model spent 2 300 output tokens deciding about
   `Top-location` on "Unten". Depends on the open question above: first whether the
   chain is redundant, then rule or skill.
5. **Short refusals.** On M.394 4 500 of the 8 200 characters of view answers are
   chains that may not be placed: incomplete, bolt groups not of class `Plan`,
   repeats. The agent reads them in full and then skips them. Give eligible chains
   as now and the others as `key`, a reason code and a short note when needed.
   Separate selectable chains from refusals explicitly (for example, `chains` and
   `refused` in each row), rather than mixing objects of different completeness in
   one selection list. Update the skill's selection instructions with the response
   contract: today they select by `incomplete=false` and bolt class.
   - The verdict "do not place" stays with the code; it covers `incomplete`, the bolt
     class and repeats. The projection only shortens existing verdicts; it must not
     introduce another set of geometry or repeat rules. A query without `repeats`
     must still allow explicitly requested repeats.
   - `Bolt.readIncomplete` blocks every bolt write on the view and must stay even
     when no bolt chain was read. Keep view-level `isComplete`, section refusals and
     other general blockers: an empty chain list must not imply a successful read
     with no bolts or no dimensions needed.
   - Full fields remain available through the stored `previewPlanId`; the stored plan
     and the batch do not change. Implement in `DimensionAnswerProjection`.
   - Expected about 1 000 characters instead of 4 500 on M.394: about 2-3 % of a run's
     cost, but about 19 chains to read instead of about 30, so less room for doubt.
   - **Done when:** saved cases with repeats, an incomplete bolt read and write errors
     give the same plan, the same writes and the same refusals, with a shorter answer.
     Selection from the short answer must produce exactly the same keys and preserve
     every condition requiring the agent to stop. Cover empty bolt rows with
     `readIncomplete` and queries with and without repeats. For item 2, cover
     `merged`, `uncertain`, `failed`, `skipped`, attributes fallback and a failed final
     read. Test the actual JSON emitted by the bridge as well as API results: the
     earlier `mainPartMaterial` omission passed API tests but lost the field in the
     bridge's response mapping. A single successful live run is not acceptance.

A dedicated agent (`.claude/agents/`, only the tekla tools and the skill) is the way
to cut the 39 600-token environment, the largest single part of the cost. It is a
separate topic: the goal is an autonomous drawing agent (views, dimensions, marks).

**Items 5 and 2 implemented 2026-11-14, tested, not deployed.**
- Item 5: `DimensionAnswerProjection` splits every row into `chains` (placeable) and
  `refused` (`key`, `reason`: `repeat`, `incomplete`, `boltClass`,
  `boltReadIncomplete`; `note` from the code's reason). Row fields such as
  `readIncomplete` and top-level `isComplete` and section status stay; the top-level
  `repeats` list is dropped, its verdicts are on the chains. Placeable chains keep
  `key`, `chainKind`, `viewClass`, `segments`, `remark`, `carries`, `restoredBesides`.
  `steel-assembly.md` step 5 now says: select every key in `chains`.
  On the eight saved cases the short answers went from 18 533 to 14 539 characters;
  the placeable keys equal the old selection rule on every case.
- Item 2: `BatchAnswerProjection` in the API. Short (default): per key `status`,
  plus `dimensionId`, `mergedIntoDimensionId`, `error`, `attributesFileUsed`,
  `boltGeometryVerification`, `renderedLineStatus` (only a mismatch) when set;
  `renderedLineNotVerified` counts unchecked lines; `finalReadError` when set.
  `create_dimensions_batch(fullResult=true)` returns every field and the final
  dimensions; the full answer now also carries `attributesFileUsed`.
- Needs the bridge and the MCP server (new `fullResult` parameter) deployed.

### 8. Dedicated dimension agent — done 2026-11-14

`.claude/agents/tekla-dimensions.md`: Haiku, tools `Read` plus six tekla tools
(drawing context, views, dimensions, delete batch, view chain context, create batch).
It reads the skill file itself, so the rules stay in one place. It may delete only
when the task says replace or delete, and it may not drop an eligible chain by its
own judgement. A new agent type is picked up only at session start.

Cold run on M.394, task "replace": deleted 18, placed 19 on 4 views, no write errors.
Both `bolt-position-Bottom` and the bottom-view `Top-location` were placed; every
chain left out was marked by code (repeat, incomplete, `Side` class).

| | general-purpose Haiku | `tekla-dimensions` |
|---|---|---|
| Environment before the first step | 39 600 | 7 700 |
| Final conversation size | 61 000 | 24 000 |
| Model turns / tool calls | 13 / 15 | 12 / 14 (incl. delete) |
| Cache writes / reads / output | 59 600 / 609 500 / 6 700 | 23 100 / 179 800 / 4 700 |
| Cost in fresh-input tokens | ~169 000 | ~70 000 |
| Time | 46 s | 41 s |

The run is about 2.4 times cheaper. The open question about `Top-location` on the
bottom view (redundant or needed) stays open: this run placed it because the skill
and the agent forbid dropping eligible chains, which is not yet a decision that the
chain is needed.

## Risks

- The compatibility corpus compares saved answers; step 4 changes their shape on
  purpose. Deferred step 5 would separately change displayed numeric values.
- Debug and review tools must keep the full data through the detailed projection.
- `repeats` depend on the dimensions already on the sheet; a reference run starts from
  a recorded sheet state.
