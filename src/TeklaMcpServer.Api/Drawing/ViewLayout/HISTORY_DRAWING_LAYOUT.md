# Historical Drawing Layout Roadmap

Status: implemented / superseded as active roadmap.

Это история уже реализованной компоновки видов. Активный план: `ROADMAP_DRAWING_LAYOUT.md`. Сюда вынесены завершённые фазы и подшаги, чтобы активный roadmap оставался тонким.

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

Статус: selector/scorer реализованы. В production selection участвуют два
полных виртуальных candidates: `planned-before-free` и `planned-final`.
Дальнейшее расширение набора полных вариантов остается future work.

Цель: генерировать и сравнивать несколько виртуальных layout-вариантов из
одного workspace.

Сделано:
- `DrawingLayoutCandidateSelector` выбирает лучший candidate по feasibility,
  score и stable input order;
- `DrawingLayoutScorer` учитывает fill ratio, uniform scale, view/reserved
  overlaps и `edgePenalty`;
- `fit_views_to_sheet` пишет selection trace: index, rank, selected flag,
  rejection/selection reason;
- частичные snapshots (`planned-arranged`, `planned-centered`,
  `post-projection`) больше не участвуют в production selection: они не содержат
  все поздние фазы и поэтому не могут безопасно применяться;
- `planned-before-free` строится после projection, centering и detail
  reposition, сохраняя исходное размещение free views;
- `planned-final` дополнительно включает free/anchor reposition;
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
- Следующее расширение должно сравнивать только варианты, каждый из которых
  прошел полный виртуальный pipeline.

#### 5.4 Применение выбранного candidate

Статус: единый apply полного `planned-final` включен в `FinalOnly` и защищен
feasibility/safety checks.

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
- planned/runtime происхождение candidate хранится явно в
  `DrawingLayoutCandidate.Source`; применимость больше не определяется по имени;
- `DebugPreview` оставляет apply в `DryRun`;
- `FinalOnly` применяет feasible `planned-final`, если safety policy разрешает
  его deltas;
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

**Известный failure case (зафиксирован):**

Чертёж: A2 (841×594), 9 видов, три секции `side=Left`.
На масштабе `1:10` алгоритм пробует разместить три секции (~88×85мм каждая)
горизонтально слева от FrontView. По логам масштаб отклонён:
`SCALE_CANDIDATE_REJECT scale=1:10 reason=no-fit(4778:SectionView)`.
Вероятная причина: с учётом gap, margin, frame offsets и занятых зон (3D вид
внизу слева) доступная область для горизонтальной линии секций оказывается
недостаточной.
Результат: алгоритм выбирает `1:15`, fill=0.42 (лист используется на 42%).

Варианты которые решили бы проблему на `1:10`:
- три секции в вертикальный столбик слева (суммарная высота ~255мм, влезает)
- секции перенести снизу или сверху горизонтальной линией
- распределить по двум сторонам (часть слева, часть справа)

Корень: `SectionGroupSet` жёстко назначает `side=Left` всем трём секциям и
передаёт один вариант в placement. `ProjectedGroupLayoutPlanner` варианты для
секций не генерирует. `no-fit` происходит до запуска planner.

Нужно: секции должны участвовать в генерации вариантов наравне с основной
проекционной группой — несколько вариантов раскладки, выбор лучшего по score.

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

**Публичный frame rect в `get_drawing_views` (контракт-расширение):**
- `ArrangedView.FrameRect` (`ReservedRect?`, sheet coordinates) вычисляется в
  `TeklaDrawingViewApi.Layout.cs` после arrange: из `SelectedFrameSizesById` +
  `FrameOffsetsById` (с учётом `scale`), иначе из `ActualViewRectsById`,
  иначе `null`.
- `DrawingCommandHandler.Views.cs` отдаёт его в `get_drawing_views` как
  nullable `frameMinX/frameMinY/frameMaxX/frameMaxY`.
- Это **аддитивное** расширение публичного JSON `get_drawing_views`: новые поля
  nullable, существующие поля не меняются. Контракт `fit_views_to_sheet` не
  затронут (запрет из раздела «Не цели» относится к нему).
- Назначение: дать клиенту/диагностике реальный frame rect для проверки
  margin-критериев 6.5 без отдельного geometry-запроса.

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

Статус: placement pipeline виртуализирован. Реальным остается scale probe,
потому что фактический размер Tekla view после смены масштаба нельзя надежно
получить аналитически.

Цель 6.9 остаётся: разделить `allowTeklaMutation` на два отдельных флага:
- `allowVirtualPlan` — всегда `true`; весь pipeline работает виртуально;
- `allowApply` — `true` только для `FinalOnly`; единственное место `Modify()`.

Сделано:
- `DebugPreview`/`DryRun` передает в arrange context `ApplyChanges=false`;
- стратегии раскладки считают `ArrangedView`, но не вызывают `view.Modify()` при
  `ApplyChanges=false`;
- scale probe в `DryRun` стал виртуальным: frame size оценивается от исходного
  scale, без временного изменения `View.Attributes.Scale` и без
  `CommitChanges()`;
- выбранные scale сохраняются в `DrawingLayoutWorkspace.SelectedScalesById`,
  поэтому planned candidates несут целевой масштаб, даже если Tekla view еще не
  изменен;
- projection alignment, group centering и detail reposition пропускают реальные
  `Modify()`/`CommitChanges()` в `DebugPreview`;
- passive candidates, scorer, selector, apply plan, safety gate и Tekla apply
  adapter реализованы;
- scale changes разрешены apply safety policy только для scale-changing режима,
  а режимы сохранения текущего масштаба продолжают блокировать изменение scale.

#### Модель: scale probe реальный, placement виртуальный

Размер вида нелинейно зависит от масштаба — текст, размерные линии и марки
имеют фиксированный бумажный размер, поэтому аналитически пересчитать
`frame size` при смене scale невозможно. Реальный размер известен только после
`view.Modify()`.

Следствие: перебор масштабов дорогой, но честный — каждый кандидат требует
реального `Modify()`. Перебор вариантов размещения после выбранного масштаба
дешёвый — размеры зафиксированы, двигается только `origin`.

**Граница:**
- **До границы (реально):** scale probe → `view.SetScale()` → `Modify()` →
  читаем реальные `view.Width/Height` / `frame sizes`
- **После границы (виртуально):** arrange strategy, centering, free-reposition,
  projection alignment — всё над `ArrangedView` / `ReservedRect`, без `Modify()`

#### Поэтапный план реализации

**Шаг 1 — free/anchor reposition виртуальный.**
`TryRepositionFreeViews` (3D-виды, details, AnchorDetailSection) работает
напрямую через `view.Origin`. Изменить: работать по `arranged` dict,
возвращать обновлённый `ArrangedView`.

Это первый блокер для selected-candidate `Apply`: `AnchorDetailSection`
получает корректную позицию именно здесь, а более ранний candidate про неё ещё
не знает.

Virtual free-pass не должен читать live Tekla geometry как fallback. Он может
использовать только `arrangedById`, selected frame sizes, сохранённые frame
offsets / actual rect snapshot из workspace. Если нужного размера или frame
data нет, helper должен вернуть decision `skip reason=no-size-or-frame`, а не
вызывать `TryGetBoundingRect(view)`. Старый live `FinalOnly` path может
временно сохранить Tekla fallback до полной замены.

**Статус Шага 1: выполнено.**

- `BuildFreeViewRepositionPlan` является единственным placement-pass для free
  views: не вызывает `Modify()`, не читает live Tekla geometry;
- Виртуальный план не использует `view.Width/Height` как fallback: только
  `SelectedFrameSizesById`. При отсутствии размера — `skip reason=no-size-or-frame`.
  Fallback на `ActualViewRectsById` snapshot разрешён (snapshot снят до layout pass,
  не live Tekla call).
- `ApplyFreeViewRepositionPlan` переносит решения в `arranged`;
- planner обновляет локальный `arrangedById` после каждого решения, поэтому
  дочерние anchor views видят уже перемещенного родителя;
- `Model3D`, `Other` и `AnchorDetailSection` используют тот же виртуальный pass;
- старый повторный planning+apply loop удален.

**Шаг 2 — виртуализировать все фазы после Arrange().**
Фактический порядок текущего pipeline:
`Arrange -> frame-offset correction -> projection alignment -> centering
-> detail reposition -> free/anchor reposition`.

**Статус Шага 2: выполнено.**

Сделано:
- `BaseProjectedDrawingArrangeStrategy.ApplyPlan(...)` больше не вызывает
  `view.Modify()`;
- frame-offset correction обновляет только `ArrangedView`;
- centering обновляет только `arranged` и пишет
  `center_group_plan applied=0`.

- ранний `ApplyArrangedOrigins` удален;
- projection alignment обновляет `arranged` через
  `ProjectionAlignmentMoveHelper`;
- detail/free/anchor reposition обновляют только `arranged`;
- placement-фазы после scale probe не вызывают `Modify()` / `CommitChanges()`.

**Шаг 3 — единый финальный apply выбранного candidate.**
**Статус Шага 3: реализовано, требуется live regression validation.**

После полной виртуализации поздних фаз:
- selected-candidate apply становится активным в `FinalOnly`;
- `passiveCandidate` / финальный baseline строится из виртуального
  `arranged` после всех фаз — через `FromPlannedViews`, а не через
  `FromRuntimeLayout` и повторное чтение текущих позиций Tekla;
- `FromRuntimeLayout` используется только для исходного probe/planning
  snapshot и не должен подменять виртуальные финальные позиции;
- production selection получает только полные `planned-before-free` и
  `planned-final`;
- частичные snapshots нельзя возвращать в selection; будущие конкурирующие
  candidates обязаны пройти весь виртуальный pipeline;
- apply plan обязан содержать frame-offset correction и все поздние поправки
  выбранного варианта;
- `DrawingLayoutCandidateTeklaApplyAdapter.Execute(applyPlan)` является
  единственной точкой изменения `Origin` после planning pipeline;
- после него выполняется один placement `CommitChanges()`;
- ранний `ApplyArrangedOrigins` удален: применять до SelectBest нельзя;
- если candidate infeasible или неполный, safety gate оставляет apply в
  `DryRun`.

**Статус centering:** live `Modify()` / `CommitChanges()` из
`TryCenterViewGroup` убран. Метод теперь только пересчитывает `arranged` и
пишет `center_group_plan applied=0`.

**Шаг 4 — генератор вариантов.**
После шагов 1-3 каждый этап возвращает план без side effects.
Варианты для перебора:
- `arranged` — base после arrange strategy
- `centered` — base + centering
- `projection` — base + projection alignment
- `free-reposition` — base + free-reposition
- `combinations` — base + centering + projection + free-reposition
- позже: 3D-corner reservation candidate, другие section policies

Этот шаг должен быть отдельным слоем генерации layout variants, а не набором
параллельных алгоритмов. Дорогая подготовка остается общей: чтение views,
semantic/topology workspace, base reserved areas, scale probe, selected frame
sizes и frame offsets выполняются один раз. Fan-out вариантов начинается только
после того, как размеры зафиксированы. Variant может строить derived reserved
areas, например добавлять временную 3D-зону, но без нового чтения Tekla.

Общая схема:
`shared scale/frame preparation -> variants[] -> arrange/post-process per variant -> build candidates -> score/select -> apply selected`.

Различаться должны только входные условия variant:
- базовый вариант: 3D не участвует в основной группе, затем идет обычный
  free-view reposition;
- 3D-corner reservation: `Model3D` заранее превращается во временную reserved
  area, потом используется тот же arrange/post-process/scorer;
- 3D-as-secondary: `Model3D` добавляется как secondary/free-placement item в
  общий layout pass, но не становится scale driver и не задает projection
  relations;
- будущие section policies меняют только входной grouping/priority, а не
  scoring/apply path.

Требование: новые варианты не должны дублировать apply, scorer, frameOffset
correction, detail/free reposition и validation. Они должны переиспользовать
общие DTO (`ArrangedView`, `DrawingLayoutCandidateView`, `ReservedRect`) и
общий `DrawingLayoutCandidateSelector`.

Ограничение по стоимости: variant generator не должен повторять scale probe и
повторное чтение геометрии Tekla для каждого варианта. Допустимо повторять
только виртуальные placement-фазы с уже известными frame sizes. Если вариант
требует нового scale probe, это отдельная policy и она должна быть явно
заложена в бюджет времени/логирование.

Точка fan-out в коде: `TeklaDrawingViewApi.Layout.cs`, после
`layoutWorkspace.SetGridAxes(preloadedAxes)`, перед `arrangeSw.StartNew()`.
К этому моменту уже зафиксированы: выбранный scale, actual rects, selected
frame sizes, frame offsets, grid axes и `arrangedViews`. Варианты начинают
отличаться только с вызова arrange strategy.

**План рефакторинга перед реализацией вариантов.**

Правило: поведение не меняется одновременно с рефакторингом. Сначала вынести
текущий вариант без новой логики, потом добавлять новые candidates.

Шаг 4.1 — ввести `SharedLayoutContext`.
Объединить всё до fan-out boundary в один DTO:
drawing, workspace, selected scale, actual rects, selected frame sizes,
frame offsets, grid axes, arrangedViews, sheet/margin/gap, current views,
base reserved areas.
`actualRects` и `selectedFrameSizesById` включены явно: они нужны для
candidate baseline и diagnostics.

Шаг 4.2 — ввести `DrawingLayoutVariantResult`.
DTO результата одного варианта:
`IReadOnlyList<DrawingLayoutCandidate> Candidates`, arranged,
arrangedBeforeFree, projectionResult, arrangeMs, postAdjustMs, projectionMs.
`Candidates` — список, а не один candidate: default variant возвращает оба
текущих кандидата (`planned-before-free` и `planned-final`), чтобы не потерять
текущую модель сравнения.
Workspace не мутировать для derived reserved areas: передавать их отдельным
параметром в arrange context.

Шаг 4.3 — вынести текущий путь как `RunDefaultLayoutVariant(sharedContext)`.
Поведение не меняется. Проверить, что лог/result идентичен текущему.

Шаг 4.4 — подключить `SelectBest` к списку variant results.
Пока список из одного default variant. Проверить, что результат тот же.
Candidates для `SelectBest` материализовать (`ToArray()`/`ToList()`), а не
передавать ленивый `SelectMany`.

Шаг 4.5 — добавить 3D-corner reservation variant. ✅ ВЫПОЛНЕН
4 угла (left-bottom, right-bottom, left-top, right-top), derived reserved areas,
тот же pipeline. Apply baseline исправлен: строится от variant result победившего
candidate, а не от последнего выполненного variant. FinalRuntimeViews включает
3D view. Проверено на реальных чертежах (M.45, M.83, M.84, M.85, M.47 и др.).

#### 6.11 Anchor-driven размещение маленьких секций

Статус: реализовано и проверено на реальном чертеже.

**Проблема.** Маленькая секция (например, G-G — горизонтальный разрез балки,
~101×85 мм при 1:10) попадает в Bottom-стопку рядом с BackView/BottomView
(~700×160 мм) — занимает целую зону, выглядит потерянной.

После первых правок стало видно более общее нарушение: маленькая секция
правильно распознаётся как `SmallAnchorDriven`, но затем смешивается с
обычными `secondaryViews` и попадает в общий fallback. В результате её
`ActualPlacementSide` может стать `Right/Top/...` только потому, что fallback
физически поставил её в эту область листа. Это не семантический тип вида.

**Нужная модель.** Ввести явную классификацию видов для layout, например
`LayoutViewKind`:

- `MainProjected` — основные проекционные виды;
- `StandardSection` — обычные сечения, участвуют в секционных стопках;
- `AnchorDetailSection` — маленькое сечение/деталь от parent anchor;
- `Detail` — detail views;
- `Model3D` — 3D/isometric view;
- `Other` — прочие виды.

`AnchorDetailSection` не должен попадать в обычные секционные стопки и не
должен терять статус при передаче в fallback/planner. Его правило размещения:
держаться около `ParentAnchorX/Y`.

**Критерий "маленькая".** Размер секции вдоль направления стопки < порог ×
медиана того же размера по группе. Порог ~0.4 как стартовое значение.

Для section-of-parent предпочтительный критерий — сравнение с parent view:
если есть `ParentViewId`/`ParentAnchorX/Y` и длинная сторона секции сильно
меньше длинной стороны parent view, классифицировать как `AnchorDetailSection`.
Side/group-based критерий оставить как fallback.

Направление стопки зависит от её ориентации (`stackOrientation`), которая
должна быть вычислена до outlier-фильтра:
- стопка вертикальная (Left/Right) → сравниваем `height`;
- стопка горизонтальная (Top/Bottom) → сравниваем `width`.

Это работает корректно для балок (длинная в X → маленький height в Left-стопке)
и для колонн (длинная в Z → маленький width в Top-стопке).

**Реализованное решение.** Такие секции не ставятся в свою стопку и
размещаются отдельно anchor-driven:

1. `LayoutViewKind`/resolver добавлен. Маленькая секция получает
   `AnchorDetailSection`, а обычные сечения остаются `StandardSection`.

2. `SectionGroupSet.Build` фильтрует маленькие anchor-driven секции из обычной
   секционной стопки и сохраняет классификацию в workspace.

3. `DrawingLayoutViewItem.ParentAnchorX/Y` уже заполнены через
   `SetParentViewRelations()` — это точка на листе где стоит SectionMark в
   ownerView.

4. После основного arrange выполняется отдельный free-placement проход:
   - free parent views размещаются раньше дочерних `AnchorDetailSection`;
   - дочерняя секция размещается рядом с ближайшей к anchor границей parent;
   - parent после размещения становится blocker и не может быть перекрыт child;
   - anchor корректируется на фактическое перемещение parent через delta
     `arranged origin - original origin`, без повторного чтения кешированного
     `View.Origin`;
   - frame rect строится из фактического размера и сохранённого frame offset,
     поэтому асимметричная рамка не выходит за границы листа;
   - `gap` учитывается один раз: расширением blockers, без дополнительного
     увеличения размера размещаемого вида;
   - при отсутствии свободного места секция остаётся на месте и пишет явный
     trace, вместо overlap fallback.

5. `_resultById[id]` для таких секций хранит resolved side (для
   projection alignment), но в стопку они не попадают.

Trace:
- `FREE_VIEW_REPOSITION frame`;
- `FREE_VIEW_REPOSITION anchor-adjust`;
- `FREE_VIEW_REPOSITION anchor-placed`;
- `FREE_VIEW_REPOSITION result=skip-anchor-no-space`.

**Данные уже готовы:**
- `ParentAnchorX/Y` → `DrawingLayoutViewItem` (заполняется `SetParentViewRelations`)
- `ParentViewId`, `ParentRelationKind` → там же

**Зависимости:** 6.10 (реализована), `SetParentViewRelations` (реализована).

Оставшиеся ограничения:
- критерий `AnchorDetailSection` пока основан на фиксированном пороге размера;
- наследование parent relation поддерживает один уровень;
- placement использует прямоугольные frame bounds, а не фактический контур
  содержимого вида.
