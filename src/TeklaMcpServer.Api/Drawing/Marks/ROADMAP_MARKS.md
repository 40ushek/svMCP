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
- [MarkGeometryHelper.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Drawing/Marks/MarkGeometryHelper.cs)
- [MarkLayoutEngine.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/MarkLayoutEngine.cs)
- [MarkOverlapResolver.cs](/d:/repos/svMCP/src/TeklaMcpServer.Api/Algorithms/Marks/MarkOverlapResolver.cs)

То есть execution path уже есть.

Следующий естественный шаг:

- выделить стабильный `MarksViewContext`

## Этапы

### Phase 1. Naming and context boundary

Нужно сделать:

- ввести `MarksViewContext`;
- ввести `MarkContext`;
- ввести `MarkGeometry` как отдельный geometry block внутри context layer;
- зафиксировать границу между:
  - query DTO
  - context layer
  - layout algorithm layer

### Phase 2. Context builder

Нужно сделать builder, который собирает `MarksViewContext` из текущего runtime path:

- active drawing view
- runtime marks
- geometry helpers
- placement/axis signals

Важно:

- не использовать `MarkLayoutItem` как public/context model напрямую;
- не делать `DrawingMarkInfo` каноническим internal context.

### Phase 3. Read projection

После появления context layer:

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

## Следующий практический шаг

Первый практический шаг:

- зафиксировать contracts `MarksViewContext` и `MarkContext`

Только после этого:

- builder
- evaluator
- snapshots
