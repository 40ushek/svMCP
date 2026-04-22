# Роадмап слоя Marks

## Цель

Слой `Marks` должен стать runtime/view-level слоем для:

- чтения фактических марок в виде;
- анализа их геометрии и placement-сигналов;
- подготовки контекста для layout/evaluator logic;
- последующего before/after snapshot pipeline.

Это не слой про desired mark intent.

Коротко:

- `MarkDefinitions` = какие marks хотим получить
- `Marks` = что реально есть в виде и как это анализировать/раскладывать

## Граница с `MarkDefinitions`

`MarkDefinitions`:

- описывает desired mark set;
- задаёт scenario/target/content/style/placement intent;
- не должен знать про runtime bbox/obb/layout geometry.

`Marks`:

- работает с реальными runtime marks;
- читает геометрию;
- строит view-level mark context;
- поддерживает query/layout/overlap logic.

## Главный принцип

Нужно разделить три уровня:

### 1. Public/runtime DTO

Например текущий [DrawingMarkInfo.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/DrawingMarkInfo.cs).

Это внешний read model, пригодный для query/debug.

### 2. `MarksViewContext`

Это внутренний factual layer одного вида.

Он нужен для:

- evaluator/scorer;
- layout reasoning;
- future agent snapshot pipeline;
- explainable mark decisions.

### 3. Layout algorithm items

Например [MarkLayoutItem.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/MarkLayoutItem.cs).

Это уже execution-level shape для `MarkLayoutEngine`, а не канонический context.

## Связь с `DrawingViewContext`

Marks не должны вводить отдельный базовый view-context.

Архитектурное правило:

- общий `DrawingViewContext` остаётся каноническим контекстом вида;
- `Marks` являются consumer этого общего view-level context;
- marks-specific logic строится поверх общего `DrawingViewContext`, а не вместо него.

Это должно совпадать с архитектурой размеров:

- `DrawingViewContext` = общие факты вида;
- mark layout = marks-specific reasoning поверх этих фактов.

Первый практический consumer path для marks:

- сначала `Parts` как каноническая геометрия деталей в виде;
- затем `Bolts` как дополнительные obstacles/signals;
- `PartsBounds` и `PartsHull` не считаются обязательными входами для marks на первом этапе.

Текущий evolution path layout logic:

- `arrange_marks` должен становиться context-aware consumer общего `DrawingViewContext`;
- `resolve_mark_overlaps` остаётся локальным secondary post-process;
- основной рост качества marks layout должен идти через context-aware placement/scoring, а не через усложнение overlap-only path.

## Почему это нужно

Сейчас mark-layer уже силён, но логика размазана между:

- query DTO;
- geometry helpers;
- `TeklaDrawingMarkLayoutAdapter`;
- `MarkLayoutEngine`.

Из-за этого пока нет явного стабильного уровня:

- `view facts`
- `mark facts`
- `placement signals`

на который можно опереться так же, как `DrawingContext` и `DrawingViewContext`.

## Целевая модель

### 1. `MarksViewContext`

Это контекст одного вида для mark reasoning.

Минимальный состав:

- `ViewId`
- `ViewScale`
- `ViewBounds`
- `Marks`
- `Warnings`

### 2. `MarkContext`

Это factual context одной марки.

Минимальный состав:

- `MarkId`
- `ModelId`
- `Anchor`
- `CurrentCenter`
- `Geometry`
- `Axis`
- `HasLeaderLine`
- `CanMove`
- `PropertiesSummary`

### 3. `MarkGeometry`

Отдельный geometry block:

- bbox
- oriented corners / polygon
- width / height
- resolved source
- reliability

`CanMove`:

- это не persisted API flag;
- это runtime/layout signal;
- он показывает, можно ли mark рассматривать как подвижную в текущем layout/evaluator path.

## Что уже есть

Уже есть сильная база:

- [TeklaDrawingMarkApi.Query.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/TeklaDrawingMarkApi.Query.cs)
- [TeklaDrawingMarkLayoutAdapter.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/TeklaDrawingMarkLayoutAdapter.cs)
- [MarkGeometryResolver.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/MarkGeometryResolver.cs)
- [MarkGeometryHelper.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/MarkGeometryHelper.cs) как compatibility facade
- [MarkLayoutEngine.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/MarkLayoutEngine.cs)
- [MarkOverlapResolver.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/MarkOverlapResolver.cs)
- [LeaderAnchorResolver.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/LeaderAnchorResolver.cs)

То есть execution path уже есть.

Что уже фактически завершено:

- `MarksViewContext` и `MarkContext` зафиксированы как внутренний factual layer;
- `MarksViewContextBuilder` стал каноническим builder-ом mark context;
- `get_drawing_marks` использует context-based projection;
- `arrange_marks` использует `MarkContext -> MarkLayoutItem`;
- `resolve_mark_overlaps` использует тот же context-based layout path.
- базовая `leader-anchor` оптимизация уже встроена в `arrange_marks` как отдельный post-step после `ApplyPlacements`;
- `leader-anchor` path уже учитывает:
  - inward shift от ближайшей грани через нормаль к ребру;
  - corner avoidance (2mm clearance от вершин полигона);
  - halve-until-inside fallback для тонких деталей.

## Текущее состояние лидеров

### Что есть в `MarkContext`

- `HasLeaderLine` — флаг
- `Anchor` — `DrawingPointInfo?` — это `LeaderLinePlacing.StartPoint`
- `PlacingType` — строка `"LeaderLinePlacing"`
- `LeaderSnapshot` — factual runtime block с:
  - `AnchorPoint`
  - `LeaderEndPoint`
  - `InsertionPoint`
  - `LeaderLines`
  - `LeaderLength`
  - `Delta`

### Инверсия: public DTO богаче internal context

`DrawingMarkInfo` (public) содержит:

- `LeaderLines: List<MarkLeaderLineInfo>` — с `StartX/Y`, `EndX/Y`, `ElbowPoints`
- `ArrowHead`

Базовая инверсия уже снята:

- internal `MarkContext` теперь тоже хранит отдельный leader runtime snapshot;
- public DTO по-прежнему остаётся read/debug projection, а не канонической внутренней моделью.

### Что есть в алгоритмах

- `BuildLeaderCandidates` — кандидаты вокруг `AnchorX/Y` с quadrant affinity
- `CalculateLeaderCrossingPenalty` — штраф за пересечение лидеров через `SegmentsProperlyIntersect`
- `CalculatePreferredSidePenalty` — держит марку на той же стороне от якоря
- `LeaderLengthWeight` — штраф за длину лидера
- `LeaderAnchorResolver` — после arrange двигает `StartPoint` к ближайшей грани детали

## Runtime placing hierarchy

Для geometry/layout reasoning нужно различать:

- **actual runtime placing** (`mark.Placing`)
- **preferred placing intent** (`mark.Attributes.PreferredPlacing`)

Это не одно и то же.

`PreferredPlacing` задаёт желаемое Tekla-поведение,
но не является каноническим источником фактической geometry/collision semantics.

Для geometry helper и layout logic каноническим источником является именно:

- `mark.Placing`

Минимальная runtime hierarchy, которую нужно учитывать:

- `LeaderLinePlacing`
- `BaseLinePlacing`
- `AlongLinePlacing`
- `PointPlacing`
- прочие / unknown placing types

## Canonical geometry rules by placing type

Следующий geometry refactor должен исходить из разных правил для разных `PlacingType`.

### `LeaderLinePlacing`

- anchor source: `LeaderLinePlacing.StartPoint`
- movement semantics:
  - на основном arrange-pass двигается body/insertion point;
  - на отдельном post-step может двигаться и `LeaderLinePlacing.StartPoint`
- collision geometry source: object-aligned / resolved geometry самой mark
- axis source: не обязателен как основной signal
- fallback: raw Tekla geometry

### `BaseLinePlacing`

- anchor source: baseline/baseline-related placement semantics
- movement semantics: mark привязана к базовой линии
- canonical axis source: связанная деталь в текущем виде
- collision geometry source: width/height mark geometry + axis from related part
- fallback axis source:
  - baseline line itself
  - `mark.Attributes.Angle`
- raw Tekla OBB/BBox не считаются каноническим collision source

### `AlongLinePlacing`

- anchor source: along-line placement semantics
- movement semantics: mark ориентирована вдоль линии
- canonical axis source: связанная деталь в текущем виде
- collision geometry source: width/height mark geometry + axis from related part
- fallback axis source:
  - along-line geometry itself
  - `mark.Attributes.Angle`
- raw Tekla OBB/BBox не считаются каноническим collision source

### `PointPlacing` и прочие fallback cases

- anchor source: point/current mark position
- collision geometry source: raw/resolved mark geometry
- axis source: отсутствует или secondary
- fallback: raw Tekla geometry

## Known API limitation

Для части runtime mark types Tekla raw boxes недостаточны как канонический источник
layout/collision geometry.

Практически подтверждённая проблема:

- для `BaseLinePlacing` / `AlongLinePlacing` оригинальные Tekla bbox/obb могут давать
  геометрически неверный box для collision reasoning
- из-за этого overlap detection на сыром bbox/obb тоже может быть неверным

Следствие:

- `MarkGeometryResolver` должен быть canonical resolved-geometry path
- `MarkGeometryHelper` может временно оставаться только compatibility facade
- raw Tekla bbox/obb должны использоваться только как:
  - debug data
  - display data
  - fallback path

## Этапы

### Phase 1. Naming and context boundary — completed

Сделано:

- ввести `MarksViewContext`;
- ввести `MarkContext`;
- ввести `MarkGeometry` как отдельный geometry block внутри context layer;
- зафиксировать границу между:
  - query DTO
  - context layer
  - layout algorithm layer

### Phase 2. Context builder — completed

Сделан builder, который собирает `MarksViewContext` из текущего runtime path:

- active drawing view
- runtime marks
- geometry helpers
- placement/axis signals

Важно:

- не использовать `MarkLayoutItem` как public/context model напрямую;
- не делать `DrawingMarkInfo` каноническим internal context.

### Phase 3. Read projection — completed

После появления context layer выполнено:

- сделать clean read projection for debug/query;
- обновить `get_drawing_marks`, если context-builder даёт тот же набор данных чище, с меньшим дублированием и без потери runtime detail.

### Phase 4. Leader-anchor basic post-step — completed

После стабилизации context/layout path выполнено:

- вынести базовый выбор точки anchor в отдельный algorithm/helper;
- встроить post-step после `ApplyPlacements` только для leader marks;
- использовать `PartPolygonsByModelId` как runtime source polygon;
- выбирать anchor как inward point от ближайшей грани;
- учитывать:
  - paper-mm semantics через `viewScale`;
  - corner avoidance;
  - minimum clearance from far edge.

Это structural/runtime quality step, а не full leader-shape system.

### Phase 5. Leader geometry snapshot and candidate selection

Phase 5 частично реализована.

Уже выполнено:

- введён отдельный internal runtime block для leader geometry:
  - `AnchorPoint`
  - `LeaderEndPoint`
  - `InsertionPoint`
  - `LeaderLines`
  - `LeaderLength`
  - `Delta`
- этот слой не смешан с `DrawingMarkInfo`;
- собран factual runtime snapshot лидера;
- добавлен маленький candidate-based selector для выбора anchor на ближайшей грани.

Что остаётся в рамках Phase 5:

- перейти от выбора только `anchor point` к совместному выбору пары `part anchor <-> mark body point / leader end`;
- после этого добавить первые style modes:
  - straight
  - angled
  - horizontal elbow
  - vertical elbow.

Рекомендуемая последовательность реализации:

#### Step 5.1. Internal leader runtime snapshot — completed

Выполнено:

- введён отдельный internal block для factual leader geometry;
- он хранится как отдельный `LeaderSnapshot` рядом с `MarkContext`;
- snapshot собирается из runtime `LeaderLine`, `LeaderLinePlacing`, `InsertionPoint`;
- он не смешан с `DrawingMarkInfo` и не вынесен в public DTO;
- используется как factual input для последующих leader-shape algorithms.

Текущий состав snapshot:

- `MarkId`
- `AnchorPoint`
- `LeaderEndPoint`
- `InsertionPoint`
- `LeaderLines`
- `LeaderLength`
- `Delta = InsertionPoint - LeaderEndPoint`

#### Step 5.2. Candidate points on nearest edge — completed

Выполнено:

- для текущей ближайшей грани детали строится не одна точка, а небольшой набор кандидатов;
- минимум:
  - nearest point
  - point shifted left along edge
  - point shifted right along edge
- для каждой candidate point сохраняются:
  - inward anchor
  - corner distance
  - far-edge clearance
  - line length to fixed `LeaderEndPoint`

#### Step 5.3. Candidate-based pair selection — partially completed

Текущая реализованная версия уже использует маленький deterministic score, но только для выбора лучшего `anchor` при фиксированном `LeaderEndPoint`:

- shorter line is better
- less corner-adjacent is better
- larger far-edge clearance is better
- tie-breaker: nearest, then shifted-left, then shifted-right

Что ещё не сделано в полном Step 5.3:

- оценивать уже не только `anchor`, а полную пару:
  - `part anchor point`
  - `mark body point` / `leader end point`
- добавить явный angle/style preference поверх длины линии;
- по-прежнему не вводить full evaluator framework на этом шаге.

#### Step 5.4. First leader shape modes

- после появления pair-selection добавить первые shape modes:
  - `straight`
  - `angled`
  - `horizontal elbow`
  - `vertical elbow`
- shape mode пока держать internal/runtime preference;
- public command parameter выносить только после стабилизации default behavior.

#### Step 5.5. Reference-guided refinement — partially completed

- использовать local reference project `markAligner/` как source of practical ideas;
- особенно полезны:
  - `TeklaMarksEditor.Logic.AlignMarks`
  - `TeklaMarksEditor.Logic.Annotation`
- заимствовать оттуда не код целиком, а decomposition:
  - anchor point
  - leader end point
  - insertion point
  - elbow manipulation
  - leader length / delta semantics.

Уже фактически использовано:

- decomposition `anchor / leader end / insertion / delta`;
- internal `LeaderSnapshot` shape;
- pragmatic split между `anchor placement` и будущим `leader shape`.

Что ещё не сделано:

- explicit elbow/shape behavior;
- прямое runtime reuse идей `AlignMarks` для `angled / horz elbow / vert elbow`.

### Phase 6. Evaluator — partially completed

Уже есть базовый deterministic evaluator/scorer в текущем layout pipeline:

- `SimpleMarkCostEvaluator`
- penalties на:
  - overlaps
  - crowding
  - leader length
  - preferred side
  - leader crossings
  - source/foreign part signals

Что ещё остаётся для полного Phase 6:

- сделать evaluator более явно context-native и explainable;
- выделить/добрать сигналы:
  - overlaps
  - outside/inside quality
  - leader-line quality
  - distance / readability
- привести evaluator к более явному слою reasoning, а не только к engine-local cost function.

### Phase 7. Snapshot pipeline

После evaluator:

- before/after mark snapshots;
- per-view mark cases;
- capture service;
- dataset examples для agent workflow.

## Что не нужно делать сейчас

Не нужно:

- вводить общий интерфейс для drawing/dimensions/marks заранее;
- сразу делать mark agent pipeline;
- смешивать `MarkDefinitions` и `Marks`;
- тащить scorer/snapshot раньше, чем будет стабилен context layer.

## Acceptance criteria

Работа считается успешной, когда:

1. Есть явный `MarksViewContext` как factual view-level model для marks.
2. Есть отдельный `MarkContext` как canonical internal mark unit.
3. `DrawingMarkInfo` остаётся projection/debug DTO, а не единственной внутренней моделью.
4. `MarkLayoutItem` остаётся execution-level model для layout engine.
5. Граница между `MarkDefinitions` и `Marks` становится явной.

## Известные факты API (подтверждено экспериментально)

### LeaderLinePlacing.StartPoint можно перемещать программно

`mark.Placing = new LeaderLinePlacing(new Point(x, y, 0))` — работает, `mark.Modify()` применяет.

Это даёт возможность управлять точкой крепления лидера к детали, а не только телом марки.

**Попытка переместить в центр bbox детали — не сработала:**
- тело марки притянулось к source center (из-за `SourceDistanceWeight`)
- якорь тоже оказался в центре → оба оказались внутри детали → марки поверх конструкции

**Что уже реализовано базово:**
1. Сначала `arrange_marks` размещает тело марки
2. Затем отдельный post-step перемещает якорь лидера в безопасную внутреннюю точку рядом с ближайшей гранью детали

Текущий базовый algorithm:

- nearest edge on part polygon;
- inward normal;
- depth in paper mm through `viewScale`;
- halve-until-inside fallback;
- corner avoidance;
- far-edge clearance.

**Что уже реализовано сверх базовой версии:**

- internal `LeaderSnapshot` / `LeaderLineSnapshot`;
- shared `MarkLeaderLineReader`;
- 3 candidate points на той же ближайшей грани:
  - nearest
  - shifted-left
  - shifted-right
- deterministic best-anchor selection against fixed `LeaderEndPoint`.

**Что ещё не реализовано:**

- совместный выбор пары `anchor point` + `leader end point`;
- explicit leader-shape modes (`angled`, `horz elbow`, `vert elbow`);
- angle/style preference в public command surface.

Для этого ещё нужны:
- full pair-selection поверх уже существующего snapshot/candidate layer;
- explicit leader-shape selection;
- optional reuse of practical ideas from local reference project `markAligner/` (`TeklaMarksEditor.Logic.AlignMarks`, `Annotation`).

## Следующий практический шаг

Следующий практический шаг:

- full pair-selection для `anchor point` + `leader end point / mark body point`
- затем explicit leader-shape modes

В рабочем порядке это значит:

1. `LeaderSnapshot` layer уже есть.
2. `3-point candidate selection` на ближайшей грани уже есть.
3. Следующий шаг — перейти к выбору лучшей пары `anchor <-> leader end/body point`.
4. И только потом добавлять `angled` / `horz elbow` / `vert elbow` modes.

## Phase 8. Multi-mark layout по аналогии с cartographic labeling

После стабилизации leader pair-selection следующий уровень качества должен идти уже не только через leader geometry, а через placement самих mark bodies.

Проблема:

- одна mark зависит от другой;
- хорошего local anchor tweak недостаточно, если тело mark уже стоит слишком далеко или в плохой зоне;
- для нескольких деталей и нескольких marks нужна раскладка множества marks, а не только локальная оптимизация одной линии.

Практический deterministic path здесь должен быть вдохновлён cartographic labeling, но адаптирован под drawing marks:

- candidate positions для body каждой mark;
- deterministic score;
- greedy placement с учётом уже размещённых marks;
- короткий local-improvement pass после первичной раскладки.

Это должно быть эволюцией текущего `MarkLayoutEngine`, а не его заменой:

- существующий candidate/scoring pipeline уже есть;
- `SimpleMarkCandidateGenerator` и `SimpleMarkCostEvaluator` уже решают первую базовую версию;
- следующий шаг — расширить этот pipeline новыми candidate signals, ordering и local improvement.

### Candidate positions for mark body

Для каждой leader mark нужно строить небольшой конечный набор body-candidates, а не искать position в непрерывном пространстве.

Минимальный practical набор:

- текущая позиция;
- позиция ближе к детали по текущему направлению;
- позиция около середины длинной стороны детали;
- соседние позиции с небольшим сдвигом вдоль этой стороны;
- при необходимости 1-2 запасных candidates около альтернативной стороны детали.

Для вытянутых деталей типа балки candidate generation должен учитывать aspect ratio:

- для горизонтально вытянутой детали основные body-candidates — над/под длинной стороной около её середины;
- для вертикально вытянутой детали — слева/справа около середины высоты;
- simple "center of detail" недостаточен без учёта главной оси детали.

### Candidate scoring

Для первой deterministic версии score должен быть explainable и быстрым.

Минимальные penalties/signals:

- overlap с уже размещёнными marks;
- overlap с geometry детали;
- длина leader line;
- crossing лидеров;
- выход за bounds вида;
- слишком близкое или слишком далёкое положение от детали.

### Placement strategy

Первый practical algorithm не должен быть global stochastic optimizer.

Нужен такой порядок:

1. для каждой mark построить небольшой candidate set;
2. отсортировать marks по сложности/риску конфликта;
3. размещать marks greedily по одной;
4. на каждом шаге выбирать лучший candidate с учётом уже поставленных marks;
5. после первичной раскладки делать короткий local-improvement pass.

Это даст:

- deterministic поведение;
- explainable score;
- быстрый runtime path;
- хороший базовый слой для будущего AI-agent orchestration.

### Что не делать на первом шаге

Пока не идти в:

- simulated annealing;
- integer programming;
- непрерывный глобальный поиск position.

Первый production-worthy шаг:

- `candidate positions + greedy placement + local improvement`

Уже после стабилизации этого deterministic слоя можно думать про:

- richer pair-selection;
- more advanced leader styles;
- AI-agent как orchestrator поверх candidate/scoring pipeline.

### Phase 8.1. Force-directed local improvement pass — partially completed

После greedy placement можно запустить force-directed (magnetic) pass как local improvement.

Текущий код уже содержит отдельный experimental runtime-path:

- `ArrangeMarksForce` / `arrange_marks_force`
- `ForceDirectedMarkPlacer`
- `ForceDirectedMarkItem`

Важно:

- этот path пока не заменяет основной `arrange_marks`
- force-directed solver сейчас существует как отдельный экспериментальный layout path и tuning playground

**Идея:** каждая метка притягивается к своей детали и отталкивается от соседних меток.
Система итеративно оседает — метки у своих деталей, без перекрытий.

**Исходные данные (всё уже есть):**
- `MarksViewContext.Marks` → позиция, размер (Width/Height), ModelId каждой метки
- `DrawingViewContext.Parts` → `SolidVertices` для контура и центроида каждой детали
- `DrawingViewContext.ViewScale` → масштаб для paper mm → model mm

**Сила притяжения к своей детали:**
- уже реализован polygon-aware path через nearest edge / nearest boundary
- для собственной детали solver сейчас использует own-part contour как основной attract source
- отдельно учитывается случай, когда центр метки оказался внутри собственного полигона

**Сила отталкивания от других меток:**
- current path уже использует OBB/polygon-aware repulsion через `LocalCorners`
- fallback AABB остаётся только запасным путём
- во втором проходе используется не только exact overlap, но и небольшой gap-aware separation signal

**Текущая реализация axis-constrained меток (BaseLinePlacing, AlongLinePlacing):**
- `Pass1`: axis-constrained marks двигаются только вдоль оси
- `Pass2`: для коллидирующих axis-constrained marks жёсткое axis-ограничение временно снимается
- вместо этого включается слабая поперечная пружина обратно к линии оси детали
- это позволяет:
  - разойтись с соседними marks
  - но не улететь далеко от baseline axis

**Текущий итерационный цикл experimental path:**
1. `Pass1`
   - двигаются все marks
   - учитываются только детали
   - mark-mark repulsion выключен
2. `Pass2`
   - двигаются только marks, которые после `Pass1` ещё конфликтуют
   - в расчёте участвуют все детали и все marks
   - включён mark-mark repulsion
   - для baseline/along-line marks работает weak return-to-axis-line

Внутри одной итерации solver уже считает смещения по snapshot-состоянию:

- сначала считает `dx/dy` для всех movable marks
- потом применяет их разом

Общий цикл:
```
for iter in 0..100:
    for each mark:
        compute attraction force to own part contour
        compute repulsion from all other marks
        apply constraints / axis-line return
        move by (fx, fy) * dt
    dt *= 0.98   // затухание
    stop early if total displacement < epsilon
```

Что уже реализовано в `ForceDirectedMarkPlacer`:

- own-part attraction
- foreign-part repulsion
- inside-own-polygon correction
- inside-foreign-polygon push-out
- OBB-based mark-mark repulsion
- mark gap in `Pass2`
- simultaneous update per iteration
- separate `Pass1Default` / `Pass2Default`
- normalized axis handling inside solver
- `KPerpRestoreAxis` for axis-constrained marks in `Pass1`
- weak `ReturnToAxisLine` for freed baseline/along-line marks in `Pass2`
- piecewise attraction spring:
  - logarithmic near field
  - linear far tail with clamp
- `PlaceInitial()` outlier recovery step for very distant free/leader marks

**Текущая интеграция:**
- не внутри `arrange_marks`
- а отдельной командой `arrange_marks_force`

То есть:
- `arrange_marks` остаётся основным context-aware candidate/scoring path
- `arrange_marks_force` — отдельный experimental solver

**Unit test:** 2 метки на одной детали → расходятся; 2 метки на разных деталях → каждая притягивается к своей.

**Рефакторинг force-path для `mark-mark` geometry — completed:**

Реализовано в `TryGetMarkRepulsion()`:

1. Если у обеих marks есть `LocalCorners` — полный polygon path:
   - `TryGetMinimumTranslationVector` для overlap cases
   - `TryGetPolygonGapVector` для gap-aware repulsion
   - touching / fully separated → `return false` (no repulsion)
2. `Width/Height` AABB fallback оставлен только для marks без OBB geometry (`// Degraded fallback for marks without OBB geometry`)
3. `TryGetPolygonGapVector` живёт в `PolygonGeometry` как geometry helper

Touching edge case сохранён: если polygon-ы только касаются (`gap = 0`) и overlap нет → repulsion не применяется.

Дополнительно зафиксированные pending tasks для force-path:

- вдоль-осевая пружина для `ConstrainToAxis` marks:
  - сейчас solver умеет удерживать такие marks поперёк оси
  - но отдельный controlled return вдоль оси как pending idea ещё не реализован
- продолжать держать нормализацию `AxisDx/AxisDy` внутри solver как обязательное invariant-требование:
  - latent bug уже был выявлен
  - внешняя нормализация не должна считаться достаточной гарантией
