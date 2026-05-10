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

## Фазы

### Фаза 1. Легкий layout workspace

Статус: выполнено.

Цель фазы: завести легкую рабочую модель для компоновки видов без изменения
поведения.

Сделано:
- добавлен `DrawingLayoutViewItem`;
- добавлен `DrawingLayoutWorkspace`;
- workspace наполняется из `DrawingContext` и текущих runtime-фактов о видах;
- поведение `fit_views_to_sheet` сохранено;
- добавлены проверки для scale, rect, semantic kind, reserved areas и lookup
  parity.

### Фаза 2. Перенос lookup-состояния в planning context

Статус: выполнено.

Цель фазы: убрать разрозненные словари из orchestration-кода и держать layout
lookup state в одном месте.

Перенесено в `DrawingLayoutWorkspace` / `DrawingArrangeContext`:
- `semanticKindById`;
- selected frame sizes;
- actual frame rects;
- frame offsets;
- arranged position lookup;
- base/neighbor/section/detail relation lookup.

Правило фазы: layout policy не менялась. Короткоживущие локальные словари
оставляются только там, где они описывают один probe result до записи в
workspace.

### Фаза 3. Projection alignment через layout context

Статус: workspace-aware path выполнен; lazy `DrawingProjectionContext` остается
опциональным follow-up.

Цель фазы: projection alignment должен использовать легкие layout-факты и не
строить полный `DrawingViewContext`.

Сделано:
- projection alignment читает sheet/reserved/frame/topology/grid facts из
  workspace;
- collision checks используют planning view rects и frame offsets;
- GA grid axes загружаются только когда нужны для GA projection alignment;
- assembly/single-part anchors загружаются только для выравниваемого model id;
- тяжелый `DrawingViewContext` не нужен для обычного layout.

### Фаза 4. Рефакторинг orchestration в `fit_views_to_sheet`

Статус: выполнено.

Цель фазы: разделить scale selection, arrange/diagnostics и runtime apply
границу, сохранив публичный результат.

Сделано:
- scale selection использует workspace facts для semantic kind и frame sizes;
- arrange/parity/detail helpers читают planning facts через
  `DrawingLayoutWorkspace` / `DrawingArrangeContext`;
- keep-scale validation и candidate scale probing вынесены в private
  behavior-preserving steps;
- runtime `View` objects остаются только как Tekla apply/probe handles;
- public MCP/bridge result shape сохранен.

Опциональная уборка:
- разбить финальную часть `fit_views_to_sheet` на маленькие private steps:
  offset correction, projection alignment, centering, detail reposition,
  result/diagnostics assembly;
- делать это только если помогает следующей feature, не как отдельный
  обязательный refactor.

### Фаза 5. Аналитический layout pipeline

Статус: активное планирование / частичная инфраструктура реализована.

Цель фазы: перейти к контуру
`candidate -> validate -> score -> choose -> apply`, чтобы больше layout
решений оценивалось виртуально до Tekla `CommitChanges`.

Основные принципы:
- сравнивать layout candidates через `DrawingLayoutScorer`;
- хранить before/after `DrawingContext` cases как canonical regression dataset;
- сначала наблюдать и объяснять текущий результат, потом менять выбор;
- не менять public MCP/bridge result shape без отдельного planned contract step.

#### 5.1 Пассивная оценка layout

Статус: начальная реализация выполнена.

Первый шаг без изменения layout behavior:
- добавлены `DrawingLayoutCandidate` и `DrawingLayoutCandidateView`;
- текущий результат `fit_views_to_sheet` строится как candidate;
- candidate оценивается через `DrawingLayoutScorer`;
- score и diagnostics пишутся в trace/log;
- другой layout еще не выбирается.

#### 5.2 Модель layout candidate

Статус: начальная реализация начата.

Модель candidate должна описывать:
- virtual view origin;
- scale;
- frame/layout rect;
- semantic kind;
- placement side;
- sheet size, margins, reserved areas и title-block/table conflicts;
- view-overlap, out-of-sheet, projection-quality и movement diagnostics.

Сделано:
- `DrawingLayoutCandidateView` содержит explicit layout rect;
- conversion runtime `View -> candidate` изолирован в
  `DrawingLayoutCandidateBuilder`;
- `DrawingLayoutCandidateEvaluation` объединяет candidate, score и feasibility
  diagnostics.

#### 5.3 Оценка нескольких candidates

Статус: начальная реализация начата.

Цель: генерировать и сравнивать несколько виртуальных layout-вариантов из
одного workspace.

Сделано:
- `DrawingLayoutCandidateSelector` выбирает лучший candidate по feasibility,
  score и stable input order;
- `fit_views_to_sheet` пишет selection trace: index, rank, selected flag,
  rejection/selection reason;
- появились passive candidates: `planned-arranged`, `planned-centered`,
  `post-projection`, `final`;
- planned candidate construction вынесен в DTO/factory path:
  `DrawingLayoutPlannedView` + `DrawingLayoutCandidateFactory.FromPlannedViews`;
- `DrawingLayoutCandidateBuilder.ToPlannedViews` отделяет adapter boundary от
  будущего pure variant generator;
- `ViewGroupCenteringGeometry` вынесен как pure helper;
- `DrawingLayoutPlannedCenteringService.TryCenterViews` строит centered
  candidate без Tekla calls;
- `fit_layout_planned_variant` trace показывает moved view count, max/avg
  delta, group bbox before/after и reserved overlap before/after;
- summary generation изолирован в `DrawingLayoutPlannedVariantDiagnostics`.

#### 5.4 Применение выбранного candidate

Статус: реализовано, выключено по умолчанию.

Цель: подготовить safe apply выбранного candidate, но не включать реальное
применение до live validation.

Сделано:
- `DrawingLayoutCandidateApplyPlan` описывает применимость candidate и список
  view origin/scale moves;
- `fit_layout_apply_plan` и `fit_layout_apply_plan_move` пишутся без
  `Modify()` / `CommitChanges()`;
- `DrawingLayoutCandidateApplyService` валидирует apply plan против runtime
  view ids и поддерживает `DryRun` / `Apply`;
- `DrawingLayoutCandidateTeklaApplyAdapter` является Tekla-facing boundary:
  умеет set origin/scale и `Modify()`, но сам не вызывает `CommitChanges()`;
- `DrawingLayoutCandidateApplyGate` по умолчанию переводит все запросы в
  `DryRun`;
- guarded selected-candidate apply branch существует, но с default gate не
  выполняется;
- `DrawingLayoutCandidateApplyDeltaBuilder` сравнивает baseline final candidate
  с selected apply plan;
- `DrawingLayoutCandidateApplySafetyPolicy` блокирует real apply при missing
  baseline views или scale changes; movement пока только диагностируется.

#### 5.5 Regression cases для layout

Статус: инфраструктура готова; следующий шаг — live validation cases.

Цель: получить воспроизводимую базу before/after cases для оценки layout
изменений.

Рекомендуемый порядок:
1. Сохранить before/after `DrawingContext` snapshots для фиксированного набора
   live drawings через `DrawingCaseCaptureService`.
2. Сохранять trace-backed metadata: selected candidate, apply-plan summary,
   delta summary, safety decision, final score.
3. Запускать `fit_views_to_sheet` два раза на одном drawing и сравнивать second
   after-state с first after-state.
4. Покрыть baseline categories: standard projected neighbors, top/bottom
   sections, left/right sections, details with `DetailMark`, detail-like
   sections with `SectionMark`, GA grid-axis projection alignment,
   reserved/table/title-block avoidance.
5. Держать selected-candidate `Apply` выключенным, пока live cases не пройдут
   критерии приемки.

Критерии приемки для включения selected-candidate apply:
- нет missing baseline views в selected apply deltas;
- нет неожиданных scale changes;
- repeated runs converge: второй запуск дает zero или near-zero apply delta;
- reserved/table/title-block overlaps не увеличиваются;
- projection/detail placement diagnostics не регрессируют.

Сделано:
- `DrawingCaseSnapshotWriter` / `DrawingCaseCaptureService` сохраняют optional
  `LayoutDiagnostics` в `meta.json`;
- `DrawingCaseLayoutDiagnosticsFactory` мапит candidate selection, apply-plan,
  apply-delta и safety-decision в snapshot DTO;
- `FitViewsResult.LayoutDiagnostics` хранит internal diagnostics без изменения
  public JSON contract;
- `DrawingLayoutStabilityAnalyzer` сравнивает repeated-run after contexts;
- `DrawingCaseSnapshotReader` загружает saved `before.json`, `after.json`,
  `meta.json`;
- `DrawingLayoutRegressionCaseEvaluator` сравнивает saved repeated-run cases.

Осталось:
- снять фиксированный live validation set в Tekla;
- сохранить first-run и second-run cases для одних и тех же drawings;
- проверить `LayoutDiagnostics` и `LayoutStability` в `meta.json`;
- не включать selected-candidate `Apply`, пока критерии приемки не выполнены.

Не цель фазы 5:
- 5.1 не меняет поведение компоновки. Пассивная оценка должна сначала
  наблюдать и объяснять текущий результат.

### Фаза 6. Качество компоновки

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

Ориентир для регрессии:
- Fallback-путь из старой компоновки не удален: сначала projection-aware
  anchors, затем packing оставшихся видов вокруг anchors. После изменений в
  workspace/candidate/validation этот путь может не достигаться или его
  результат может проигрывать более строгим projection/budget constraints.
- Git-ссылка: `8a3c3c5^`, файл
  `src/TeklaMcpServer.Api/Drawing/ViewLayout/BaseProjectedDrawingArrangeStrategy.cs`,
  метод `Arrange(...)`: ветки `mode=anchor-then-maxrects`,
  `mode=maxrects-fallback`, `mode=shelf-fallback`.
- Дополнительный старый post-adjust reference: `8a3c3c5^`, файл
  `src/TeklaMcpServer.Api/Drawing/ViewLayout/TeklaDrawingViewApi.Layout.cs`,
  методы `TryCenterViewGroup(...)` и `TryRepositionDetailViews(...)`.
- Фаза 6 не должна возвращать старый код. Она должна восстановить свойство
  старого поведения в новом validation/candidate контуре: projection side
  является preference/score signal, а не абсолютным constraint.
- Проверять нужно не только `Arrange`, но и `EstimateFit` /
  `DiagnoseFitConflicts` / scale selection: они не должны отвергать масштаб до
  попытки более гибкого размещения.

#### 6.1 Гибкое размещение дополнительных видов

Первый шаг 6.1 — диагностика, а не изменение placement policy. Нужно понять,
где именно теряется хороший вариант: scale selection, `EstimateFit`,
`DiagnoseFitConflicts`, `TrySelectBaseRectWithBudgets`, `Arrange`, candidate
selection/scoring или apply safety.

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
(`SectionGroupSet`, `TryPlace...Section...`), но цель фазы 6 шире: не уменьшать
масштаб, пока не исчерпаны допустимые варианты размещения дополнительных видов.

Что нужно изменить:
- Добавить trace-backed diagnostics, которые показывают, на каком слое отвергнут
  текущий более крупный масштаб или более компактный layout candidate.
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
  relaxed или weak/off. В фазе 6.1 это может быть только diagnostic/scoring
  input без изменения public contract.
- Добавить diagnostics для площади: available sheet area, sum view area и fill
  ratio для рассматриваемого масштаба/кандидата.

Примечание по реализации:
- Для cross-axis использовать anchor целевой стороны:
  `mainSkeleton.GetAnchorOrBase("<target>", baseRect)`, где target это
  `right`, `left`, `top` или `bottom`.

Диагностика:
- Переиспользовать существующие `PlacementFallbackUsed`,
  `PreferredPlacementSide`, `ActualPlacementSide` в arranged/planned view
  diagnostics.
- Расширить trace event `section_stack_result` флагом `crossAxis=1`, когда
  использована cross-axis сторона.

Критерии приемки для 6.1:
- Trace показывает, где именно отвергнут лучший scale/layout candidate:
  scale selection, `EstimateFit`, `DiagnoseFitConflicts`, base-rect budgets,
  `Arrange`, candidate scoring или apply safety.
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

Статус: в проектировании.

#### Будущее. Агентная компоновка видов

В перспективе нужен отдельный инструмент для агентной компоновки видов.
Это не замена `fit_views_to_sheet`, а более свободный режим, где агент может
сам искать расположение видов, менять масштабы видов, пробовать разные варианты
и выбирать лучший по quality/score.

Граница безопасности:
- агент может двигать виды и менять масштабы видов только внутри явной
  layout-команды пользователя;
- команды диагностики, размеров, марок или проверки чертежа не должны
  произвольно двигать виды или менять их масштабы;
- результат агентной компоновки сначала должен быть представлен как план/preview с
  diagnostics, score, списком перемещенных видов и списком scale changes;
- apply должен быть отдельным явным шагом или защищен тем же safety gate, что и
  остальные layout-команды.

Этот режим может быть недетерминированным по поиску кандидатов, но результат
должен быть объяснимым: какие виды двигались, какие масштабы изменились, почему
выбран этот вариант, какие constraints сохранены, какие projection связи
ослаблены.

## Не цели

- Не переписывать layout policy во время context migration.
- Не загружать parts/bolts/hulls для обычного drawing layout.
- Не класть detailed mark/dimension geometry в `DrawingContext`.
- Не заменять marker-based reserved-area reading для layout tables.
- Не менять public tool contracts без отдельного плана.

## Валидационный baseline

Небольшой live-validation набор для `fit_views_to_sheet`:

- assembly drawings со standard projected neighbors;
- sheets с `Top` / `Bottom` sections;
- sheets с `Left` / `Right` sections;
- drawings с detail views и `DetailMark` anchors;
- detail-like sections с `SectionMark` anchors;
- GA drawings, которым нужен grid-axis projection alignment;
- repeated `fit_views_to_sheet` на одном drawing для проверки stability;
- reserved table/title-block avoidance.

## Общие критерии приемки

- Есть один активный roadmap для drawing layout.
- `DrawingContext` остается sheet-level source.
- `DrawingLayoutWorkspace` остается временным рабочим context, а не source of
  truth.
- `DrawingLayoutViewItem` остается дешевым layout-specific DTO.
- `DrawingViewContext` остается для dimensions/marks.
- `fit_views_to_sheet` остается стабильным во время context migration.
- Projection alignment использует только нужную lightweight geometry.
