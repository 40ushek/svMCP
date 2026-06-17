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

Статус: реализовано.

Сделано:
- добавлены `DrawingLayoutCandidate` и `DrawingLayoutCandidateView`;
- текущий результат `fit_views_to_sheet` строится как candidate;
- candidate оценивается через `DrawingLayoutScorer`;
- score и diagnostics пишутся в trace/log;
- другой layout может быть выбран диагностически, но real apply выбранного
  candidate остается выключенным через safety gate.

#### 5.2 Модель layout candidate

Статус: реализовано для текущего candidate set; расширение набора candidates
остается future work.

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

Статус: реализовано для текущих passive candidates; дальнейшее расширение
набора candidates остается future work.

Цель: генерировать и сравнивать несколько виртуальных layout-вариантов из
одного workspace.

Сделано:
- `DrawingLayoutCandidateSelector` выбирает лучший candidate по feasibility,
  score и stable input order;
- `DrawingLayoutScorer` учитывает fill ratio, uniform scale, view/reserved
  overlaps и `edgePenalty`;
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
- На live trace `planned-centered` может победить `planned-arranged`, если у
  него меньше `edgePenalty`.

#### 5.4 Применение выбранного candidate

Статус: реализовано, real apply выключен по умолчанию.

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

Начальный default projection strength по semantic kind:
- `BaseProjected` -> `Strong`;
- `Section` -> `Relaxed`;
- `Detail` -> `Weak`;
- `Other` / `Model3D` -> `Off`.

Это не константы и не жесткие правила по типу вида. Это стартовые значения,
которые candidate builder/scorer может ослаблять или усиливать по контексту:
масштаб, reserved areas, конфликты, ручные закрепления, тип чертежа, важность
конкретного view. Набор значений projection strength также должен оставаться
расширяемым: будущие режимы могут быть добавлены без изменения Tekla `View`.

Projection strength должно жить в layout/candidate модели, а не в Tekla `View`.
Нарушение projection strength не обязано делать candidate невозможным: оно
должно давать понятный penalty и reason, чтобы layout мог выбрать читаемый
масштаб и хорошую заполняемость вместо формально строгой, но плохой проекции.

Отдельно от projection strength нужен контракт scale flexibility: может ли
конкретный view менять масштаб и как именно. Это тоже свойство layout/candidate
модели, а не Tekla `View`.

Начальный default scale flexibility по semantic kind:
- `BaseProjected` -> `SameAsMain`;
- `Section` -> `SameAsMain`;
- `Detail` -> `CanBeLarger`;
- `Other` / `Model3D` -> `Fixed`.

Возможные значения:
- `Fixed`: масштаб не менять;
- `SameAsMain`: держать масштаб главной группы;
- `CanBeLarger`: можно увеличить относительно главной группы, если это улучшает
  читаемость и не ломает layout;
- `Independent`: масштаб можно выбирать отдельно.

Это стартовые значения, не константы. Для маленьких по площади второстепенных
views, например деталей, узлов и некоторых разрезов, `CanBeLarger` может быть
лучше читаемости, чем принудительное сохранение масштаба главной группы.

Целевая политика для `Section` / `Detail` scale flexibility описана в
разделе 6.10 (`SecondaryScalePolicy`).

Для оценки качества layout нужно считать:
- доступную площадь листа: usable sheet area минус union reserved/table areas;
- занятую площадь видов на текущем масштабе: `union(view rects)`, а не простую
  сумму площадей, чтобы пересечения не завышали качество;
- fill ratio: `union(view rects) / available sheet area`.

Этот коэффициент характеризует заполняемость чертежа: насколько эффективно
виды используют доступную площадь листа. Он помогает отличить плотную
компоновку от листа с большим пустым пространством. При этом fill ratio не
заменяет геометрическую проверку размещения: даже при высокой или достаточной
заполняемости views могут не помещаться из-за формы свободных зон, reserved
areas, projection constraints или конфликтов между видами.

Критерий качества не должен поощрять маленький масштаб ради формального
помещения всех видов. Первый приоритет — читаемый чертеж: главный видовой блок
должен быть достаточно крупным, а лист не должен выглядеть пустым. Если строгая
проекция требует слишком мелкого масштаба или дает большой пустой лист, такой
candidate должен проигрывать relaxed layout с читаемым масштабом и лучшим fill
ratio.

Практическая политика выбора масштаба:
- не вычислять "идеальный" масштаб формулой;
- перебрать стандартные масштабы и для каждого построить layout candidate;
- отбрасывать масштабы, где основные виды физически не помещаются или дают
  недопустимые конфликты;
- среди допустимых кандидатов предпочитать более крупный масштаб, если он дает
  приемлемую заполняемость листа;
- `fill ratio < 0.50` считать слабым сигналом: лист выглядит пустым, масштаб
  вероятно слишком мелкий или views разнесены слишком широко;
- ориентир хорошего диапазона: `0.55..0.80`;
- `fill ratio > 0.90` считать сигналом тесноты: возможны проблемы с размерами,
  марками и ручной читаемостью;
- меньший масштаб должен побеждать только если более крупный не помещается,
  дает серьезные конфликты или заметно худший layout score.

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
- Добавлено: trace-backed diagnostics показывают, на каком слое отвергнут
  текущий более крупный масштаб или более компактный layout candidate
  (`fit_scale_decision`, `fit_layout_decision`).
- Добавить relaxed packing feasibility check перед уменьшением масштаба:
  ответить на простой вопрос "есть ли место на листе вообще?". Для этого
  использовать MaxRects как критерий возможности, без немедленного применения
  результата. Проверка должна пробовать несколько порядков видов: по площади,
  ширине, высоте и исходному порядку.
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
- Trace `fit_scale_relaxed_packing` показывает, есть ли свободная упаковка
  всех видов на отвергнутом масштабе, каким порядком видов и какой MaxRects
  эвристикой она нашлась.
- Для нового planner path использовать `projected_group_*` trace вместо
  старого `section_stack_result`.

Критерии приемки для 6.1:
- Trace показывает, где именно отвергнут лучший scale/layout candidate:
  scale selection, `EstimateFit`, `DiagnoseFitConflicts`, base-rect budgets,
  `Arrange`, candidate scoring или apply safety.
- Если обычное размещение отвергло масштаб, trace показывает, есть ли место
  на листе вообще по relaxed MaxRects packing. Если место есть, уменьшение
  масштаба считается преждевременным до проверки других вариантов размещения.
- На проблемном чертеже новая логика должна пробовать cross-axis размещение
  дополнительных видов до уменьшения масштаба. Если такое размещение проходит
  все layout constraints на текущем масштабе, масштаб не должен уменьшаться.
- Preferred side по-прежнему пробуется первым, opposite side — вторым.
  Cross-axis используется только если оба same-axis варианта не подходят.
- В arranged/planned diagnostics у перенесенных видов заполнены
  `PreferredPlacementSide`, `ActualPlacementSide` и `PlacementFallbackUsed`.
- Trace показывает фактическую сторону через `ActualPlacementSide` и
  `PlacementFallbackUsed`; для planner path это пишется в `projected_group_*`
  событиях.
- Diagnostics показывают, была ли проекционная связь сохранена strict или
  ослаблена до relaxed/weak ради лучшей компоновки.
- Публичный JSON contract `fit_views_to_sheet` не меняется.

Публичный result contract в этой фазе не меняется.

Статус: superseded by 6.2. Отдельный placement-only cross-axis production path
не нужен, пока `ProjectedGroupLayoutPlanner` покрывает этот сценарий.

#### 6.2 Виртуальная проекционная группа

Цель: выбирать положение главного вида не отдельным жестким расчетом, а через
виртуальное построение основной группы видов. Реальные Tekla views не двигаются
во время поиска. В Tekla применяется только финальный выбранный план.

6.2 развивает 6.1 и включает ее cross-axis fallback в более общий виртуальный
planner. Если 6.2 реализуется первой, отдельный placement-only cross-axis шаг из
6.1 можно не делать как самостоятельный production path: он остается
диагностикой и минимальным fallback scope. Финальное целевое поведение должно
жить в `ProjectedGroupLayoutPlanner`.

Новый расчет должен быть отдельным путем рядом с текущей strict-логикой:
- `BaseProjectedDrawingArrangeStrategy` остается основной стратегией верхнего
  уровня;
- текущий strict layout пробуется первым;
- trigger для запуска planner: strict layout отказал на текущем масштабе и
  `DrawingPackingEstimator.CheckRelaxedMaxRectsFit(...)` вернул `fits=1`;
- если strict layout отказал и relaxed packing тоже вернул `fits=0`, planner не
  запускается: это сигнал переходить к меньшему масштабу;
- если виртуальный planner тоже не нашел валидный план, только тогда можно
  переходить к меньшему масштабу.

Рабочее имя класса: `ProjectedGroupLayoutPlanner`.

Он должен использовать существующие layout-модели:
- `DrawingLayoutWorkspace` — источник легких layout-фактов;
- `DrawingLayoutPlannedView` — виртуальная позиция вида;
- `DrawingLayoutCandidate` — полный вариант раскладки;
- `DrawingLayoutCandidateBuilder` — сборка кандидата;
- `DrawingLayoutScorer` — оценка кандидата;
- `ViewPlacementValidator` / reserved areas — проверка конфликтов;
- `MaxRectsBinPacker` — fallback-размещение видов вне основной группы.

Алгоритм фазы:
1. Создать виртуальное состояние layout в памяти.
2. Начать с base view как центра основной проекционной группы.
3. Пробовать несколько детерминированных порядков добавления видов:
   - сначала вертикальные стороны: Top/Bottom;
   - сначала горизонтальные стороны: Left/Right;
   - сначала виды большей площади;
   - сначала виды с более сильной проекционной связью.
4. Для каждого порядка пошагово добавлять view на его родную сторону группы.
5. После каждого добавления пересчитывать виртуальные прямоугольники всех видов
   основной группы, включая возможное смещение base view вместе с группой.
   Группа сдвигается минимально необходимым образом, чтобы после добавления
   нового view вся группа оставалась внутри допустимой области листа. Сдвиг
   проверяется по всем constraints: sheet margins со всех сторон, reserved
   areas/tables, gap между views и отсутствие пересечений между реальными rects
   views. Если минимальный сдвиг невозможен или после него нарушается любой
   constraint, view не добавляется в группу и уходит в fallback.
6. Проверять не внешний bbox "креста", а реальные прямоугольники всех views:
   внутри листа, без пересечений между собой, без пересечений с таблицами и
   reserved areas, с нужным gap.
7. Если view не может быть добавлен в основную группу в текущем варианте,
   оставить его как fallback-view для свободного размещения.
8. После построения основной группы разместить fallback-views в свободных
   зонах через packing/placement fallback.
9. Если хотя бы один fallback-view не помещается ни в одной допустимой
   свободной зоне, текущий виртуальный вариант отклоняется.
10. Собрать `DrawingLayoutCandidate`, посчитать diagnostics/score и выбрать
   лучший валидный вариант.

Сценарии проходов:
- `TopFirst`: сначала Top views;
- `BottomFirst`: сначала Bottom views;
- `LeftFirst`: сначала Left views;
- `RightFirst`: сначала Right views;
- `VerticalFirst`: сначала Top/Bottom;
- `HorizontalFirst`: сначала Left/Right;
- `LargeFirst`: сначала views большей площади;
- `ProjectionFirst`: сначала views с более сильной проекционной связью;
- `CurrentOrder`: порядок из текущей strict-логики:
  Top/Bottom/Left/Right neighbors и затем текущие section/detail/secondary
  groups в том порядке, в котором их сейчас обрабатывает
  `BaseProjectedDrawingArrangeStrategy`.

Каждый сценарий нужно пробовать не от одной фиксированной позиции главного
вида, а от нескольких стартовых позиций. Рабочий набор стартовых точек:
- `Center`;
- `LeftCenter`;
- `RightCenter`;
- `TopCenter`;
- `BottomCenter`;
- `TopLeft`;
- `TopRight`;
- `BottomLeft`;
- `BottomRight`.

Для каждой стартовой точки `MaxRectsBinPacker.TryInsertClosestToPoint(...)`
подбирает ближайший допустимый прямоугольник главного вида с учетом margins и
reserved areas. Дальше каждый сценарий строит независимый виртуальный layout
candidate от этого прямоугольника или возвращает reject reason.

Это не полный перебор всех перестановок, а фиксированный детерминированный
набор проходов: стартовая точка base view x порядок добавления views. Его можно
объяснить в trace.

Производительность:
- расчет выполняется только в памяти, реальные Tekla views не двигаются;
- для типичного чертежа с десятками views десятки проходов допустимы;
- первый implementation scope должен начинаться с фиксированного списка
  сценариев выше;
- позже список можно расширить до большего числа проходов, если diagnostics
  показывает, что базовых сценариев не хватает;
- trace должен логировать summary по всем сценариям, а подробности — только для
  выбранного candidate и для лучших rejected candidates, чтобы не засорять лог.

Порядок попыток для fallback-views:
- сначала пробовать родную сторону view, если она еще имеет свободную область;
- затем opposite side;
- затем cross-axis стороны;
- внутри одного класса сторон выбирать сторону с наибольшим доступным
  свободным прямоугольником под этот view;
- сами fallback-views размещать в порядке убывания площади, чтобы крупные виды
  не оставались последними.

Если fallback-view не помещается ни на одной стороне и ни в одной свободной
области, это не частичная удача. Это reject текущего candidate. Если все
виртуальные candidates rejected, масштаб можно уменьшать.

Пример пошагового виртуального расчета:

```text
Лист: 420 x 297

Шаг 1: ставим FrontView в центр: (210, 148).

Шаг 2: добавляем TopView сверху.
TopView.Y = FrontView.MaxY + gap = 248.
248 > 297: TopView выходит за лист.
Сдвигаем всю группу вниз на 20 мм.
Теперь FrontView.Y = 128, TopView.Y = 228: группа влезает.

Шаг 3: добавляем Section1 сверху TopView.
Section1.Y = TopView.MaxY + gap = 271.
Section1.MaxY = 314 > 297: Section1 не влезает.
Пробуем сдвинуть всю группу вниз, но упираемся в нижний край листа.
Section1 не включается в верхний стек и уходит в fallback.

Шаг 4: Section1 из fallback пробуем справа от FrontView.
Справа есть свободная зона: Section1 влезает, ставим справа.
```

Смысл примера: при добавлении view сначала двигается вся виртуальная группа.
Если группа уже не может быть сдвинута без нарушения границ/таблиц/зазоров,
новый view не ломает масштаб, а переходит в fallback-размещение.

Важные правила:
- предварительный стек не является жестким резервом места;
- view считается частью стека только если виртуальная проверка всей группы
  после добавления view прошла;
- при добавлении view может смещаться вся основная группа, а не только новый
  view;
- проекционная сторона остается preferred placement, но не абсолютным
  constraint;
- масштаб не уменьшается, пока не проверены strict layout, виртуальная
  проекционная группа и fallback-размещение отложенных views.

Диагностика:
- писать trace по каждому варианту порядка: order name, added views,
  deferred views, reject reason;
- писать trace по стартовым позициям главного вида:
  `projected_group_base_candidates`;
- в result каждого сценария писать стартовую позицию: `base=Center`,
  `base=RightCenter` и т.п.;
- для каждого отказа указывать blocker: sheet bounds, table/reserved area,
  view overlap, gap, no fallback space;
- писать выбранный base view rect как результат виртуального плана, а не как
  отдельное предварительное решение;
- показывать, какие views остались на preferred side, а какие ушли в fallback.

Текущий production status:
- `ProjectedGroupLayoutPlanner` подключен к `EstimateFit` и `Arrange`;
- `EstimateFit` вызывает planner до уменьшения масштаба;
- `Arrange` применяет custom plan из planner, если он найден;
- planner пробует несколько стартовых позиций главного вида;
- planner пробует несколько порядков views: top/bottom/left/right,
  vertical/horizontal, large-first, projection-first и current order;
- planner пробует несколько `(margin, gap)` candidates:
  текущие значения, `(5,4)`, `(8,4)`, `(10,4)`, `(10,6)`;
- fallback-placement сначала пытается разместить view ближе к preferred side,
  затем использует общий packing;
- `ActualPlacementSide` вычисляется постфактум относительно base rect;
- score учитывает compactness, удаленность base от центра листа,
  side mismatch penalty и `edgePenalty`;
- trace пишет `selectedBase`, `selected`, `margin`, `gap`, `sidePenalty`,
  `edgePenalty`, fallback views и reasons.

Текущий live result:
- на проблемном 6-view чертеже scale selection выбирает `1:20`, потому что это
  первый влезший масштаб;
- часть Top views уходит в fallback на Left, потому что сверху физически не
  хватает места;
- финальный candidate selection может выбрать `planned-centered`, если его
  `edgePenalty` меньше.

Критерии приемки для 6.2:
- На проблемном чертеже, где relaxed packing говорит `fits=1`, алгоритм
  запускает `ProjectedGroupLayoutPlanner` до уменьшения масштаба.
- Если `ProjectedGroupLayoutPlanner` нашел `result=ok` на текущем масштабе,
  `EstimateFit` не должен отклонять этот масштаб.
- Trace показывает хотя бы один виртуальный вариант основной группы и причину
  его принятия или отказа.
- Trace показывает стартовые позиции главного вида и выбранную позицию для
  принятого candidate.
- Главный вид может менять виртуальную позицию при добавлении видов в группу.
- Проверка валидности использует реальные прямоугольники views, а не только
  общий bbox группы.
- Если найден валидный виртуальный candidate на текущем масштабе, масштаб не
  уменьшается.
- Если fallback-view размещен не на preferred side, trace показывает
  `viewId`, `PreferredPlacementSide`, `ActualPlacementSide`,
  `PlacementFallbackUsed=1` и scenario, в котором это решение принято.
- В Tekla применяются только финальные planned placements, промежуточные
  виртуальные варианты реальные views не двигают.

Статус: реализовано в production path; требуется live validation на нескольких
чертежах перед дальнейшим усложнением.

#### 6.3 Quality-aware выбор масштаба

Текущая политика выбора масштаба: scale selection идет по кандидатам от более
крупного масштаба к более мелкому и останавливается на первом масштабе, который
проходит layout feasibility. Например, если `1:20` влез, `1:25` уже не
проверяется. Это нормальное базовое поведение: читаемость деталей важнее, чем
небольшое улучшение пустых полей.

Но для плотных чертежей нужен будущий режим сравнения качества нескольких
влезших масштабов. Смысл: не уменьшать масштаб автоматически, а разрешить
перейти с более крупного масштаба на чуть меньший только если layout заметно
лучше.

Предлагаемая политика:
- сначала найти первый влезший масштаб как сейчас;
- затем опционально проверить 1-2 следующих более мелких масштаба;
- для каждого масштаба построить layout candidate и посчитать score;
- оставить более крупный масштаб, если разница качества небольшая;
- выбрать более мелкий масштаб только если он существенно лучше по качеству
  компоновки.

Критерии "существенно лучше" должны быть численными, например:
- заметно меньше `edgePenalty`;
- меньше fallback views или меньше side mismatch penalty;
- меньше пересечений/diagnostics;
- общий layout score лучше не менее чем на заданный порог, например 10-15%.

Важно: это не замена текущей политики и не срочный фикс. Текущее поведение
`1:20` вместо `1:25` допустимо, если `1:20` физически влезает и не создает
конфликтов. Quality-aware scale selection нужен только для случаев, где более
крупный масштаб дает слишком плотный или визуально плохой чертеж.

Diagnostics:
- `fit_scale_decision` должен явно писать, что выбран первый влезший масштаб
  или что включено quality-aware сравнение;
- если более мелкий масштаб отвергнут, trace должен показывать его score и
  причину: улучшение недостаточно;
- если более мелкий масштаб выбран, trace должен показывать, какие метрики
  улучшились и почему уменьшение масштаба оправдано.

Критерии приемки:
- по умолчанию поведение не меняется: первый влезший масштаб продолжает
  выбираться;
- при включенной quality-aware политике `1:25` может победить `1:20` только
  при явном выигрыше по score/diagnostics;
- trace объясняет, почему масштаб сохранен или уменьшен;
- trace явно различает diagnostic selected candidate и фактически примененный
  layout: если safety gate оставил `DryRun`, выбранный candidate объясняет
  качество, но не обязан физически двигать views.

Статус: future / design note. Не реализовывать до проверки нескольких реальных
чертежей и согласования порогов качества.

#### 6.4 Fallback-stack projection alignment

Проблема: после `ProjectedGroupLayoutPlanner` несколько views могут уйти в
одну fallback-зону. Например, на текущем 6-view assembly drawing C-C и B-B
оказались слева как отдельные fallback views. Геометрически это читается как
стек. Первый проход уже умеет распознать такой стек и выровнять views внутри
него между собой, но порядок views внутри стека пока берется из текущей
fallback-раскладки, а не из положения линий разреза на главном виде.

Цель: если несколько fallback views имеют общий `PreferredPlacementSide` и
общий `ActualPlacementSide`, рассматривать их как локальный fallback-stack.
Внутри такого стека можно мягко восстановить проекционную связь между views
самой группы, не заставляя каждый view выравниваться с главным видом.

Пример:
- `TopView`, C-C и B-B не помещаются сверху от главного вида;
- planner переносит их в `ActualPlacementSide=Left`;
- `TopView` остается выше;
- C-C и B-B образуют локальный стек ниже;
- C-C и B-B можно попробовать выровнять между собой по X, если это не ломает
  margins, reserved areas, gaps и пересечения с другими views.

Сделано:
- Вынесен общий helper `ProjectionAlignmentMoveHelper` для projection move
  validation/application: построение `ProjectionViewState`, расчет frame rect,
  проверка через `ViewPlacementValidator`, применение move и обновление
  `ArrangedView`.
- `DrawingProjectionAlignmentService` использует helper без изменения
  основного projection behavior.
- Добавлен отдельный шаг `ApplyFallbackStackAlignment(...)` после
  planner/fallback placement.
- Fallback views группируются по:
  `PreferredPlacementSide`, `ActualPlacementSide`, `ViewType`,
  `PlacementFallbackUsed=1`.
- Для `PreferredPlacementSide=Top/Bottom` внутри группы пробуется alignment по
  X; для `Left/Right` — по Y. Используется существующее правило
  `DrawingProjectionAlignmentMath.TryGetSectionAlignmentAxis(...)`.
- Anchor внутри группы выбирается детерминированно по текущему положению в
  стеке.
- Остальные views двигаются к anchor только если helper подтверждает, что move
  не нарушает sheet margins, reserved areas и view overlaps.
- Если alignment не проходит, исходная fallback placement остается без отката
  всей компоновки.

Добавлено следующим шагом:
- `DetailRelationResolver.BuildSectionMarkRelations(...)` строит связь
  `SectionMark -> SectionView` и берет midpoint линии разреза на owner view.
- Fallback-stack теперь упорядочивает section views по реальному положению
  линии разреза/проекции на главном виде, а не по текущему положению после
  packer.
- Для стеков слева/справа views сортируются сверху вниз по координате линии на
  главном виде.
- Для стеков сверху/снизу views сортируются слева направо по координате линии
  на главном виде.
- Если координату линии разреза найти нельзя, остается текущий
  детерминированный порядок как fallback.
- После сортировки локальный стек пересобирается с сохранением gap и validation
  через тот же `ProjectionAlignmentMoveHelper`.

Что осталось согласовать с candidate scoring/apply:
- После `fallback_stack_order_result applied` финальный layout уже содержит
  правильный порядок fallback-stack, но diagnostic candidate `planned-centered`
  может быть построен от snapshot до перестановки.
- Из-за этого `fit_layout_apply_delta` может показывать обратную перестановку
  C-C/B-B, хотя фактический `final` уже правильный.
- Нужно синхронизировать candidate snapshot после stack-order или при равном
  score предпочитать `final`, если именно он содержит примененный stack-order.

Diagnostics:
- Добавить trace `fallback_stack_alignment_group`:
  preferred side, actual side, view ids, выбранный anchor.
- Добавить trace `fallback_stack_alignment_attempt`:
  view id, anchor id, axis, delta, candidate rect.
- Добавить trace `fallback_stack_alignment_result`:
  applied/rejected и причина reject.

Критерии приемки:
- Текущая основная проекционная связь с главным видом не ухудшается.
- Если fallback views уже лежат валидно, неуспешный stack alignment не меняет
  их позиции.
- На текущем чертеже C-C/B-B распознаются как fallback-stack и получают
  попытку локального alignment.
- Если в fallback-stack несколько разрезов, их порядок должен соответствовать
  порядку линий разреза на главном виде, когда такая связь доступна.
- Все moves проходят тот же validator, что и обычная projection alignment:
  sheet margins, reserved areas и view overlaps.
- Trace объясняет, был ли stack alignment применен или отклонен.
- После примененного stack-order `fit_layout_apply_delta` не должен показывать
  обратную перестановку тех же section views.

Статус: implementation in validation. Helper refactor, первый fallback-stack
alignment pass и projection-aware ordering внутри fallback-stack добавлены.
На текущем чертеже stack-order применился. Следующий шаг — синхронизировать
candidate scoring/apply snapshot с примененным fallback-stack order.

#### 6.5 Учет смещения BBox относительно origin

Проблема: часть views имеет реальный frame/BBox, смещенный относительно
`View.Origin`. Если placement считает прямоугольник как центрированный на
origin, то origin может оказаться внутри допустимого margin, но реальный
`BBox.MinX` или `BBox.MinY` выйдет за рамку листа.

Пример:
- margin задан корректно: `5 мм`;
- packer ставит origin около `24.5 мм`;
- у view frame смещен относительно origin примерно на `24.5-36 мм`;
- в результате реальный `BBox.MinX` становится около `0 мм`, хотя origin не
  нарушает margin.

Это не проблема настройки отступов. Это проблема геометрической модели
placement: алгоритм должен размещать не абстрактный `width x height` вокруг
origin, а реальный frame rect с offset от origin.

Подтверждение по Tekla API:
- у `View` отдельно есть `Origin`, `FrameOrigin`, `Width` и `Height`;
- `FrameOrigin` описывает смещение frame относительно origin view;
- `GetAxisAlignedBoundingBox()` возвращает фактическую bounding box геометрию;
- значит `Origin` нельзя считать центром видимого frame/BBox без проверки.

Ссылки:
- `View` properties:
  https://developer.tekla.com/doc/tekla-structures/2026/view-properties-69351
- `GetAxisAlignedBoundingBox()`:
  https://developer.tekla.com/doc/tekla-structures/2024/get-axis-aligned-bounding-box-method-25432

Что нужно изменить:
- уже используется `DrawingViewFrameGeometry` /
  `DrawingLayoutWorkspace.SetFrameOffsets(...)` как источник фактических
  rect/offset facts;
- `BaseProjectedDrawingArrangeStrategy.ApplyPlan(...)` применяет placement как
  frame center и вычисляет Tekla origin через
  `origin = targetFrameCenter - frameOffset`;
- trace `view_frame_offset_apply` показывает примененную offset-correction;
- post-arrange parity сравнивает planned/actual rects.

Осталось проверить/доделать:
- убедиться, что все MaxRects/fallback ветки используют frame-size/frame-rect
  как canonical geometry, а не centered origin-size approximation;
- добавить targeted regression на несимметричный BBox;
- после применения placement проверять parity по реальному BBox, а не только по
  расчетному centered rect, во всех fallback paths.

Критерии приемки:
- при margin `5 мм` ни один final view не имеет `BBox.MinX < 5`,
  `BBox.MinY < 5`, `BBox.MaxX > sheetWidth - 5`,
  `BBox.MaxY > sheetHeight - 5`;
- для B/C sections с несимметричным BBox trace показывает frame offset;
- planner/fallback не выбирает позицию, где origin допустим, но реальный BBox
  выходит за margin;
- проблема воспроизводимой 6-видовой компоновки исправлена без увеличения
  margin.

Статус: частично реализовано; нужен targeted regression/proof по всем fallback
веткам.

#### 6.6 Quality scoring для выбора лучшей раскладки

Статус: начато; `preferredSidePenalty`, `compactnessPenalty`,
`stackOrderPenalty` и `projectedAxisPenalty` реализованы.

Цель: если несколько раскладок физически валидны, выбирать не просто первый
вариант, который влез, а лучший для чтения чертежа.

Уже есть:
- `DrawingLayoutScorer` считает общий score candidate;
- `edgePenalty` штрафует близость views к краям листа;
- `preferredSidePenalty` штрафует candidate, где view ушел не на
  `PreferredPlacementSide`;
- `compactnessPenalty` штрафует слишком растянутый общий bbox всех views:
  `layoutBoundingBoxArea / usableSheetArea`;
- `projectedAxisPenalty` штрафует раскладки, где основные проекционные views
  не стоят на общей оси с `FrontView`: `Top/Bottom` стремятся к тому же `X`,
  `Left/Right/Back` стремятся к тому же `Y`;
- candidate selection умеет сравнивать несколько candidates;
- при равном score выбранный `final` candidate может побеждать planned
  snapshot, чтобы не предлагать обратное движение уже примененной раскладки.
- trace `fit_layout_score` пишет `edgePenalty`, `preferredSidePenalty`,
  `compactnessPenalty`, `stackOrderPenalty`, `projectedAxisPenalty`;
- trace `fit_layout_stack_order_score` пишет expected/actual порядок
  fallback-stack группы;
- `stackOrderPenalty` является мягким штрафом, а не layout constraint: если
  правильный порядок не влезает, валидная раскладка с нарушенным порядком все
  равно может победить;
- на live 6-view drawing `compactnessPenalty` уже различил `final` и
  `planned-centered`: `final` получил меньший compactness penalty и победил по
  score.

Как работает `stackOrderPenalty`:
1. `DrawingLayoutCandidateBuilder` строит fallback-stack groups по
   `PreferredPlacementSide`, `ActualPlacementSide`, `ViewType`.
2. Expected order берется через
   `DetailRelationResolver.BuildSectionMarkRelations(...)`.
3. Actual order берется из candidate rects на фактической стороне.
4. `DrawingLayoutScorer` считает inversion ratio и умножает его на небольшой
   вес.

Как работает `projectedAxisPenalty`:
1. Находит reference view: `FrontView`, затем `TopView`, `BottomView`,
   `BackView`, затем самый большой `BaseProjected`.
2. Для основных проекционных views считает отклонение центра от оси reference
   view.
3. Views на `Top/Bottom` сравниваются по `X`, views на `Left/Right` — по `Y`.
4. Это мягкий штраф: если иначе views не влезают, валидная раскладка все равно
   может победить.

Приоритет реализации:
1. Проверить live logs после `stackOrderPenalty` на чертеже с B-B/C-C.
2. Если штраф слишком слабый или сильный, отрегулировать
   `StackOrderPenaltyWeight`.
3. Проверить live logs: `projectedAxisPenalty` должен отличать вариант, где
   основные views стоят одной линией, от визуально разорванного варианта.

Критерии приемки:
- Если две раскладки валидны, выбирается та, где больше views осталось на
  preferred side, при равных overlaps и scale.
- Если число fallback views одинаковое, выбирается более компактная раскладка
  и/или вариант дальше от краев листа.
- Если fallback-stack уже отсортирован по section marks, candidate scoring не
  должен выбирать snapshot, который возвращает старый порядок.
- Если правильный порядок нарушает constraints, алгоритм выбирает валидную
  раскладку, а не отклоняет ее из-за `stackOrderPenalty`.
- Если две раскладки валидны, основные проекционные views по возможности
  остаются на одной оси с `FrontView`.
- Если выравнивание основных views по оси невозможно без overlaps/reserved
  conflicts, алгоритм выбирает валидную раскладку и только снижает score.
- Trace объясняет выбор коротко: score total и основные penalty components.
- Публичный JSON contract `fit_views_to_sheet` не меняется.

Сделано в коде:
- `preferredSidePenalty`: `dfdeedc Add preferred side layout scoring`;
- `compactnessPenalty`: `7f84344 Add compactness layout scoring`;
- `stackOrderPenalty`: мягкий штраф по expected/actual fallback-stack order;
- `projectedAxisPenalty`: мягкий штраф за разрыв оси основных проекционных
  views относительно `FrontView`.

#### 6.7 Arrange existing views без изменения масштаба

Статус: частично реализовано.

Цель: дать команду/режим, который расставляет уже существующие views, но не
меняет их scale.

Важно: не возвращаться к старому отдельному алгоритму. Команда должна быть
тонкой оберткой над новым layout pipeline:

- scale candidate loop отключен;
- текущий scale каждого view сохраняется;
- реальные bbox/frame rect читаются на текущем масштабе;
- `ProjectedGroupLayoutPlanner` используется для размещения;
- projection alignment, fallback-stack ordering, centering и scoring остаются
  теми же;
- apply меняет только `Origin`, не `Scale`.

Предлагаемый режим:
- `ScalePolicy = PreserveExistingScales`;
- отдельная команда `arrange_views_only`, которая внутри вызывает тот же
  pipeline.

Открытое решение по public API:
- отдельная команда `arrange_views_only` может быть не обязательна, потому что
  существующий `fit_views_to_sheet keepScale=true` уже покрывает тот же сценарий;
- если цель — минимальный API surface, лучше оставить только
  `fit_views_to_sheet keepScale=true`;
- если цель — более понятный agent/user-facing command, можно оставить
  `arrange_views_only` как alias без отдельного алгоритма;
- важно: независимо от имени команды должен быть один implementation path через
  `PreserveExistingScales`.

Сделано:
- `fit_views_to_sheet keepScale=true` уже использует
  `DrawingScalePolicy.PreserveExistingScales`;
- добавлена явная bridge/MCP-команда `arrange_views_only`;
- команда вызывает `FitViewsToSheet(..., PreserveExistingScales, ...)`, то есть
  не поддерживает отдельный старый алгоритм.

Зачем нужно:
- пользователь уже выставил нужные масштабы вручную;
- нужно только разложить views аккуратно;
- можно проверить, влезают ли текущие масштабы без автоматического уменьшения;
- будущий агентный режим сможет отдельно менять scale отдельных views, а затем
  запускать размещение без нового глобального scale selection.

Критерии приемки:
- команда не меняет `View.Attributes.Scale`;
- если текущие масштабы не влезают, возвращается diagnostic, а не тихое
  уменьшение масштаба;
- trace пишет, что scale selection отключен / current scales preserved;
- scoring использует те же penalty components:
  `edgePenalty`, `preferredSidePenalty`, `compactnessPenalty`,
  `stackOrderPenalty`;
- public behavior старого `fit_views_to_sheet` не меняется.

#### 6.8 3D/Other views не должны менять масштаб

Статус: реализовано базовое правило.

Проблема из live log: `_3DView` попадал в `ScalePolicy=UniformAllNonDetail`
как `driver=1`, из-за этого:
- 3D view пересчитывался на candidate scale;
- крупные масштабы могли отклоняться из-за 3D view;
- общий масштаб основных видов выбирался хуже.

Правило:
- `BaseProjected` и `Section` могут быть scale drivers;
- `Detail` сохраняет текущий scale;
- `Other` (`_3DView` и похожие вспомогательные views) сохраняет текущий scale;
- `Other` участвует в размещении после выбора масштаба, но не должен заставлять
  уменьшать масштаб основных видов.

Критерии приемки:
- в `fit_scale_inputs` для `_3DView` ожидается `driver=0`, если на чертеже есть
  `BaseProjected`/`Section` views;
- в `fit_scale_candidate` scale/frame `_3DView` не пересчитывается на candidate
  scale;
- если candidate scale отклонен, `_3DView` не должен быть единственной причиной
  выбора меньшего масштаба для основных видов;
- apply не меняет `View.Attributes.Scale` для `_3DView`.

#### 6.9 Настоящий DryRun для layout pipeline

Статус: частично реализовано; `allowTeklaMutation` временно возвращён к
`applyMode == FinalOnly` (коммит `1fe8677`) — технический долг, требует замены
на раздельные флаги.

⚠️ Текущее состояние (после `1fe8677`): `allowTeklaMutation = FinalOnly` снова
разрешает ранние `Modify()`/`CommitChanges()` во всём старом pipeline (scale
probe, arrange strategy, projection, centering, detail reposition). То есть
`FinalOnly` применяет изменения дважды: сначала через старый pipeline, потом
через selected-candidate apply adapter. Это работает корректно, но не
соответствует цели 6.9.

Цель 6.9 остаётся: разделить `allowTeklaMutation` на два отдельных флага:
- `allowVirtualPlan` — всегда `true`; весь pipeline работает виртуально;
- `allowApply` — `true` только для `FinalOnly`; единственное место `Modify()`.

Сделано:
- `DebugPreview`/`DryRun` передает в arrange context `ApplyChanges=false`;
- `FinalOnly` тоже строит layout виртуально и применяет выбранный план один раз
  в конце через candidate apply adapter;
- стратегии раскладки считают `ArrangedView`, но не вызывают `view.Modify()` при
  `ApplyChanges=false`;
- scale probe в `DryRun` стал виртуальным: frame size оценивается от исходного
  scale, без временного изменения `View.Attributes.Scale` и без
  `CommitChanges()`;
- выбранные scale сохраняются в `DrawingLayoutWorkspace.SelectedScalesById`,
  поэтому planned candidates несут целевой масштаб, даже если Tekla view еще не
  изменен;
- projection alignment, group centering и detail reposition пропускают реальные
  `Modify()`/`CommitChanges()`;
- `fit_layout_apply_execution` стал единственным местом, где `FinalOnly`
  вызывает `view.Modify()` для выбранного candidate;
- scale changes разрешены apply safety policy только для scale-changing режима,
  а режимы сохранения текущего масштаба продолжают блокировать изменение scale.

Осталось:
- проверить на реальных чертежах, что `FinalOnly` применяет тот же candidate,
  который виден в trace как selected;
- вынести projection alignment в полноценную виртуальную операцию над plan, а не
  только пропускать его в plan-only pipeline;
- добавить regression-тест: `DebugPreview` не меняет drawing, `FinalOnly` делает
  один commit выбранного plan.

Исходная проблема: текущий `applyMode=DryRun` защищал только поздний candidate apply
(`fit_layout_apply_execution`), но не весь pipeline.

Раньше часть старого pipeline применяла изменения раньше:
- `_arrangementSelector.Arrange(...)` может вызывать `view.Modify()`;
- frame offset correction после Arrange тоже меняет `Origin` и вызывает
  `Modify()`;
- projection/centering/detail reposition также могут менять views;
- затем выполняется ранний `activeDrawing.CommitChanges()` до финального
  candidate safety gate.

Следствие было такое: в trace могло быть `effectiveMode=DryRun` и `appliedMoves=0`, но
чертеж уже мог измениться раньше через базовый Arrange/Commit.

Цель: разделить layout pipeline на два этапа:
- `Plan` - только расчет позиций, масштабов, score и diagnostics;
- `Apply` - единственное место, где выполняются `view.Modify()` и
  `CommitChanges()`.

Что нужно изменить:
- перенести projection alignment в виртуальную операцию над layout plan;
- держать запрет `Modify()` внутри planner/scoring/probe веток;
- `DryRun` должен строить тот же финальный план, что и apply режим, но не
  менять drawing;
- `DebugPreview` может возвращать plan/diagnostics без изменения чертежа;
- `FinalOnly`/apply mode применяет уже выбранный plan один раз в самом конце;
- scale probing остается виртуальным для plan-only pipeline.

Trace:
- отдельно логировать `plan`, `probe`, `preview`, `apply`;
- в apply trace писать reason, mode, количество измененных views и факт commit;
- `fit_layout_apply_execution appliedMoves=0` должен означать, что drawing
  реально не менялся.

Критерии приемки:
- при `applyMode=DryRun` не вызываются `Modify()` и `CommitChanges()`;
- после `DryRun` повторное чтение drawing показывает те же origins/scales, что
  до запуска;
- trace показывает выбранный candidate, score и финальные позиции views;
- в обычном apply режиме результат совпадает с планом из DryRun;
- `fit_layout_apply_execution appliedMoves=0` означает, что drawing реально не
  изменился;
- ранний `activeDrawing.CommitChanges()` не используется для виртуального
  pipeline;
- roadmap/trace ясно различают `plan`, `probe`, `preview`, `apply`.

#### 6.10 SecondaryScalePolicy — гибкое управление масштабом второстепенных видов

Статус: design / future.

Проблема: текущая `DrawingScalePolicy.UniformAllNonDetail` принудительно
приводит все `Section` к одному общему масштабу. Если автор намеренно поставил
крупный масштаб на маленьких сечениях, это теряется.

Контракт:

```
SecondaryScalePolicy (параметр fit_views_to_sheet, независим от DrawingScalePolicy):
  SameAsMain               — текущее поведение (дефолт)
  PreserveIfNotSmaller     — сохранить originalScale если он не мельче mainScale
                             (denominator <= main denominator; т.е. 1:5 не мельче 1:10)
  PreserveLargerIfFits     — сохранить originalScale если он крупнее mainScale
                             (denominator < main denominator) AND estimate fits
  AllowLargerIfFits        — выбрать максимальный стандартный масштаб при котором fits

Применяется только к видам с ScaleFlexibility = CanBeLarger или Independent.
ScaleFlexibility = Fixed или SameAsMain игнорируют политику.
```

Дефолты `ScaleFlexibility` по semantic kind (уже реализованы):
- `BaseProjected` → `SameAsMain` (scale driver)
- `Section` → `SameAsMain` (по умолчанию — политика не действует)
- `Detail` → `CanBeLarger`
- `Other` / `Model3D` → `Fixed`

Bootstrap для Section: **решено** — context-aware вариант.
- `ScaleFlexibilityResolver.Resolve(kind, policy)` повышает `Section` до
  `CanBeLarger` при `policy != SameAsMain`; глобальный default не изменён.
- `DrawingLayoutWorkspace.GetScaleFlexibility(id, policy)` — policy-aware
  перегрузка для вызовов внутри layout pipeline.

TODO выполнено: `SecondaryScalePolicy` wired в `fit_views_to_sheet`.
- ~~Логика promotion продублирована~~ — устранено: workspace-overload делегирует
  в `ScaleFlexibilityResolver.Resolve`.
- ~~`TraceSecondaryScaleDecision` без policy~~ — устранено: policy передаётся
  до trace, `scaleFlex` теперь отражает активную политику.

TODO перед реализацией `PreserveLargerIfFits`:
1. **`ResolveSelectedScales`** принимает `secondaryScalePolicy`, но пока не
   использует его — нужно синхронизировать с логикой estimate/apply.
2. **`ResolveTargetScale`** вызывается при реальном `Modify()` без policy —
   estimate (с policy) и apply (без policy) разойдутся. Синхронизировать оба
   пути перед включением реального поведения.
3. **Группировка по стороне**: решение о сохранении originalScale принимать
   для группы одной стороны (все Left-секции, все Right-секции), а не
   per-view — иначе стек будет выглядеть несогласованно.

Связь с существующим кодом:
- `ScaleFlexibility` уже живёт в `DrawingLayoutWorkspace` и `DrawingLayoutViewItem`;
- `DrawingScalePolicy` управляет выбором общего масштаба (кто driver);
- `SecondaryScalePolicy` управляет тем, что делают secondary views
  относительно выбранного mainScale;
- оба параметра ортогональны и не пересекаются.

Важные правила:
- secondary view никогда не становится мельче mainScale (даже при
  `PreserveIfNotSmaller`): если original denominator > main denominator (т.е. originalScale мельче mainScale), view приводится к
  mainScale;
- решение принимается не для каждого view отдельно, а для группы одной стороны
  (Left-стопка, Right-стопка), чтобы стек сечений выглядел согласованно;
- проверка "fits" для `PreserveLargerIfFits` использует estimate с tolerance
  `ScaleEstimateOversizeTolerance = 1.05` (уже реализован для pre-reject);
- в ответе: поле `scaleDowngradedViews: [{id, originalScale, appliedScale}]`
  сигнализирует о тихом откате масштаба;
- разброс масштабов ограничен: не более двух различных значений на листе
  (mainScale + один более крупный для групп secondary views);
- `AllowLargerIfFits` реализовывать последним: требует критерий "насколько
  укрупнить" (не более одного шага вверх по стандартному ряду на старте).

Стандартный ряд масштабов (восходящий порядок):
`1:100, 1:75, 1:50, 1:40, 1:30, 1:25, 1:20, 1:15, 1:10, 1:8, 1:5, 1:4, 1:2.5, 1:2, 1:1`

Критерии приемки:
- при `SameAsMain` поведение не меняется относительно текущего;
- при `PreserveLargerIfFits` view с originalScale=5 при mainScale=10 сохраняет
  `scale=5`, если estimate показывает что он помещается;
- при откате trace пишет `secondary-scale-downgrade view=... originalScale=...
  appliedScale=... reason=...`;
- все views одной стороны получают одинаковый итоговый масштаб;
- `scaleDowngradedViews` в public result правильно отражает откаты;
- `AllowLargerIfFits` не реализуется, пока три предыдущих варианта не
  проверены на нескольких реальных чертежах.

#### 6.11 Anchor-driven размещение маленьких секций

**Проблема.** Маленькая секция (например, G-G — горизонтальный разрез балки,
~101×85 мм при 1:10) попадает в Bottom-стопку рядом с BackView/BottomView
(~700×160 мм) — занимает целую зону, выглядит потерянной.

**Критерий "маленькая".** Размер секции вдоль направления стопки < порог ×
медиана того же размера по группе. Порог ~0.4 как стартовое значение.

Направление стопки зависит от её ориентации (`stackOrientation`), которая
должна быть вычислена до outlier-фильтра:
- стопка вертикальная (Left/Right) → сравниваем `height`;
- стопка горизонтальная (Top/Bottom) → сравниваем `width`.

Это работает корректно для балок (длинная в X → маленький height в Left-стопке)
и для колонн (длинная в Z → маленький width в Top-стопке).

**Решение.** Такие секции не ставить в свою стопку — размещать отдельно
anchor-driven:

1. В `SectionGroupSet.Build` после резолва side — определить `stackOrientation`
   по side (Left/Right → vertical, Top/Bottom → horizontal), вычислить медиану
   размера вдоль стека, отфильтровать аутлайеры. Аутлайеры переносить в
   отдельный список `SmallAnchorDriven` (не в `Unknown`).

2. `DrawingLayoutViewItem.ParentAnchorX/Y` уже заполнены через
   `SetParentViewRelations()` — это точка на листе где стоит SectionMark в
   ownerView.

3. В `BaseProjectedDrawingArrangeStrategy` после основного arrange —
   отдельный проход для `SmallAnchorDriven`:
   - найти ближайший свободный прямоугольник к anchor (nearest-free-rect);
   - если рядом с anchor места нет — обычный fallback (свободный угол листа).

4. `_resultById[id]` для таких секций хранит resolved side (для
   projection alignment), но в стопку они не попадают.

**Данные уже готовы:**
- `ParentAnchorX/Y` → `DrawingLayoutViewItem` (заполняется `SetParentViewRelations`)
- `ParentViewId`, `ParentRelationKind` → там же

**Зависимости:** 6.10 (реализована), `SetParentViewRelations` (реализована).

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
