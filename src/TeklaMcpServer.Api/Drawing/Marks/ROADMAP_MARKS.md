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

Multi-mark layout сейчас идёт через force-directed path (Phase 8.1). Базовая схема уже стабилизирована и должна оставаться главным production path для `arrange_marks_force`.

Ближайший практический шаг после текущей синхронизации roadmap-а:

- не менять общую физику force solver-а;
- сначала довести диагностику leader/text conflicts до decision layer;
- затем сделать dry-run cleanup для лидеров:
  - не двигать body марки на первом шаге;
  - пробовать только безопасные варианты формы/якоря лидера;
  - оценивать own leader/text crossing, foreign text crossing, leader length и regression по foreign-part conflicts;
  - применять изменение только если dry-run явно лучше текущего состояния.

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
- `arrange_marks_force` — основной multi-mark путь, вызывается пользователем отдельной командой
- текущая цель `arrange_marks_force`: убрать mark-mark overlaps, уменьшить avoidable foreign-part overlaps и затем поправить leader anchors/диагностику лидеров

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
- `Equilibrium step`: axis-constrained marks двигаются только вдоль оси
- `Mark separation step`: для коллидирующих axis-constrained marks жёсткое axis-ограничение временно снимается
- вместо этого включается слабая поперечная пружина обратно к линии оси детали
- это позволяет:
  - разойтись с соседними marks
  - но не улететь далеко от baseline axis

**Текущий цикл `arrange_marks_force`:**
1. `Equilibrium`
   - двигаются все marks
   - учитываются только детали
   - mark-mark repulsion выключен
2. `Foreign cleanup`
   - последовательно уменьшает частичные пересечения меток с чужими деталями
   - не трогает `MarkInsideForeignPart` и `ForeignPartInsideMark`
3. `Axis separation`
   - для axis-based меток пробует прямое разъезжание конфликтующей пары вдоль осей
   - применяется до общего mark separation
4. `Mark separation`
   - двигаются только marks, которые после `Equilibrium` ещё конфликтуют
   - в расчёте участвуют все детали и все marks
   - включён mark-mark repulsion
   - для baseline/along-line marks работает weak return-to-axis-line
   - есть early exit, когда mark-mark overlaps среди `movableIds` устранены
5. `Final foreign cleanup`
   - после mark separation ещё раз уменьшает частичные foreign-part overlaps
   - не откатывает весь mark separation, но использует per-mark/global rollback внутри cleanup
6. `Apply + Leader anchor optimization`
   - сначала применяется body movement
   - затем leader anchor post-step выбирает безопасную точку крепления на детали
7. `Leader text diagnostics`
   - только при активном `PerfTrace`
   - считает пересечения leader polyline с own/foreign text polygons до/после layout

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
- mark gap in `Mark separation`
- simultaneous update per iteration
- separate `EquilibriumDefault` / `MarkSeparationDefault`
- normalized axis handling inside solver
- `KPerpRestoreAxis` for axis-constrained marks in `Equilibrium`
- weak `ReturnToAxisLine` for freed baseline/along-line marks in `Mark separation`
- piecewise attraction spring:
  - logarithmic near field
  - linear far tail with clamp
- leader-specific attraction:
  - `LeaderIdealDist = 6 мм бумаги * viewScale`
  - `LeaderComfortDist = 8 мм бумаги * viewScale`
  - extra linear attraction outside comfort zone
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

#### Completed high-impact tasks

**1. View-scale awareness (paper-mm policy semantics) — completed**

Все фиксированные distance-параметры (`IdealDist=25`, `MarkGapMm=2.0`, `PartRepelRadius=120`, `PartRepelSoftening=5.0`, `StopEpsilon=0.05`) заданы в drawing units. Имена с `Mm` и семантика не совпадают с реальностью — на scale 1:25 `MarkGapMm=2.0` = 0.08 мм на бумаге.

`farThreshold = markSize * FarDistanceFactor` (и outlier thresholds в `PlaceInitial`, завязанные на `min(Width, Height)`) уже пропорциональны размеру марки в drawing units, поэтому автоматически адаптируются под масштаб вида. Они не входят в список параметров, требующих paper-mm конверсии.

**Отменённые попытки и вывод:**

Подход "перевести координаты в paper-mm и оставить старые `EquilibriumDefault`/`MarkSeparationDefault` как есть" не сработал — марки улетали за границы чертежа. Причина: текущие константы (`KRepelPart=300`, `PartRepelRadius=120`, `IdealDist=25`, `PartRepelSoftening=5`, `StopEpsilon=0.05`) содержат implicit drawing-unit/view-scale семантику. После скалирования координат:

- `PartRepelRadius=120` превратился из ~5 мм paper в 120 мм paper — repulsion начал действовать через пол-листа
- `IdealDist=25` из ~1 мм paper стал 25 мм paper — равновесие сместилось далеко от детали
- `F = KRepelPart / (effectiveDist² + ε²)` при dist в 20-25 раз меньше выросла на сотни раз — силы взорвались

Последующая попытка исправить это через отдельные paper-mm defaults, coordinate conversion и view-bounds clamp тоже признана неверной постановкой для текущего solver-а. Это фактически создавало новую физику solver-а, а не нормализовало существующую.

Вывод — **координаты, полигоны, bbox, local corners и Tekla movement path должны оставаться в drawing units**.
`viewScale` нужен для перевода policy-порогов, заданных в paper-mm, в drawing units.

Правильная формула:

```text
geometry stays in drawing units
paperMm policy value * viewScale = drawing-unit solver value
```

**Реализованное правило:**

1. Coordinate conversion solver-а не делается:
   - не делить позиции marks на `viewScale`;
   - не делить polygons/bbox/local corners;
   - не менять `InsertionPoint` / `AxisOrigin` unit semantics.

2. Используются scale-aware factory для `ForcePassOptions`:
   - `ForcePassOptions.CreateEquilibriumForViewScale(viewScale)`
   - `ForcePassOptions.CreateMarkSeparationForViewScale(viewScale)`

3. В factory пересчитываются только distance/policy параметры:

   - `IdealDist = 4`
   - `MarkGapMm = 2`
   - `PartRepelRadius = 8`
   - `PartRepelSoftening = 0.75`
   - `StopEpsilon = 0.25`
   - `LeaderIdealDist = 6`
   - `LeaderComfortDist = 8`

   Эти значения трактуются как paper-mm и умножаются на `viewScale`.

4. Не пересчитываются физические коэффициенты:

   - `KAttract`
   - `KRepelPart`
   - `KRepelMark`
   - `InitialDt`
   - `DtDecay`
   - `MaxAttract`
   - coordinate data

   Цель — не новая физика, а paper-mm semantics для порогов.

5. `StopEpsilon` означает минимальный практически значимый сдвиг на бумаге:

   - `StopEpsilonPaperMm = 0.2..0.25`
   - `StopEpsilonDrawing = StopEpsilonPaperMm * viewScale`

6. Нужна продолжающаяся smoke-валидация на чертежах разных масштабов (например 1:15, 1:20, 1:25, 1:50). Успешный критерий:

   - нет марок, улетевших за рабочую область вида/листа;
   - overlaps после `arrange_marks_force` не хуже legacy path;
   - mark separation early exit продолжает срабатывать, когда конфликты устранены.

**View-bounds guard** остаётся отдельной задачей. Его нельзя смешивать с view-scale normalization, потому что это отдельное поведенческое ограничение, а не unit semantics.

**2. Foreign-part conflict diagnostics — completed**

Smoke на `[EW14S.3 - 1]` показал отдельный тип конфликта: метка может не пересекаться с другими метками, но full OBB/polygon метки лежит поверх чужой детали в 2D-проекции.

Реализовано:

- `ForeignPartOverlapKind` enum: `PartialForeignPartOverlap`, `MarkInsideForeignPart`, `ForeignPartInsideMark`
- `ForeignPartOverlapSummary` содержит per-kind счётчики (`PartialConflicts`, `MarkInsideConflicts`, `PartInsideConflicts`) и severity
- `ForeignPartOverlapAnalyzer.Analyze()` классифицирует каждый конфликт через `AllPointsInsideOrOnBoundary`
- logging в `arrange_marks_force_view` и `arrange_marks_force_foreign_cleanup` включает `kind=` в per-overlap строки

**3. Foreign-part cleanup before mark separation — completed**

Реализован через `CleanupForeignPartOverlaps` в `ForceDirectedMarkPlacer`. Запускается после equilibrium step, до mark separation step.

Назначение шага:

- для обрабатываемых меток уменьшить пересечения с любыми чужими деталями вида;
- чужая деталь не обязана сама иметь обрабатываемую метку;
- собственная деталь метки (`part.ModelId == mark.OwnModelId`) не считается foreign conflict;
- исправляются только частичные пересечения, где перемещение метки реально может помочь.

Реализованный подход — per-mark sequential cleanup:

- кандидаты на очистку: только `PartialForeignPartOverlap` конфликты (не `MarkInside`, не `PartInside`)
- марки обрабатываются в порядке убывания суммарной глубины перекрытия (worst first)
- для каждой марки: inner while loop до `maxStepsPerMark=25` принятых шагов
- на каждом шаге:
  1. пробуется axis-constrained шаг (для `ConstrainToAxis` марок)
  2. если axis шаг не помог — full unconstrained шаг с `allowEqualSeverity=true`
- нейтральные шаги отслеживаются: если `consecutiveNeutralSteps > 10` — марка отпускается
- per-mark rollback: если итоговая severity марки не лучше исходной — позиция восстанавливается
- global rollback: если суммарная `PartialSeverity` не улучшилась — все позиции восстанавливаются
- новые mark-mark overlaps не допускаются (проверка `CountMarkOverlapPairs`)
- `MarkInside` и `PartInside` конфликты не обрабатываются — layout не может их устранить перемещением

**4. Mark separation cleanup — completed**

Текущий mark-mark Relax step:

- двигаются marks, которые после equilibrium + foreign-part cleanup конфликтуют с другими marks
- включён mark-mark repulsion
- early exit по устранению mark-mark overlaps остаётся
- `collidingIds` / `markSeparationEarlyExit` должны относиться именно к mark-mark overlaps

Это сохраняет текущую удачную логику раздвижки меток, но запускает её после попытки убрать avoidable foreign-part conflicts.

**5. Final foreign-part cleanup — completed**

После mark separation возможна ситуация, когда шаг успешно раздвинул метки между собой, но одну или несколько меток снова сдвинул на чужую деталь.

Решение:

- после mark separation повторно запустить ту же `CleanupForeignPartOverlaps`;
- цель — уменьшить `PartialForeignPartOverlap` для обрабатываемых меток с любыми чужими деталями вида;
- не трогать `MarkInsideForeignPart` и `ForeignPartInsideMark`, потому что такие случаи часто являются особенностью 2D-проекции плотной 3D-сборки;
- не откатывать весь mark separation step;
- если cleanup ухудшает конкретную метку — откатывать только эту метку, как в текущей per-mark rollback logic;
- не допускать ухудшения mark-mark overlap результата после mark separation.

Фактическая целевая схема force path:

1. `Equilibrium` — поиск равновесия меток относительно своих деталей/осей.
2. `Foreign cleanup` — уменьшить частичные пересечения обрабатываемых меток с любыми чужими деталями вида.
3. `Axis separation` — прямое вдоль-осевое разъезжание axis-based конфликтующих пар.
4. `Mark separation` — раздвинуть метки между собой.
5. `Final foreign cleanup` — ещё раз уменьшить foreign-part conflicts, если mark separation их создал или усилил.

**6. Differentiated attraction by placing type — completed for leader/free marks**

Реализовано через `ForcePassOptions`:

- обычный `IdealDist = 4 мм бумаги * viewScale`
- `LeaderIdealDist = 6 мм бумаги * viewScale`
- `LeaderComfortDist = 8 мм бумаги * viewScale`
- `LeaderKExtraAttract = 0.8`
- `LeaderMaxExtraAttract = 40`

Смысл:

- leader-марка может быть чуть дальше от детали, потому что связь показывает leader;
- если leader-марка слишком далеко, включается дополнительное притяжение;
- baseline/along-line и прочие non-leader marks остаются ближе к детали через обычный `IdealDist`.

**7. Mark separation overlap-based early exit — completed**

Раньше mark-mark cleanup шёл до `MaxIterations=100` или `maxDisplacement < StopEpsilon`. Но настоящая цель этого pass-а — устранить mark-mark overlaps среди `movableIds`.

Решение: каждые N итераций (например, 5) считать `GetOverlappingMarkIds(currentPlacements).Intersect(movableIds).Count`. Если 0 — выход.

Этот пункт уже реализован для текущего mark separation step.

**8. Axis-prioritized mark separation movement — completed**

Для `BaseLinePlacing` / `AlongLinePlacing` на длинных деталях часто лучший способ разойтись с соседней меткой — сдвинуться вдоль оси детали, а не уходить поперёк на соседние детали.

Реализовано двумя слоями:

- `AxisMarkSeparationCleanup.Resolve(...)` до общего mark separation;
- `preferAxisStepForReturnToAxisMarks: true` внутри mark separation.

Поведение:

- для axis-based конфликтующих меток сначала пробуется projected step вдоль `AxisDx/AxisDy`;
- шаг принимается, если он уменьшает mark-mark overlap и не ухудшает foreign-part severity;
- если движение вдоль оси не помогает — используется обычный 2D force-step как fallback;
- поперечное движение по-прежнему удерживается слабой пружиной обратно к оси.

Цель — сохранить readable связь метки со своей длинной деталью и уменьшить случаи, когда mark-mark repulsion выталкивает baseline/along-line метку на чужую деталь.

**9. Leader text overlap diagnostics — completed**

Реализована диагностика, без изменения поведения:

- `LeaderTextOverlapAnalyzer`
- вход: text polygon марки + primary leader polyline
- считается пересечение leader polyline с:
  - собственным text polygon (`own`)
  - чужими text polygons (`foreign`)
- короткое касание собственного text polygon около leader end игнорируется через threshold `0.5 мм бумаги * viewScale`
- `arrange_marks_force_view` пишет агрегаты:
  - `leaderTextInitialCrossings`
  - `leaderTextInitialOwn`
  - `leaderTextInitialForeign`
  - `leaderTextInitialSeverity`
  - `leaderTextFinalCrossings`
  - `leaderTextFinalOwn`
  - `leaderTextFinalForeign`
  - `leaderTextFinalSeverity`
- отдельный detail trace:
  - `arrange_marks_force_leader_text`
  - `stage`, `markId`, `crossedMarkId`, `own`, `segmentIndex`, `severity`

Это diagnostic-only layer. Он нужен как база для будущего leader-shape cleanup.

#### High-impact pending tasks

**1. Mark separation dynamic movable set expansion**

Сейчас `movableIds` — только изначально коллидирующие после equilibrium марки. Остальные заморожены. Если коллидирующая марка М упирается в замороженную марку N, repulsion не может раздвинуть конфликт — N не двигается.

Решение: каждые N итераций обновлять `movableIds`, добавляя марки, которые сейчас перекрываются с кем-то из set. "Заражение" — конфликт распространяется по цепочке.

**2. Step acceptance policy in Relax**

Сейчас `Relax()` применяет `F*dt` независимо от того, создаётся ли новый overlap или ухудшается foreign-part severity. Компенсация косвенная — в следующей итерации repulsion толкает обратно. Приводит к "качанию", медленной сходимости и случаям, когда mark-mark repulsion может вытолкнуть метку на чужую деталь.

Решение:

- перед применением шага проверять mark-mark overlaps и foreign-part severity
- если шаг ухудшает foreign-part severity без mark-mark выгоды — пробовать half-step или отклонять
- если шаг уменьшает mark-mark overlap ценой небольшого projected foreign-part overlap — может быть допустим
- policy должна быть soft-hard, не absolute ban: 2D projected foreign-part overlap иногда приемлем в плотных 3D assemblies

Семантика такая же, как в `PlaceInitial` — там эта проверка уже есть для outlier recovery.

**3. Leader text cleanup dry-run**

Следующий кандидат на реализацию после диагностики.

Цель:

- уменьшить случаи, когда leader polyline проходит через собственный или чужой text box;
- не ухудшать body placement;
- не двигать body марки на первом шаге.

Рекомендуемый MVP:

- кандидаты:
  - shift anchor вдоль текущей грани собственной детали;
  - horizontal elbow;
  - vertical elbow;
  - anchor shift + elbow только если простой elbow не помогает;
- scoring:
  - own leader/text crossing;
  - foreign leader/text crossing;
  - foreign-part severity;
  - leader length growth;
  - body movement forbidden или очень большой penalty;
- apply только если dry-run candidate строго лучше текущего состояния.

**4. Explicit leader shape modes**

После dry-run diagnostics/cleanup можно добавлять явные shape modes:

- straight
- angled
- horizontal elbow
- vertical elbow

Public command parameter выносить только после стабилизации default behavior.

**5. View-bounds guard**

Остаётся отдельной задачей. Не смешивать с `viewScale`/paper-mm policy semantics.

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

- **Axis normalization invariant:** держать нормализацию `AxisDx/AxisDy` внутри solver как обязательный invariant. Latent bug уже был выявлен — внешняя нормализация не должна считаться достаточной гарантией.
- **Rejected leader-anchor precompute experiment:** не считать надёжным способом убрать body jump. Tekla после `LeaderLinePlacing.StartPoint` может пересчитать body/leader end, поэтому текущий безопасный путь — apply/reload/post-verify/body compensation.
