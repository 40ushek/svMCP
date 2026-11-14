# Answer Size Roadmap

Created 2026-11-14, revised the same day after review.
Status: current refactor implemented, reviewed and tested; the bridge is deployed and one
cold Haiku run on M.394 placed 18 dimensions with no write errors, including
`bolt-position-Bottom`. The MCP server is not rebuilt yet (details by previewPlanId).

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

Deployment requires the matching bridge/API and refreshed MCP tool schema for the
new optional previewPlanId detail argument. No deployment, push or drawing mutation
was performed. Only the initial roadmap was committed before implementation.

## Risks

- The compatibility corpus compares saved answers; step 4 changes their shape on
  purpose. Deferred step 5 would separately change displayed numeric values.
- Debug and review tools must keep the full data through the detailed projection.
- `repeats` depend on the dimensions already on the sheet; a reference run starts from
  a recorded sheet state.
