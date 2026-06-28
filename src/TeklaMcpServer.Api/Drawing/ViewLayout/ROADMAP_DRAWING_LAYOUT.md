# Roadmap Drawing Layout

Реализованные фазы/подшаги вынесены в `HISTORY_DRAWING_LAYOUT.md` (фазы 1,2,4;
подшаги 6.1,6.2,6.5-6.9,6.11; шаги 4.1-4.5; инфраструктура фазы 5). Здесь —
активное и незакрытые остатки. Внимание: Фаза 5 НЕ завершена целиком —
инфраструктура готова, но live validation и safety-gate для selected-candidate
`Apply` не закрыты (см. «Фаза 5 — остаток» ниже).

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

### Фаза 5 — остаток (active risk)

Инфраструктура аналитического pipeline реализована и вынесена в HISTORY
(5.1–5.5), НО фаза не закрыта. Незакрытое держим здесь, чтобы не прятать риск:

- **5.5 live validation не снят.** Нужно: снять фиксированный live validation
  set в Tekla, сохранить first-run и second-run cases для одних и тех же
  drawings, проверить `LayoutDiagnostics` / `LayoutStability` в `meta.json`.
- **selected-candidate `Apply` под safety-gate.** НЕ включать реальное
  применение выбранного candidate, пока live cases не пройдут критерии приёмки:
  нет missing baseline views; нет неожиданных scale changes; repeated runs
  converge (второй запуск даёт zero/near-zero apply delta); reserved/table/
  title-block overlaps не растут; projection/detail diagnostics не регрессируют.
- Полный контекст и список «Сделано» — в `HISTORY_DRAWING_LAYOUT.md`, раздел 5.5.

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

Шаг 4.6 — добавить 3D-as-secondary variant. (не начат, детализация отложена)
Идея: размещать Model3D не в фиксированный угол, а как secondary/free-placement
item наравне с другими видами — scorer сам выбирает лучшую позицию.
Детализация отложена до завершения Step 4.7 (dependency zones).

Шаг 4.7 — переключить detail view fallback на anchor pipeline. ⏸️ ОТЛОЖЕН (объединение сделано, цель близости к anchor НЕ достигнута)

**Цель.** Обычные `Detail` views должны размещаться тем же anchor-пайплайном
(`BuildFreeViewRepositionPlan` → `TryInsertClosestToAnchor`), что и
`AnchorDetailSection`. Старый отдельный detail-пайплайн репозиции
(`TryRepositionDetailViews`) — это и есть «второй алгоритм», который убирается.
Итог: один алгоритм вместо двух.

**Суть.** Anchor pipeline: MaxRects + перебор позиций вдоль границ занятых
прямоугольников, отсортированных по близости к anchor. Старый detail-путь
(`TryFindBestEffortPosition`, сетка 12×12, метрика min-overlap) был устаревшим:
шаг ~1/12 usable area пропускал зазоры между блокерами.

**Сделано:**
- Удалён `TryFindBestEffortPosition` (сетка 12×12) и `IntersectionArea`.
- Удалён метод `TryRepositionDetailViews` (старый detail-пайплайн репозиции,
  ~235 строк) и его вызов из `Layout.Variant.cs`.
- `DrawingLayoutWorkspace.SetParentViewRelations()` расширен:
  `SetDetailMarkRelations()` заполняет `ParentAnchorX/Y` + `ParentViewId` для
  `Detail` через `DetailRelationResolver.Build` (`RelationKindDetailMark`).
- `IsAnchorDrivenFreeSection` теперь возвращает true и для `Detail`, у которого
  заполнен parent-anchor → деталь входит в `BuildFreeViewRepositionPlan` как
  anchor-driven, anchor = detail-callout, scope = весь лист.
- `Other` / `Model3D` при неудаче packer'а отклоняются (`no-space`) вместо
  overlap-fallback — часть anchor-пайплайна, сохраняется.
- Проверено live (M.48, 2 детали): обе идут через
  `FREE_VIEW_REPOSITION anchor-adjust`, старый `DETAIL_PROBE` путь исчез.

**ВАЖНО — что НЕ удалено:** `ProbeDetailPlacement` и файл
`BaseProjectedDrawingArrangeStrategy.Details.cs` оставлены. `ProbeDetailPlacement`
общий: используется первичной arrange-стратегией (`TryPlaceDetailViews`) и тремя
unit-тестами, не только удалённым методом репозиции. Исходная формулировка
«удалить ProbeDetailPlacement» была неточностью.

**Открытый вопрос: детали не встают под callout (ОТЛОЖЕНО).**

Объединение пайплайнов выполнено и проверено на M.48 и M.49 — обе детали идут
через `FREE_VIEW_REPOSITION anchor-adjust`. Но визуально детали не садятся под
свой detail-callout. Разобрано по коду и логам — это НЕ лечится мелким gap-фиксом:

Корень — механика `MaxRectsBinPacker.TryInsertClosestToAnchor`
(`Algorithms/Packing/MaxRectsBinPacker.cs`, `TryScoreAnchorCandidate`):
- для каждой свободной ниши желаемая позиция (центр=anchor) **зажимается**
  `Clamp(...)` в границы ниши, затем берётся ниша с минимальным расстоянием до
  anchor;
- если anchor лежит ВНУТРИ ниши — деталь встаёт на anchor (идеально);
- если anchor вне всех ниш (под callout стоит owner-вид → места нет) — `Clamp`
  прижимает деталь к ближайшему КРАЮ ближайшей дырки, а дырка может быть сбоку/
  внизу листа. Это «притяжение к границе доступного», а не к точке anchor.
- крупная деталь отсекается фильтром размера (`free.Width < width`) во всех
  близких нишах → остаются только дальние → уезжает. Мелкие секции уезжают
  реже, т.к. влезают в большее число ниш у anchor.

Вывод: packer НЕ создаёт место у anchor, лишь выбирает ближайший край имеющейся
дырки. Чтобы деталь встала под callout, нужно дырку СОЗДАТЬ заранее —
зарезервировать зону под деталь рядом с anchor до раскладки секций (по образцу
3D-corner reservation variant). gap-фикс границы owner и `anchorDistancePenalty`
в scorer — лишь частичные паллиативы, корень в reserve-у-anchor.

Прежняя гипотеза «единый пайплайн попутно починит близость к anchor» оказалась
неверной: пайплайн объединён, но Clamp-притяжение место не освобождает.

Статус: отложено по решению пользователя. Текущая раскладка валидна (победитель
feasible, без наложений/выходов за лист) — детали просто не под callout.

**3D-corner reservation candidate.**

Цель: 3D view должен оставлять как можно больше полезного места для основных
видов, а не получать остаток после их размещения.

Идея: добавить еще один полный layout candidate, где `Model3D` view
размещается заранее как временная reserved area. Это не заменяет текущий
free-view reposition, а конкурирует с ним в общем `SelectBest`. `Other` views
на первом этапе остаются в текущем free-view path.

Правила candidate:
- взять существующие reserved areas чертежа;
- построить 4 варианта временной 3D-зоны: left-top, right-top, left-bottom,
  right-bottom;
- временная 3D-зона допустима только если она внутри листа и не пересекается с
  уже существующими reserved areas;
- для выбора угла 3D-зона может примыкать к краю листа без layout margin, но
  временная reserved area должна полностью блокировать frame 3D view; вопрос
  дополнительного gap до других видов оставить политикой scorer/validator;
- origin 3D view в угол не ставится напрямую: нужно вычислить origin так, чтобы
  именно frame (с учётом frameOffset) влез в угол, а не origin;
- для каждого допустимого угла добавить 3D-зону в список reserved areas и
  посчитать обычную компоновку основных видов/сечений;
- после layout добавить сам 3D view в эту зону;
- передать получившийся полный candidate в общий scorer.

Критерий выбора: scorer должен выбрать не просто угол для 3D, а полный layout,
где основные виды получили максимум нормального места: без выхода за лист, без
reserved overlaps, с минимальными view overlaps и приемлемым fill/compactness.

Первый scope:
- поддержать один `Model3D` view;
- описывать его временную зону прямоугольником по selected frame size;
- при нескольких 3D views оставить текущий free-view path или сложить их в
  будущую отдельную стратегию;
- `Other` views оставить в текущем free-view path до отдельного решения.

**Шаг 5 — SelectBest + один финальный apply.**
`SelectBest(candidates)` → `DrawingLayoutCandidateTeklaApplyAdapter` для
победителя → один финальный placement `CommitChanges()`.

Осталось:
- **Шаг 4:** генератор нескольких полных вариантов поверх зафиксированных sizes;
- **Шаг 5:** SelectBest среди полных вариантов + один финальный apply;
- проверить на реальных чертежах, что applied origins/scales совпадают с
  selected candidate;
- добавить regression-тест: `DebugPreview` не меняет drawing, `FinalOnly` делает
  один commit выбранного plan.

Исходная проблема была в том, что `applyMode=DryRun` защищал только поздний
candidate apply, но не весь pipeline. Текущее состояние:
- `_arrangementSelector.Arrange(...)` вызывается с `ApplyChanges=false`;
- `BaseProjectedDrawingArrangeStrategy.ApplyPlan(...)` не вызывает
  `view.Modify()`;
- frame-offset correction обновляет `ArrangedView`;
- projection, centering, detail/free/anchor reposition обновляют `arranged`;
- `planned-final` строится из итогового виртуального `arranged`;
- origin changes выполняются только selected-candidate apply adapter.

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
- scale probing остается отдельной реальной фазой, потому что фактический frame
  size зависит от Tekla annotations и не вычисляется линейно.

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

Статус: частично реализовано. `SameAsMain`, `PreserveIfNotSmaller`, `PreserveLargerIfFits` работают и проверены на реальных чертежах. `AllowLargerIfFits` не реализован.

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

TODO выполнено:
1. ~~**`ResolveSelectedScales`** принимает `secondaryScalePolicy`, но пока не использует его~~ — передаёт в `ResolveTargetScale`, синхронизировано.
2. ~~**`ResolveTargetScale`** вызывается при реальном `Modify()` без policy~~ — policy передаётся на всех путях estimate и apply.

TODO остаётся:
3. **Группировка по стороне**: решение о сохранении originalScale принимается per-view, а не для группы одной стороны (Left-стопка, Right-стопка). На практике секции одной стороны обычно имеют одинаковый originalScale, поэтому проблема редкая, но теоретически стек может получить смешанные масштабы.

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

### Фаза 7. Единый placement-сервис (архитектурный долг)

Статус: предложено, не начато.

**Проблема (по факту кода).** `new MaxRectsBinPacker` создаётся в **11 call
sites** (проверено `rg`). Каждое место вручную делает boilerplate вокруг packer:
координатный flip sheet↔packer, свой clamp, свой gap, своя конвертация
`placement → frameCenter → ReservedRect`.

**Полная инвентаризация 11 call sites (строки актуальны на момент записи):**

| # | Call site | bin | режим вставки | gap-модель (verified) |
|---|---|---|---|---|
| 1 | `BaseProjectedDrawingArrangeStrategy.cs:292` | `availableW+gap, availableH+gap` | `TryInsert(BestAreaFit)` | **gap в bin + gap к item** |
| 2 | `BaseProjectedDrawingArrangeStrategy.cs:585` | `availableW+gap, availableH+gap` | `TryInsert(BestAreaFit)` | **gap в bin + gap к item** |
| 3 | `BaseProjectedDrawingArrangeStrategy.BaseRect.cs:583` | `searchWindow` size | `TryInsertClosestToPoint` | raw (gap нет) |
| 4 | `BaseProjectedDrawingArrangeStrategy.Relative.cs:74` | `availableW+gap` | `TryInsert(BestAreaFit)` | gap в bin + gap к item |
| 5 | `ProjectedGroupLayoutPlanner.cs:684` | `available` (без gap) | `TryInsertClosestToPoint` | **raw (gap нет вообще)** |
| 6 | `ProjectedGroupLayoutPlanner.cs:832` | `available+gap` | `TryInsertClosestToPoint` | gap в bin + gap к item |
| 7 | `ProjectedGroupLayoutPlanner.cs:974` | `band+gap` | `TryInsertClosestToPoint` | gap в bin + gap к item |
| 8 | `TeklaDrawingViewApi.Layout.Details.cs:185` | `usableMax-usableMin` | anchor **и** point | raw (gap нет) |
| 9 | `GaDrawingMaxRectsArrangeStrategy.cs:131` | `availableW+gap` | (свой) | gap в bin (item — проверить) |
| 10 | `DrawingPackingEstimator.cs:112` | `available+gap` | feasibility | gap в bin |
| 11 | `DrawingPackingEstimator.cs:130` | `available+gap` | feasibility | gap в bin |

**Gap-модель разъезжается сильнее, чем казалось** — три разных степени раздувания:
- **gap в bin + gap к item** (двойной запас): #1,2,4,6,7;
- **raw, gap нет вообще**: #3,5,8;
- **gap только в bin**: #9,10,11.

Это и есть скрытый риск миграции: при переводе на единый контракт
«blockers+gap, item raw» нужно сохранить НЕ только факт gap, но и прежнюю
степень раздувания. Места с «gap в bin + gap к item» давали фактический зазор
≈ `2*gap` между видами — наивный перевод на `blockers+gap` ужмёт их до `gap` и
изменит раскладку. Для таких мест при миграции либо передавать `2*gap`, либо
сознательно зафиксировать смену зазора как намеренное изменение поведения (тогда
сверка origins на M.48/M.49 НЕ совпадёт — и это надо отметить отдельно, а не
считать регрессией).

**Почему это долг.** Один неверный знак во flip = вид «уезжает». Любое новое
правило размещения (например anchor-zone reservation из отложенного 4.7) надо
добавлять в каждое из 11 мест, а не в одно.

**Что есть сейчас.** `ViewPlacementGeometryService` (104 строки) — geometry-helper
(rect↔origin↔frameCenter), он НЕ оборачивает packer и flip не делает. Новый
`ViewPlacementService` строится ПОВЕРХ него, а не вместо.

**Координатный контракт (критично — зафиксировать ДО кода).**
`PlacementFrame` описывается углами в sheet-координатах, а правило оси Y задаётся
явно: **packer-space `Y=0` соответствует ВЕРХУ frame (`MaxY`) и растёт ВНИЗ.**
Это согласуется с фактическим flip в Details.cs (`usableMaxY - placement.Y - h`)
и в Planner (`SheetHeight - margin - placement.Y`). Frame хранит `MinX/MinY/
MaxX/MaxY`, не `Origin+Size`, чтобы не закрепить неоднозначный угол:

```
readonly struct PlacementFrame
{
    double MinX, MinY, MaxX, MaxY;        // sheet-координаты углов bin
    // toPacker(sheetX, sheetY) = (sheetX - MinX,  MaxY - sheetY)   // Y вниз от MaxY
    // toSheet(packerX, packerY) = (MinX + packerX, MaxY - packerY)
}
```

**Единый gap-контракт (один на сервис).** Сервис принимает gap ОДНИМ способом:
**blocked-прямоугольники расширяются на gap, размер вида подаётся raw.** Все 11
мест приводятся к этой модели при миграции; место, которое сейчас кладёт `+gap`
в bin или в item size, переписывается на расширение blockers. Это делает
миграцию реально behavior-preserving, а не случайно.

```
sealed class ViewPlacementService
{
    bool TryPlaceNearPoint(PlacementFrame frame, double w, double h,
        double sheetTargetX, double sheetTargetY,
        IReadOnlyList<ReservedRect> blocked, double gap, out ReservedRect sheetRect);

    bool TryPlaceNearAnchor(PlacementFrame frame, double w, double h,
        double sheetAnchorX, double sheetAnchorY,
        IReadOnlyList<ReservedRect> blocked, double gap, out ReservedRect sheetRect);

    bool CanFit(PlacementFrame frame, IReadOnlyList<(double W,double H)> items,
        IReadOnlyList<ReservedRect> blocked, double gap);
}
```

Выход — в sheet-координатах через `ViewPlacementGeometryService`.

**План (behavior-preserving, по одному месту за раз):**
- 7.1 — ✅ ВЫПОЛНЕН. Введены `PlacementFrame` (flip-контракт) и
  `ViewPlacementService` (`TryPlaceNearPoint` / `TryPlaceNearAnchor` / `CanFit`)
  поверх `MaxRectsBinPacker`; gap входит только через расширение blockers, item
  raw. `ViewPlacementServiceTests` (9 тестов, зелёные): round-trip
  `toSheet(toPacker(p))==p`, направление Y (точка у `MaxY` → packer Y=0),
  байт-в-байт эквивалентность старой формуле Details.cs, gap-expanded blocker,
  anchor-at-point, `CanFit`, degenerate frame. На момент 7.1 сервис ещё никем не
  вызывался — поведение чертежей не менялось; последующие шаги подключают
  отдельные call sites.
- 7.2 — ✅ ВЫПОЛНЕН. `Layout.Details.cs` free-view/anchor pass переведён на
  `_viewPlacementService.TryPlaceNearAnchor` / `TryPlaceNearPoint`; удалён ручной
  packer+flip и `BuildFreeViewBlockedRectangles` (его gap-модель «blockers+gap,
  item raw» совпадала с контрактом сервиса). Проверено live на M.49: детали
  по-прежнему идут через `FREE_VIEW_REPOSITION anchor-adjust`, anchor target
  тот же (`530.0,70.5`), раскладка `feasible=1`.
  **Замечание по сверке:** чистую origins-parity на M.49 снять нельзя — раскладка
  недетерминирована между прогонами (score-tie 3D-corner вариантов,
  предсуществующее свойство, не следствие миграции). Эквивалентность placement
  гарантирована unit-тестом `TryPlaceNearPoint_MatchesOldFlipFormula` (байт-в-байт
  со старой формулой). Недетерминизм candidate-selection — отдельный вопрос (6.6).
- 7.3a — код мигрирован, НО live НЕ доказан. #3 `BaseRect.cs:583` переведён на
  `ViewPlacementService.TryPlaceNearPoint`. ВАЖНО: на M.48/M.49 финал строит
  custom-план `ProjectedGroupLayoutPlanner` (`front_arrange_plan mode=custom`,
  старые packer #5/#6/#7), а strict base-rect #3 как кандидат НЕ выигрывает.
  Совпадение позиции FrontView НЕ доказывает, что её поставил #3 — это была
  ошибочная проверка. На M.48/M.49 наблюдался реально применённым только #8
  (детали, near-anchor); #3 мигрирован в коде, но его runtime-path/live-эффект
  НЕ доказан, т.к. strict-кандидат проигрывает Planner. На другом drawing, где
  strict-путь выиграет, #3 может применяться — это надо проверять отдельно.
  Статус #3: **code migrated, acceptance pending** (не «выполнен»).

- **7.3b — оставшиеся 9 мест (режимы verified по коду).**
  - **closest-to-point (сервис УЖЕ умеет — `TryPlaceNearPoint`):**
    Planner #5 `684`, #6 `832`, #7 `974`. Это путь, который реально строит
    раскладку основных видов на M.48/M.49 (`front_arrange_plan mode=custom`).
    gap: #5 raw, #6/#7 «gap в bin + gap к item» (~2*gap — сохранить зазор).
  - **best-area-fit (сервис умеет — `TryInsertBestArea`):**
    #1 `BaseProjected.cs:292`, #2 `:585`, #4 `Relative.cs:74`.
    gap: #1/#2/#4 «gap в bin + gap к item».
  - **Ga #9 `:131`** — своя обёртка вставки + item-inflation с origin от угла
    ячейки (см. блокер ниже).
  - **feasibility (сервис умеет — `CanFit`):**
    #10/#11 `DrawingPackingEstimator` — не placement-output site, а чистый
    пробник вместимости; мигрировать через `CanFit`, не через `TryInsertBestArea`.
  Перед миграцией для мест с двойным gap решить: передавать `2*gap` ИЛИ
  зафиксировать смену зазора как намеренную (origins НЕ совпадут — отметить).
  **Пересмотр приоритета:** Planner #5–7 важнее всех — он определяет видимую
  раскладку. Мигрировать его ПЕРВЫМ (не Ga), и сверять не позицию-совпадение, а
  факт что `view_placement` от сервиса = applied-результат; совпадение origins
  само по себе не доказывает, что мигрированный path реально выиграл.

  **БЛОКЕР на саму миграцию area-fit мест (зафиксирован).** #9 Ga переплетает
  item-inflation (`w+gap`) с origin от ВЕРХНЕГО-ЛЕВОГО угла ячейки
  (`margin + rect.X + w/2`, не центр ячейки). Чистый перенос требует пересчёта
  origin из rect ячейки, а проверить его можно только на живом GA-чертеже —
  сейчас открыты только AssemblyDrawing (M.48/M.49), которые идут через
  `BaseProjectedDrawingArrangeStrategy`, НЕ через Ga. Миграция area-fit мест
  вслепую нарушила бы критерий приёмки «сверка origins», поэтому отложена до
  сессии с GA/area-fit чертежом. `TryInsertBestArea` готов и ждёт.
- 7.4 — #10,#11 `DrawingPackingEstimator` перевести на `CanFit` (тонкий пробник,
  без своего packer).
- 7.5 — добавить anchor-zone reservation ОДИН раз в сервисе
  (`ReserveAnchorZone` перед раскладкой секций) — закрывает остаток 4.7.

**Порядок и риск.** #8 (Details) уже принят. Дальше первым мигрировать `Planner`
(#5–7), потому что он реально определяет видимую раскладку основных видов на
M.48/M.49. #1/#2/#4 мигрировать после решения по effective gap. #9 Ga — только
когда есть живой GA drawing для проверки. Каждый шаг = отдельный коммит со
сверкой code/live acceptance.

**Не входит в Фазу 7.** Распухший `BaseProjectedDrawingArrangeStrategy`
(2906 строк, 86 методов) как декомпозиция — отдельный долг. НО его 2 packer call
sites (#1,#2) В scope Фазы 7: критерий «`new MaxRectsBinPacker` только внутри
сервиса» относится ко всем 11, включая эти два.

**Критерии приёмки — два РАЗДЕЛЬНЫХ статуса на каждое место:**

`code migrated` (код переведён на сервис):
- ручной `new MaxRectsBinPacker` и ручной flip удалены из call site;
- единый gap-контракт (или явно зафиксированное отклонение);
- unit-тест эквивалентности flip.

`live accepted` (доказано в проде):
- `view_placement` от сервиса реально исполнился И его результат применён
  (этот candidate выиграл — сверка по `fit_layout_decision` / `mode`, не по
  совпадению позиции). Совпадение origins само по себе НЕ доказательство;
- публичный JSON contract `fit_views_to_sheet` / `get_drawing_views` не меняется.

Для feasibility-only #10/#11 `live accepted` означает другое: old/new
feasibility result совпадает на regression/live inputs или live trace показывает,
что scale/layout decision не изменился. У них нет применённого `view_placement`
rect.

Текущий статус мест:
- #8 детали — `code migrated` + `live accepted` (M.48/M.49).
- #3 base-rect — `code migrated`, `acceptance pending` (strict проигрывает Planner).
- #1,2,4,5,6,7,9,10,11 — не мигрированы.

Финальный критерий Фазы 7: `new MaxRectsBinPacker` только внутри
`ViewPlacementService` для всех 11 мест, И каждое место `live accepted`.

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
