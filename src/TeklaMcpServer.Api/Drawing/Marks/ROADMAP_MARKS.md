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

То есть execution path уже есть.

Что уже фактически завершено:

- `MarksViewContext` и `MarkContext` зафиксированы как внутренний factual layer;
- `MarksViewContextBuilder` стал каноническим builder-ом mark context;
- `get_drawing_marks` использует context-based projection;
- `arrange_marks` использует `MarkContext -> MarkLayoutItem`;
- `resolve_mark_overlaps` использует тот же context-based layout path.

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
- movement semantics: двигается mark body, `StartPoint` не меняется
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

### Phase 4. Evaluator

После стабилизации context:

- добавить deterministic mark evaluator/scorer;
- считать:
  - overlaps
  - outside/inside quality
  - leader-line quality
  - distance / readability signals

Но это не первый шаг.

### Phase 5. Snapshot pipeline

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

**Правильная стратегия (не реализована):**
1. Сначала разместить тело марки рядом с деталью но снаружи
2. Затем переместить якорь лидера в точку на контуре детали, ближайшую к телу марки

Для этого нужны:
- контур детали (уже есть в `PartPolygonsByModelId`)
- позиция тела марки после `ApplyPlacements`
- функция "ближайшая точка на полигоне к заданной точке"

## Следующий практический шаг

Следующий практический шаг:

- evaluator
- snapshots
