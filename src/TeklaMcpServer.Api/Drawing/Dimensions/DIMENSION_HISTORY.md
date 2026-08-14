# Dimension Milestones and Validation History

The strategic roadmap keeps only current status. This file records completed
milestones and the empirical evidence behind them, so neither is mistaken for
future work.

- 2026-08-01: implemented read-only 'capture_dimension_observation'.
- 2026-08-01: retained per-segment related sources and readable
  'DimensionLink' endpoint IDs; the legacy flat 'RelatedSources' list remains
  for compatibility.
- 2026-08-01: stabilized 'DimensionActionPlanBuilder' and
  'get_dimension_action_plan'; the old command remains an alias.
- 2026-08-01: implemented 'get_dimension_chain_coverage', retaining all
  candidate matches and confidence without choosing a winner.
- 2026-08-02: implemented compact read-only 'get_dimension_defects' over one
  shared snapshot; failed candidate reads skip anchor checks.
- Validated: 'arrange_dimensions', 'place_control_diagonals', 'PartsBounds'
  anchoring, view-local geometry and stable reread on assembly cases.

Remaining work starts with cache sharing, printed-row/start-point contracts,
candidate-point planning, placement planning and selective GA context loading.

## Detailed completed decisions

### Orchestration rename — 2026-08-01

'DimensionAiAssistedOrchestrator' was misleading: it used no model and executed
nothing. It was renamed to 'DimensionActionPlanBuilder', with
'DimensionActionPlanResult', 'DimensionActionPlanStep', 'DimensionPlanAction'
and 'DimensionActionPlanEvidence'. Behaviour stayed unchanged.

'get_dimension_action_plan' is the stable command; the old
'get_dimension_ai_orchestration_plan' remains an alias. The intended boundary is:

```text
observation -> DimensionActionPlanBuilder -> plan
            -> an LLM or person decides whether to apply it
            -> combine / move / arrange / recreate
```

The builder does not execute operations.

### Coverage evidence — 2026-08-01

'get_dimension_chain_coverage' deliberately keeps every candidate match. It
does not pick a winner. Search tolerance and position-coincidence epsilon are
separate: 'matched' means one geometric place, often with several anchor keys;
'ambiguous' means genuinely different places.

On IW.1, view 1214, all eight chains and 27 points were matched, but three
junction points belonged to two parts. That is one position, not necessarily
an ambiguous choice; the dimension subject still has to select the source part.

Two points used only a raked-top bounding-box corner. The full evidence and
coordinates are in [DIMENSION_RUNTIME_NOTES.md](DIMENSION_RUNTIME_NOTES.md).

## Defect detection evidence

This is the empirical history behind the removed 'get_dimension_defects'.

### EW.4-6 batch check — 2026-08-02

This was the first drawing where the full checklist was run before proposing
anything.

**The checks found all four defects. Three were then left in place by the
assistant, and the person removed them. The fourth was acted on incorrectly.**

The bottleneck was not detection. It was talking a detected defect out of the
result because a rule appeared to have an exception: overlay layer, wide panel,
or an apparently honest printed number. The exception must name a checkable
condition rather than become an explanation.

Two consequences were established:

- **Speed:** 'get_dimension_contexts' returned 119,921 characters on a
  six-chain drawing and did not fit the tool limit. A findings command returns a
  compact result and does not scale with the full part count.
- **Correctness:** on EW.4-6, an ad-hoc bounding-box script and
  'get_dimension_chain_coverage' found different problems. The script reported
  the width overall's endpoints as 'on nothing'; coverage returned 'Missing'
  because no candidate existed, not even a box corner. Independent checks of
  the same condition diverge.

### Grading before automatic application

The checks must first be a pure function over captured JSON:

- 'dimension_contexts.json';
- 'parts_geometry.json';
- 'candidates_*.json';
- 'coverage_*.json'.

Run it against the human-edited cases and compare each finding with what the
person actually changed. A class that fires on a point deliberately retained
by the person is not safe for automatic action.

The cases include human-edited states
'004604c1', '5cf600c9', '8a856c51', 'c5018fe1', and 'c5109755', plus accepted
after-states.

### Former bridge implementation

'get_dimension_defects viewId' was a read-only compact detector, removed on
2026-08-14 when placement moved to structural-chain positions.
It read dimension contexts and part geometry once, built candidate coverage once
per distinct part, and passed that snapshot to `DimensionDefectDetector`.

The bridge/MCP result contains compact chain summaries, findings and warnings.
If a part candidate read fails, anchor checks are skipped instead of producing
a false 'UnanchoredPoint'.

### Automatic-action boundary

The provisional split remains:

| likely automatic | likely reported |
|---|---|
| phantom anchor ('fallbackOnly' / 'Missing') re-anchored to a real vertex | anything concerning an overall dimension |
| span equal to a part's own extent | start point of an absolute chain |
| point far across from its chain | coordinate-space failure |
| contained printed values | raked top beyond the one worked case |

Start points remain report-only until the printed rows and pre-normalization
point order are exposed. Recreating all absolute chains blind would be unsafe.
