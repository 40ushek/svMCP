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

Multi-mark layout идёт через force-directed path (Phase 8.1). Приоритетные pending items там — `View-scale awareness`, `Differentiated IdealDist by placing type`, `Pass2 overlap-based early exit`, `Pass2 dynamic movable set expansion`, `Hard constraint в Relax`. Подробности в разделе Phase 8.1.

Leader geometry отдельной линией:

- full pair-selection для `anchor point` + `leader end point / mark body point` (Phase 5.3)
- затем explicit leader-shape modes (Phase 5.4)

## Phase 8. Multi-mark layout по аналогии с cartographic labeling — deferred

Подход через cartographic-style `candidate positions + greedy placement + local improvement` отложен.

Причины:

- кандидаты требуют ordering/sorting логики, sensitive к эвристикам;
- greedy placement зависит от удачного ordering — ранние marks могут занять место, нужное поздним;
- force-directed path (Phase 8.1) уже работает как full local improvement от текущих позиций без кандидатов и даёт приемлемый результат (8 → 1 overlap после `arrange_marks_force` + `arrange_marks_no_collisions`).

Основной production path для multi-mark layout — **Phase 8.1 (force-directed)**.

Возврат к cartographic-candidate подходу имеет смысл только если upper bound качества force-directed окажется недостаточным для практических задач.

### Phase 8.1. Force-directed multi-mark layout — primary path

Поскольку Phase 8 (cartographic candidates) отложена, force-directed solver объявлен основным подходом к multi-mark layout.

Путь реализован как:

- `ArrangeMarksForce` / `arrange_marks_force`
- `ForceDirectedMarkPlacer`
- `ForceDirectedMarkItem`

Отношение к `arrange_marks`:

- `arrange_marks` остаётся context-aware candidate/scoring path для одиночной расстановки
- `arrange_marks_force` — основной multi-mark путь, вызывается пользователем отдельной командой; ряд high-impact задач остаётся открытым (см. pending tasks ниже)
- пользовательская связка `arrange_marks_force` + `arrange_marks_no_collisions` даёт приемлемое качество (8 → 1 overlap)

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
    stop early if max displacement < epsilon
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

#### High-impact pending tasks

**1. View-scale awareness (paper-mm internal semantics)**

Все фиксированные distance-параметры (`IdealDist=25`, `MarkGapMm=2.0`, `PartRepelRadius=120`, `PartRepelSoftening=5.0`, `StopEpsilon=0.05`) заданы в drawing units. Имена с `Mm` и семантика не совпадают с реальностью — на scale 1:25 `MarkGapMm=2.0` = 0.08 мм на бумаге.

`farThreshold = markSize * FarDistanceFactor` (и outlier thresholds в `PlaceInitial`, завязанные на `min(Width, Height)`) уже пропорциональны размеру марки в drawing units, поэтому автоматически адаптируются под масштаб вида. Они не входят в список параметров, требующих paper-mm конверсии.

Предлагаемое решение (Вариант B):

- на входе в `Relax()` делить все координаты (позиции меток, полигоны деталей, LocalCorners) на `viewScale`
- вся физика идёт в paper-mm — все константы приобретают реальный физический смысл
- на выходе умножать позиции обратно в drawing units
- одна точка конверсии на границе, одна unit system внутри solver
- `PlaceInitial`, `WouldOverlapForeignPart`, `WouldOverlapOtherMark` тоже работают в paper-mm
- `viewScale` прокидывается в `ForcePassOptions` или параметром в `Relax()`

**2. Differentiated attraction by placing type**

Сейчас один `IdealDist` для всех марок — solver тянет любую марку к ближайшей грани детали.

Для leader-марок это не нужно: `LeaderAnchorResolver` всё равно перепривязывает anchor к ближайшей грани детали после solver. Значит leader-марка может стоять в "разумной близости" (10-15 мм на бумаге), приоритет — collision-free.

Для baseline/along-line марок (без лидера) наоборот — они должны прилегать к детали (3-5 мм на бумаге).

Решение:

- разные `IdealDist` / `KAttract` для leader vs non-leader
- либо отдельные `ForcePassOptions` per placing type
- либо per-mark override на `ForceDirectedMarkItem`

**3. Pass2 overlap-based early exit**

Сейчас Pass2 всегда идёт до `MaxIterations=100` или `maxDisplacement < StopEpsilon`. Но настоящая цель Pass2 — устранить overlaps среди `movableIds`.

Решение: каждые N итераций (например, 5) считать `GetOverlappingMarkIds(currentPlacements).Intersect(movableIds).Count`. Если 0 — выход.

Простое изменение, ускоряет работу (Pass2 сейчас ~200-600мс на чертёж).

**4. Pass2 dynamic movable set expansion**

Сейчас `movableIds` — только изначально коллидирующие после Pass1 марки. Остальные заморожены. Если коллидирующая марка М упирается в замороженную марку N, repulsion не может раздвинуть конфликт — N не двигается.

Решение: каждые N итераций обновлять `movableIds`, добавляя марки, которые сейчас перекрываются с кем-то из set. "Заражение" — конфликт распространяется по цепочке.

**5. Hard constraint в Relax (no new overlaps)**

Сейчас `Relax()` применяет `F*dt` независимо от того, создаётся ли новый overlap. Компенсация косвенная — в следующей итерации repulsion толкает обратно. Приводит к "качанию" и медленной сходимости.

Решение:

- перед применением шага проверять: создаст ли он новый overlap (через `WouldOverlap*`)
- если до движения конфликтов не было → запрещать шаги, создающие новые (аналогично `PlaceInitial`)
- если были → разрешать шаг только если количество overlaps не увеличилось

Семантика такая же, как в `PlaceInitial` — там эта проверка уже есть для outlier recovery.

#### Medium-impact pending tasks

**6. Leader crossing swap post-process**

В `arrange_marks` есть `CalculateLeaderCrossingPenalty` (штраф за `SegmentsProperlyIntersect`). В force solver — ничего.

Решение: после force solver проверить все пары leader-марок. Если их лидеры пересекаются — попробовать swap позиций (М ↔ N). Применить если:

- после swap пересечений меньше
- новых overlaps не появилось

Локально, детерминированно, простая реализация.

Альтернативы (дороже):

- force-based: третий компонент силы для leader-leader отталкивания на каждой итерации — O(N²) per iter
- quadrant affinity в attraction — требует переделки attraction-логики

#### Phase 9-level diagnostics / tuning

- **Convergence diagnostics:** в `arrange_marks_force_view` перф-трейс добавлять `converged=true/false` (reached StopEpsilon или capped на MaxIterations)
- **Adaptive dt per mark:** марки с большой итоговой силой — меньший dt, чтобы не проскочить равновесие
- **Multi-pass с нарастающим `MarkGapMm`:** начать с 1 мм, поднимать до 3 мм если ранние стадии сошлись — даёт "комфортный" gap без жёстких начальных требований
- **Stuck mark detection:** марка не двигалась 20 итераций → заморозить на оставшиеся итерации, не тратить compute

#### Прочие pending items

- **KAlongAxisRestore (experiment, not committed):** вдоль-осевая пружина для baseline/along-line марок, когда они освобождаются в Pass2. Параметр `KAlongAxisRestore` и метод `ApplyAxisAnchorSpring` существуют в рабочей копии как незакоммиченный эксперимент. Коммит отложен до появления практического случая, когда freed baseline-марки уезжают вдоль оси детали.
- **Axis normalization invariant:** держать нормализацию `AxisDx/AxisDy` внутри solver как обязательный invariant. Latent bug уже был выявлен — внешняя нормализация не должна считаться достаточной гарантией.
