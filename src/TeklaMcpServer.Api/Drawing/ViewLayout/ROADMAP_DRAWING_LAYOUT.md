# Roadmap Drawing Layout

## Goal

Make drawing view composition use one lightweight, connected layout context
instead of scattered runtime `View` lists, DTOs, and local dictionaries.

This roadmap is the active source of truth for sheet/view composition:

- `fit_views_to_sheet`
- view scale selection
- view rectangles and reserved areas
- base/projected/section/detail relations
- projection alignment
- layout scoring and before/after layout cases

Algorithm history and already implemented behavior are kept in
`ROADMAP_VIEWS.md`.

## Context Model

### DrawingContext

`DrawingContext` is the coarse sheet-level source for layout.

It should contain:

- drawing identity and type
- sheet width/height and margins
- lightweight layout views
- reserved table/title-block zones
- warnings and diagnostics

It must not contain heavy per-view geometry such as all parts, bolts, solid
vertices, mark geometry, dimension geometry, or part hulls.

### DrawingLayoutViewItem

`DrawingLayoutViewItem` is the lightweight view item used by layout.

It is effectively a layout-facing wrapper around the current coarse view facts:

- view id, type, semantic kind, name
- scale
- origin
- width/height
- frame/bbox rectangle
- frame offset, when known
- base/neighbor/section/detail role
- placement side and fallback diagnostics

It should be cheap to build for every view on the sheet.

### DrawingLayoutWorkspace

`DrawingLayoutWorkspace` is the temporary working area for one drawing-layout
operation.

It is built from `DrawingContext`, enriched with calculated layout facts, and
discarded after the operation. It is not a new source of truth.

It can hold:

- `DrawingLayoutViewItem` list and lookup by view id
- runtime `View` handles needed only for apply/probe operations
- current/candidate frame sizes and frame offsets
- topology and relation lookup
- arranged positions
- diagnostics
- optional `DrawingProjectionContext`

### DrawingProjectionContext

`DrawingProjectionContext` is a lazy add-on used only when projection alignment
needs extra signals.

It may contain:

- GA grid axes as `Guid/Label/Direction/Coordinate`
- assembly/single-part local anchors for selected model ids
- section placement side / alignment axis

It should not force full `DrawingViewContext` construction.

### DrawingViewContext

`DrawingViewContext` stays the detailed per-view geometry context for
dimensions and marks.

It contains heavier facts:

- parts
- bolts
- `PartsBounds`
- `PartsHull`
- grid ids
- detailed view-local warnings

Normal drawing layout must not pay the cost of building it.

## Current State

Already present:

- `DrawingContext`
- `DrawingLayoutViewItem`
- `DrawingLayoutWorkspace`
- `DrawingLayoutContextBuilder`
- `DrawingViewContext`
- `DrawingViewContextBuilder`
- `DrawingLayoutScorer`
- `DrawingCaseCaptureService`
- `DrawingCaseSnapshotWriter`
- `ViewTopologyGraph`
- `ViewPlacementValidator`
- `DrawingProjectionAlignmentService`

Current migration status:

- `fit_views_to_sheet` builds/reads `DrawingContext`.
- `DrawingLayoutWorkspace` is created for the operation and now carries the
  main lightweight layout state: view items, runtime view handles, semantic
  lookup, original scales, actual rects, selected frame sizes, frame offsets,
  topology cache, reserved areas, sheet facts, and GA grid axes.
- `DrawingArrangeContext` preserves the workspace through derived contexts.
- `DrawingProjectionAlignmentService` has a workspace-aware path and reads
  sheet/reserved/frame/topology/grid facts from the workspace.

Remaining cleanup:

- `fit_views_to_sheet` is still a large orchestration method, but the main
  context migration is complete.
- Further splitting of post-arrange/projection/final-result steps is optional
  readability work, not an architecture blocker.
- Runtime `View` lists are still needed for Tekla apply/probe operations, but
  they are now treated as apply/probe handles rather than primary layout state.

## Phases

### Phase 1. Define Lightweight Layout Workspace

- Add `DrawingLayoutViewItem`.
- Add `DrawingLayoutWorkspace`.
- Populate them from `DrawingContext` and existing runtime view facts.
- Keep behavior unchanged.
- Add tests for scale, rect, semantic kind, reserved areas, and lookup parity.

Status: done.

### Phase 2. Move Existing Lookup State Into Planning Context

Move these scattered structures behind the planning context:

- `semanticKindById`
- selected frame sizes
- actual frame rects
- frame offsets
- arranged position lookup
- base/neighbor/section/detail relation lookup

No layout policy changes in this phase.

Status: done.

Notes:

- `DrawingLayoutWorkspace` now owns the main lookup state used by layout.
- `DrawingArrangeContext` preserves workspace access through derived contexts.
- Keep short-lived local candidate dictionaries only where they represent a
  single probe result before it is stored into the workspace.

### Phase 3. Make Projection Alignment Context-Aware

- Introduce lazy `DrawingProjectionContext`.
- Reuse planning view rects and frame offsets for collision checks.
- Load GA grid axes only when GA projection alignment needs them.
- Load assembly/single-part anchors only for the model id being aligned.
- Keep full `DrawingViewContext` out of projection alignment.

Status: workspace-aware path done; lazy `DrawingProjectionContext` remains
optional follow-up if projection alignment needs more per-view geometry later.

### Phase 4. Refactor `fit_views_to_sheet` Orchestration

- Make scale selection consume layout context facts.
- Make arrange/diagnostics consume planning context facts.
- Keep runtime `View` objects as apply handles only.
- Preserve public MCP/bridge result shape.

Status: done.

Completed:

- Scale selection consumes workspace facts for semantic kind and frame sizes.
- Arrange/parity/detail helpers read planning facts through
  `DrawingLayoutWorkspace` / `DrawingArrangeContext`.
- Keep-scale validation and candidate scale probing are split into private
  behavior-preserving steps.
- Runtime `View` objects remain only where Tekla apply/probe handles are
  required.
- Public MCP/bridge result shape is preserved.

Optional cleanup:

- Split final post-arrange orchestration into smaller private steps:
  offset correction, projection alignment, centering, detail reposition, and
  result/diagnostics assembly.
- Do this only when it helps the next feature; it is not required before
  analytical layout work.

### Phase 5. Analytical Layout Follow-up

Status: active planning.

Live validation of the context migration passed without required behavior
changes. The next work is analytical candidate scoring, not another view-context
refactor.

Goal:

- move toward `candidate -> validate -> score -> choose -> apply`
- evaluate more layout decisions virtually before Tekla `CommitChanges`
- compare candidate layouts with `DrawingLayoutScorer`
- keep before/after `DrawingContext` cases as the canonical dataset format

#### 5.1 Passive Scoring

First step. No layout behavior changes.

Status: initial implementation done.

- Add `DrawingLayoutCandidate`.
- Add `DrawingLayoutCandidateView`.
- Reuse existing `DrawingLayoutScore`; no separate score result is needed yet.
- Build one candidate from the current `fit_views_to_sheet` result.
- Score that candidate with `DrawingLayoutScorer`.
- Write score and diagnostics to trace/log output.
- Do not choose a different layout yet.
- Do not change public MCP/bridge result shape.

#### 5.2 Candidate Model

Status: initial implementation started.

- Represent virtual view origin, scale, frame rect, semantic kind, and placement
  side without requiring a runtime `View`.
- Carry sheet size, margins, reserved areas, and title-block/table conflicts.
- Carry view-overlap, out-of-sheet, projection-quality, and movement
  diagnostics.
- Keep candidates serializable enough for case snapshots and regression data.
- Keep heavy `DrawingViewContext` data out of normal candidates.

Implemented so far:

- `DrawingLayoutCandidateView` carries an explicit layout rect.
- Runtime `View` to candidate conversion is isolated in
  `DrawingLayoutCandidateBuilder`.
- `DrawingLayoutCandidateEvaluation` groups candidate, score, and feasibility
  diagnostics for the next multi-candidate selection step.

#### 5.3 Multi-Candidate Evaluation

Status: initial implementation started.

- Generate several virtual layout candidates from the same workspace.
- Start with behavior-equivalent variants before introducing new policies.
- Evaluate candidates without `Modify()` / `CommitChanges()` where possible.
- Score candidates through `DrawingLayoutScorer`.
- Keep diagnostics explaining why the selected candidate won.

Implemented so far:

- `DrawingLayoutCandidateSelector` evaluates candidate lists and selects the
  best candidate by feasibility, score, and stable input order.
- `fit_views_to_sheet` routes the current passive final candidate through the
  selector, but still supplies only the existing behavior-equivalent candidate.
- `fit_views_to_sheet` now compares two passive candidates in trace:
  post-projection before post-processing and final after centering/detail
  repositioning. The selected candidate is still not applied.
- Selection trace now includes per-candidate index, rank, selected flag, and
  rejection/selection reason.
- `DrawingLayoutCandidateBuilder.FromPlannedLayout` builds a virtual planned
  candidate from arranged origins and selected frame sizes without reading
  post-apply actual view rectangles.
- Planned candidate construction now has a pure DTO/factory path:
  `DrawingLayoutPlannedView` + `DrawingLayoutCandidateFactory.FromPlannedViews`.
  Tekla `View` remains only in the current adapter builder.
- Adapter boundary is explicit: `DrawingLayoutCandidateBuilder.ToPlannedViews`
  converts runtime `View + ArrangedView` to planned DTOs; `FromPlannedLayout`
  is a convenience wrapper over it. Future variant generator receives planned
  DTOs directly without touching Tekla `View`.
- `ViewGroupCenteringGeometry` extracted from `TeklaDrawingViewApi` as a pure
  static helper; `TryCenterViewGroup` now delegates to it.
- First virtual variant: `DrawingLayoutPlannedCenteringService.TryCenterViews`
  computes a centered layout from planned DTOs without Tekla calls. The
  `fit_views_to_sheet:planned-centered` candidate appears in selection trace
  alongside `planned-arranged`, `post-projection`, and `final`.
- `fit_layout_planned_variant` trace event emitted per variant with moved view
  count, detailMoved (always 0), maxDelta, avgDelta, group bbox before/after,
  and reserved overlap count before/after from scorer evaluation.
- Planned variant summary calculation is isolated in
  `DrawingLayoutPlannedVariantDiagnostics`; `TeklaDrawingViewApi` only formats
  and emits the trace event.

#### 5.4 Apply Selected Candidate

Status: implemented, disabled by default.

- Apply only the selected candidate to Tekla runtime views.
- Preserve current public result shape.
- Include selected-candidate score/diagnostics in trace first.
- Add result fields only in a separate planned API contract step.

Implemented so far:

- `DrawingLayoutCandidateApplyPlan` describes whether the selected candidate is
  currently applicable and which view origins/scales it would apply.
- `fit_layout_apply_plan` and `fit_layout_apply_plan_move` trace events report
  the selected candidate apply plan without calling `Modify()` or
  `CommitChanges()`.
- `DrawingLayoutCandidateApplyService` validates a selected apply plan against
  available runtime view ids and supports explicit `DryRun` / `Apply` modes.
  `fit_views_to_sheet` currently calls it only in `DryRun` mode and emits
  `fit_layout_apply_execution`, so selected-candidate apply remains disabled.
- `DrawingLayoutCandidateTeklaApplyAdapter` is the Tekla-facing apply boundary:
  it can map apply-plan moves to runtime `View` handles, set origin/scale, and
  call `Modify()`. It intentionally does not call `CommitChanges()`, and the
  current `fit_views_to_sheet` integration still uses `DryRun`.
- `DrawingLayoutCandidateApplyGate` is the internal behavior switch. By default
  it resolves every request to `DryRun`; when explicitly enabled, only
  `DrawingLayoutApplyMode.FinalOnly` resolves to `Apply`.
- `fit_views_to_sheet` has a guarded selected-candidate apply branch: when the
  gate resolves to `Apply` and all moves succeed, it commits once, refreshes
  runtime views/actual rects, rebuilds the arranged result from the apply plan,
  and emits `fit_layout_apply_commit`. With the default gate state this branch
  is not executed.
- `DrawingLayoutCandidateApplyDeltaBuilder.BuildDeltas(baseline, plan)`
  compares the current final candidate with the selected apply plan before
  apply. Trace now includes `fit_layout_apply_delta` summary for all moves and
  `fit_layout_apply_delta_view` only for views that moved beyond the explicit
  movement tolerance, changed scale beyond the shared scale tolerance, or are
  missing from the baseline.
- `DrawingLayoutCandidateApplySafetyPolicy` converts requested apply mode to
  effective mode using the delta summary. The default policy blocks real apply
  when baseline views are missing or scale changes are detected. Movement is
  reported but not blocked by default. Trace event `fit_layout_apply_safety`
  records requested/effective mode and decision reason.

#### 5.5 Regression Cases

Status: infrastructure done; live validation cases are next.

- Store before/after `DrawingContext` case snapshots.
- Include candidate score and validation diagnostics.
- Re-run the validation baseline on real drawings.
- Compare geometry and score stability across repeated runs.

Recommended sequence:

1. Capture before/after `DrawingContext` snapshots for a small fixed drawing
   set using `DrawingCaseCaptureService`.
2. Store the trace-backed candidate metadata for each run: selected candidate,
   apply-plan summary, delta summary, safety decision, and final score.
3. Re-run `fit_views_to_sheet` twice on the same drawing and compare the second
   run against the first after-state for stability.
4. Cover the validation baseline categories:
   standard projected neighbors, top/bottom sections, left/right sections,
   details with `DetailMark` anchors, detail-like sections with `SectionMark`
   anchors, GA grid-axis projection alignment, and reserved table/title-block
   avoidance.
5. Keep selected-candidate `Apply` disabled until the captured cases show that
   the selected candidate is stable and the safety policy accepts only expected
   origin-only moves.

Acceptance for enabling selected-candidate apply:

- No missing baseline views in selected apply deltas.
- No unexpected scale changes.
- Repeated runs converge: second run has zero or near-zero apply delta.
- Reserved/table/title-block overlaps do not increase.
- Projection/detail placement diagnostics do not regress.

Implemented so far:

- `DrawingCaseSnapshotWriter` / `DrawingCaseCaptureService` can write optional
  `LayoutDiagnostics` into `meta.json`, including selected candidate, apply
  plan summary, apply delta summary, and safety decision summary. Existing
  before/after snapshot files and existing save calls remain compatible.
- `DrawingCaseLayoutDiagnosticsFactory` maps candidate selection, apply-plan,
  apply-delta, and safety-decision objects into the case `LayoutDiagnostics`
  DTO using the same stable reason strings as trace output.
- `fit_views_to_sheet` now builds that diagnostics DTO and stores it on an
  internal `FitViewsResult.LayoutDiagnostics` property for in-process case
  capture. The public JSON result contract remains unchanged.
- `DrawingCaseCaptureService.SaveLayoutCase(...)` accepts the `FitViewsResult`
  and persists its internal layout diagnostics with the before/after
  `DrawingContext` snapshots.
- `DrawingLayoutStabilityAnalyzer` compares two repeated-run after contexts
  and reports convergence signals: moved views, scale changes, missing/added
  views, score delta, and overlap-area deltas.
- `DrawingCaseSnapshotWriter` / `DrawingCaseCaptureService` can persist an
  optional `LayoutStability` report into `meta.json` together with score and
  selected-candidate diagnostics.
- `DrawingCaseSnapshotReader` can load saved `before.json`, `after.json`, and
  `meta.json` files back into typed contexts for offline regression checks.
- `DrawingLayoutRegressionCaseEvaluator` combines the snapshot reader and
  stability analyzer to compare two saved repeated-run cases by their
  after-contexts.

Remaining work:

- Capture the fixed live validation set in Tekla.
- Store first-run and second-run cases for the same drawings.
- Review `LayoutDiagnostics` and `LayoutStability` in `meta.json`.
- Keep selected-candidate `Apply` disabled until the captured cases satisfy
  the acceptance checks below.

Phase 5 non-goal:

- Do not change layout behavior in 5.1. Passive scoring must observe and
  explain the current result before candidate selection affects output.

### Phase 6. Качество компоновки

Цель: чертеж должен быть понятен человеку. Крупный масштаб, близкое
расположение видов и отсутствие пустого места важнее строгого соблюдения
проекционной стороны.

Проекционное выравнивание сторон (Top section над FrontView, Right section
справа) остается бонусом, когда оно получается без ухудшения компоновки. Это не
главная цель.

Проекционная связь должна быть настраиваемой по силе:
- strong projection: держать вид на канонической стороне и сохранять
  проекционное выравнивание, если это не ухудшает масштаб/заполняемость;
- relaxed projection: разрешить opposite/cross-axis placement, если strict
  projection вынуждает уменьшать масштаб или оставляет много пустого места;
- off/weak projection: для независимых дополнительных видов на GA drawing связь
  с base view может быть слабой или отсутствовать, важнее компактная
  компоновка без конфликтов.

Для оценки качества layout нужно считать:
- доступную площадь листа: usable sheet area минус union reserved/table areas;
- суммарную площадь видов на текущем масштабе;
- fill ratio: `sum(view area) / available sheet area`.

Этот коэффициент характеризует заполняемость чертежа: насколько эффективно
виды используют доступную площадь листа. Он помогает отличить плотную
компоновку от листа с большим пустым пространством. При этом fill ratio не
заменяет геометрическую проверку размещения: даже при высокой или достаточной
заполняемости views могут не помещаться из-за формы свободных зон, reserved
areas, projection constraints или конфликтов между видами.

Regression reference:
- Fallback path из старого layout не удален: projection-aware anchors сначала,
  затем packing оставшихся видов вокруг anchors. После workspace/candidate/
  validation изменений этот path могут не достигать или его результат может
  проигрывать более strict projection/budget constraints.
- Git reference: `8a3c3c5^`, файл
  `src/TeklaMcpServer.Api/Drawing/ViewLayout/BaseProjectedDrawingArrangeStrategy.cs`,
  метод `Arrange(...)`: ветки `mode=anchor-then-maxrects`,
  `mode=maxrects-fallback`, `mode=shelf-fallback`.
- Дополнительный старый post-adjust reference: `8a3c3c5^`, файл
  `src/TeklaMcpServer.Api/Drawing/ViewLayout/TeklaDrawingViewApi.Layout.cs`,
  методы `TryCenterViewGroup(...)` и `TryRepositionDetailViews(...)`.
- Phase 6 не должна возвращать старый код. Она должна восстановить свойство
  старого поведения в новом validation/candidate контуре: projection side
  является preference/score signal, а не абсолютным constraint.
- Проверять нужно не только `Arrange`, но и `EstimateFit` /
  `DiagnoseFitConflicts` / scale selection: они не должны отвергать масштаб до
  попытки более гибкого размещения.

#### 6.1 Гибкое размещение дополнительных видов

Текущее поведение: дополнительные виды размещаются на вычисленной стороне
(Top -> сверху, Right -> справа). Если они там не помещаются, `GetFallbackZone`
через
`TryProbeSectionStackWithFallback` пробует противоположную сторону
(Top -> Bottom, Right -> Left). Если не помещается и она, уменьшается масштаб.
Cross-axis варианты (Top -> Right/Left, Right -> Top/Bottom) сейчас не
пробуются.

Целевое поведение: расширить существующую цепочку preferred -> opposite новым
cross-axis шагом до уменьшения масштаба. Уменьшение масштаба остается последним
вариантом.

Приоритет размещения дополнительных видов:
1. Preferred side (сторона, вычисленная из направления/отношения вида).
2. Opposite side (Top -> Bottom, Right -> Left) — существующий fallback шаг.
   Его нужно сохранить, но не считать достаточным: при некоторых layout
   constraints opposite side тоже может не пройти.
3. Cross-axis sides (Top -> Right или Left, Right -> Top или Bottom) — новый
   шаг. Выбирать сторону, где больше свободного места.

При cross-axis меняется только фактическая сторона и ориентация стека. Например,
виды из группы Top остаются Top views, но если они переехали вправо, они
размещаются вертикальным стеком справа от `baseRect`.

Термин "дополнительные виды" здесь включает section/detail/secondary projected
views, виды деталей на GA drawing и другие небазовые виды. Явная связь между
видами не обязательна.
Первый implementation scope идет через текущий section placement path
(`SectionGroupSet`, `TryPlace...Section...`), но цель Phase 6 шире: не уменьшать
масштаб, пока не исчерпаны допустимые варианты размещения дополнительных видов.

Что нужно изменить:
- Budget/base-rect selection в `TrySelectBaseRectWithBudgets` — главный
  архитектурный риск. Он не должен резервировать место для дополнительных видов
  на preferred стороне, если эти виды в итоге уходят на cross-axis сторону. Иначе
  `baseRect` будет выбран так, будто сверху нужен top budget, даже когда top
  views перенесены вправо.
- Изменения вокруг budgets/base rect рискованнее, чем placement probing.
  Сначала нужно покрыть cross-axis placement тестами и менять budget/base-rect
  selection только если тест показывает, что старые budgets реально мешают
  cross-axis placement.
- `GetFallbackZone` сейчас возвращает только одну противоположную сторону.
  Нужен дополнительный cross-axis fallback шаг рядом с ним, а не вместо него.
- `TryPlaceHorizontalSectionStackWithFallback`,
  `TryPlaceVerticalSectionStackWithFallback` и `TryPlaceDegradedStandardSections`
  должны получить cross-axis кандидатов.
- `SectionGroupSet` и семантическая группа вида не меняются. Например,
  Top section/detail-like view остается в группе Top, даже если фактически
  размещается справа.
  Меняется только `ActualPlacementSide`/actual placement geometry.
- Добавить явный projection-strength signal для кандидата/вида: strict,
  relaxed или weak/off. В Phase 6.1 это может быть только diagnostic/scoring
  input без изменения public contract.
- Добавить diagnostics для площади: available sheet area, sum view area и fill
  ratio для рассматриваемого масштаба/кандидата.

Implementation note:
- Для cross-axis использовать anchor целевой стороны:
  `mainSkeleton.GetAnchorOrBase("<target>", baseRect)`, где target это
  `right`, `left`, `top` или `bottom`.

Диагностика:
- Переиспользовать существующие `PlacementFallbackUsed`,
  `PreferredPlacementSide`, `ActualPlacementSide` в arranged/planned view
  diagnostics.
- Расширить trace event `section_stack_result` флагом `crossAxis=1`, когда
  использована cross-axis сторона.

Acceptance criteria для 6.1:
- На проблемном чертеже новая логика должна пробовать cross-axis размещение
  дополнительных видов до уменьшения масштаба. Если такое размещение проходит
  все layout constraints на текущем масштабе, масштаб не должен уменьшаться.
- Preferred side по-прежнему пробуется первым, opposite side — вторым.
  Cross-axis используется только если оба same-axis варианта не подходят.
- В arranged/planned diagnostics у перенесенных видов заполнены
  `PreferredPlacementSide`, `ActualPlacementSide` и `PlacementFallbackUsed`.
- Trace `section_stack_result` показывает фактическую сторону и `crossAxis=1`
  для cross-axis fallback.
- Diagnostics показывают, была ли проекционная связь сохранена strict или
  ослаблена до relaxed/weak ради лучшей компоновки.
- Публичный JSON contract `fit_views_to_sheet` не меняется.

Публичный result contract и scoring в этой фазе не меняются.

Status: in design.

#### Future. Agentic View Layout

В перспективе нужен отдельный инструмент для agent-driven компоновки видов.
Это не замена `fit_views_to_sheet`, а более свободный режим, где агент может
сам искать расположение видов, менять масштабы видов, пробовать разные варианты
и выбирать лучший по quality/score.

Граница безопасности:
- агент может двигать виды и менять масштабы видов только внутри явной
  layout-команды пользователя;
- команды диагностики, размеров, марок или проверки чертежа не должны
  произвольно двигать виды или менять их масштабы;
- результат agentic layout сначала должен быть представлен как план/preview с
  diagnostics, score, списком перемещенных видов и списком scale changes;
- apply должен быть отдельным явным шагом или защищен тем же safety gate, что и
  остальные layout-команды.

Этот режим может быть недетерминированным по поиску кандидатов, но результат
должен быть объяснимым: какие виды двигались, какие масштабы изменились, почему
выбран этот вариант, какие constraints сохранены, какие projection связи
ослаблены.

## Non-Goals

- Do not rewrite layout policy while introducing context.
- Do not load parts/bolts/hulls for normal drawing layout.
- Do not put detailed mark/dimension geometry into `DrawingContext`.
- Do not replace table marker-based reserved-area reading.
- Do not change public tool contracts unless explicitly planned.

## Validation Baseline

Before Phase 5, run a small live-validation baseline for `fit_views_to_sheet`
behavior:

- assembly drawings with standard projected neighbors
- sheets with `Top` / `Bottom` sections
- sheets with `Left` / `Right` sections
- drawings with detail views and `DetailMark` anchors
- drawings with detail-like sections and `SectionMark` anchors
- GA drawings that need grid-axis projection alignment
- repeated `fit_views_to_sheet` on the same drawing to check stability
- reserved table/title-block avoidance

## Acceptance Criteria

- There is one clear active roadmap for drawing layout.
- `DrawingContext` remains the sheet-level source.
- `DrawingLayoutWorkspace` is temporary and not a source of truth.
- `DrawingLayoutViewItem` is cheap and layout-specific.
- `DrawingViewContext` remains reserved for dimensions/marks.
- `fit_views_to_sheet` behavior remains stable during context migration.
- Projection alignment gets only the lightweight geometry it needs.
