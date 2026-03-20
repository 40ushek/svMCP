# Runtime Boundary Notes

## Goal

Capture the possible future boundary between:

- domain/planner logic
- local trusted Tekla runtime
- remote/server-delivered logic

This note is intentionally separate from `ROADMAP_VIEWS.md`.
`ROADMAP_VIEWS.md` remains focused on view-layout domain redesign, not on
deployment architecture.

## Why This Exists

The current project is local-first:

- `TeklaBridge` runs next to installed Tekla
- runtime commands execute against local Tekla Open API
- planning and execution currently live in the same overall local system

A future architecture may want to separate:

- evolving policies/prompts/planner logic
- trusted local execution against Tekla

That separation is important, but it is a later concern than the current
planner redesign.

## Candidate Boundary

Possible future split:

- `server-delivered layer`
- `local trusted host`
- `Tekla bridge`

### Server-Delivered Layer

Could eventually contain:

- policies for `BaseViewSelection`
- policies for `ProjectionMethod`
- rules for `SectionPlacementSide`
- planner logic around `ProjectionLayoutPlan`
- prompts, templates, feature flags
- explanation/debug formatting

This layer would answer:

- what should be planned
- what policy/rule set is active

### Local Trusted Host

Must remain close to Tekla runtime and keep execution control.

Could contain:

- validation of requested operations
- safety checks before modifying drawings
- translation between snapshots/results and runtime commands
- command orchestration against the bridge

This layer would answer:

- can this be executed safely here

### Tekla Bridge

Must remain local to the installed Tekla environment.

It is responsible for:

- reading Tekla runtime metadata
- adapting `View` and drawing objects into local snapshots
- executing `Modify()` / `CommitChanges()`
- applying concrete changes through Tekla Open API

This layer would answer:

- how the requested action is executed in Tekla

## Guiding Rule

If this architecture is pursued later:

- server-delivered layer decides `what`
- local trusted host decides `whether it is safe/allowed`
- Tekla bridge performs `how`

## Why This Is Deferred

This boundary is not part of the immediate `ROADMAP_VIEWS.md` execution plan.

Reasons:

- the current priority is stabilizing the planner/domain model
- introducing runtime/distribution concerns too early would blur the roadmap
- online/server-delivered architecture is a separate level of complexity

## Revisit Trigger

Revisit this note only after:

- `BaseViewSelection` is explicit
- `SectionPlacementSide` is explicit
- `ProjectionLayoutPlan` is explicit
- degradation paths are explicit and testable
- planner logic is sufficiently separable from raw Tekla runtime objects
