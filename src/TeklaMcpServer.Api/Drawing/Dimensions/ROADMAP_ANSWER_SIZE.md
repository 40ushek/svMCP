# Answer Size Roadmap

Created 2026-11-14, revised the same day after review. Status: planned, nothing implemented.

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

### 1. Fix the reference — planned

- Record, on the four baseline drawings from a known sheet state, the chains a standard
  run must place and the write parameters of each (points, distance, direction,
  attributes file).
- Record the per-run figures to compare later: characters and tokens read, tool calls,
  time, and **the number of placeable chains skipped**.
- Repeat each run more than once; one run is not evidence.

### 2. Shorter standard part of the skill — planned

- Split the skill: a short standard-run part (target: under 8 000 characters) and a
  reference part read only for repair, review or debugging.
- Name `bolt-position-*` explicitly: place it when `incomplete=false`.
- Ask for a short final report.
- **Done when:** the reference runs place the same chains, with no skipped chain.

### 3. Separate the stored plan from the answer — planned

- The published plan keeps the current write parameters exactly as written today.
- The model's answer becomes a projection of that plan; batch resolution reads only the plan.
- A detailed projection of the same plan is available without changing the plan
  variant (the section `chainDetails` switch stays a separate question).
- **Done when:** batch tests and the compatibility corpus pass unchanged; the answer
  text is still identical at this step.

### 4. Shorter fields — planned

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
- **Done when:** the reference runs place the same chains with the same write
  parameters; corpus differences are reviewed and accepted explicitly, not
  regenerated silently.

### 5. Rounding of displayed numbers — deferred, outside the current scope

- Do not implement this step now. It requires a separate decision after the current
  refactor; completing the answer-size work does not depend on it.
- Possible later change: round displayed `segments` and coordinates to 0.1 mm
  (`549.999` would show as `550`). Until then, retain the existing rounding.
- This is display only. Printed precision on the sheet depends on the dimension
  attribute files and is not changed here.
- **Guarantee:** rounded numbers take no part in repeat selection, checks or writes;
  those keep reading the plan. A test proves it.

### 6. Acceptance — planned

- Reference runs on M.298, M.187, M.116 and M.394 after each step.
- **Criterion:** no skipped placeable chain and no change of write parameters.
- Characters, tokens, tool calls and time are recorded and compared, not required.

## Risks

- The compatibility corpus compares saved answers; step 4 changes their shape on
  purpose. Deferred step 5 would separately change displayed numeric values.
- Debug and review tools must keep the full data through the detailed projection.
- `repeats` depend on the dimensions already on the sheet; a reference run starts from
  a recorded sheet state.
