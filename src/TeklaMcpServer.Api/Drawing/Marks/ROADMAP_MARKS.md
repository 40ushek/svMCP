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
- `PartsBounds` не считается обязательным входом для marks на первом этапе.

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
- `arrange_marks` и `resolve_mark_overlaps` учитывают текстовые боксы размеров как
  fixed blockers:
  - `DimensionTextBoxContextLoader` собирает `DrawingViewContext.DimensionTextBoxes`
    из presentation primitives размеров;
  - `MarkLayoutFixedBlockerBuilder` передаёт только polygons в
    `MarkLayoutOptions.FixedTextBoxPolygons`;
  - `SimpleMarkCostEvaluator` штрафует кандидатов меток за пересечение с
    fixed blockers;
  - `MarkOverlapResolver` откатывает push/nudge, если метка попала в fixed
    blocker;
  - сами метки не добавляются в `FixedTextBoxPolygons`, mark-mark conflicts
    остаются динамической частью mark layout.
- Для этого path добавлена диагностика dimension blockers через `PerfTrace`:
  - `resolve_mark_overlaps_dimension_blockers`;
  - `arrange_marks_dimension_blockers`;
  - поля: `viewId`, `dimensionTextBoxes`, `fixedBlockers`, `marks`.
- базовая `leader-anchor` оптимизация уже встроена в `arrange_marks` как отдельный post-step после `ApplyPlacements`;
- `leader-anchor` path уже учитывает:
  - inward shift от ближайшей грани через нормаль к ребру;
  - corner avoidance (2mm clearance от вершин полигона);
  - halve-until-inside fallback для тонких деталей.

Текущее состояние dimension blockers:

- `arrange_marks` и `resolve_mark_overlaps` учитывают текстовые боксы размеров
  через `MarkLayoutOptions.FixedTextBoxPolygons`.
- `ArrangeMarksForce` / `arrange_marks_force` тоже учитывает текстовые боксы
  размеров на MVP-уровне:
  - `ForceMarkLayoutOrchestrator` создаёт `PresentationConnection`;
  - `BuildDrawingViewContext` заполняет `DrawingViewContext.DimensionTextBoxes`;
  - `ForceDimensionBlockerBuilder` превращает polygons текста размеров в
    synthetic `PartBbox` obstacles с отрицательными `ModelId`;
  - synthetic obstacles добавляются в общий `partBboxes`, поэтому участвуют в
    `PlaceInitial`, force repulsion и `CleanupForeignPartOverlaps`;
  - после `AxisMarkSeparationCleanup.Resolve(...)` добавлен безусловный
    post-axis obstacle cleanup.
- Для force-path намеренно не вводился новый `ForceObstacle`: текущий MVP
  переиспользует существующий `PartBbox` / `ForeignPartOverlapAnalyzer`
  механизм.

### Resolved: dimension blockers на shortened views

**Статус:** production-flow читает реальные presentation text boxes и подаёт
их в force-path как fixed dimension blockers. Для подтверждённого smoke-case
shortening mapper не нужен: `shorteningMode=none`, sources=`segment:7`.

Главная проблема оказалась не в направлении `ViewShorteningCoordinateMapper`,
а в том, что force-path иногда уходил в analytical/runtime fallback и терял
часть фактически нарисованных Tekla text boxes. Это особенно заметно на
absolute dimensions: один dimension object может нарисовать несколько текстов
например `6105`, `27`, `6133`. Поэтому 7 text boxes на таком виде — корректный
результат, а не ошибка подсчёта.

Актуальный контракт:

- основной источник dimension blockers — presentation primitives на уровне
  segment/source object;
- production trace должен показывать `sources=segment:N`;
- fallback по runtime/аналитике допустим только если presentation не вернул
  ни одного бокса;
- analytical fallback является приближением: он строит box по точкам и не
  гарантирует совпадение с тем, что Tekla реально рисует;
- mark geometry и part blocker geometry остаются без shortening-конвертации;
- mapper не применяется глобально к force-flow.

Smoke на shortened view `viewId=2988` подтвердил:

```text
dimension_text_box_loader: presentation=connected sources=9 boxes=7 shorteningMode=none hasShortening=True
arrange_marks_force_dimension_blockers: dimensionTextBoxes=7 dimensionBlockers=7 sources=segment:7
```

До исправления тот же view мог уходить в fallback:

```text
arrange_marks_force_dimension_blockers: dimensionTextBoxes=5 sources=dimensionSet.analyticalFallback:5
```

Это неверный источник для данного кейса: absolute dimension имеет несколько
реальных text boxes, которые analytical fallback не обязан восстановить.

Поведенческий smoke:

- когда blocker был построен из fallback, force видел конфликт с dimension
  blocker (`foreignInitialConflicts=1`) и мог сдвинуть mark;
- после перехода на `segment:7` текущая позиция mark уже не давала видимого
  конфликта (`foreignInitialConflicts=0`), поэтому повторные запуски давали
  только микросдвиг `net=(0.0,-0.1)`;
- `marksMovedCount=1` в таком случае означает, что apply path принял малое
  изменение, а не обязательно визуально заметный перенос mark.

#### Решение: `ViewShorteningCoordinateMapper`

Реализован собственный coordinate transform на основе двух runtime источников:

1. `View.GetVisibleAreaRestrictionBoxes()` — `AABB` видимых областей вида.
2. `view.Attributes.Shortening.Offset` — paper-mm gap между cut-сегментами,
   переводится в координаты вида как `Offset * Scale`.

Алгоритм (на каждую ось X / Y):

- собрать `AABB` → построить intervals `[Min..Max]`;
- отсортировать и слить пересекающиеся;
- между соседними intervals из координаты точки вычитается не весь raw gap,
  а `rawGap - spaceBetweenCutParts`, где `spaceBetweenCutParts = Offset * Scale`
  — это видимый break gap, который Tekla оставляет между cut-сегментами.
  То есть удаляется только "скрытая" часть пробела, а Tekla-видимый зазор
  сохраняется;
- если intervals ≤ 1 — identity (`HasShortening = false`).

Mapper остаётся полезным диагностическим и edge-case инструментом для проверки
raw/visual coordinate hypotheses на shortened views. Production default сейчас
не должен полагаться на mapper для presentation segment text boxes: smoke
подтвердил корректную работу при `shorteningMode=none`.

Файлы:

- `TeklaMcpServer.Api/Drawing/Geometry/ViewShorteningCoordinateMapper.cs` —
  алгоритм; public surface: `FromAabbs`, `FromVisibleBoxes`, `ConvertPoint`,
  `ConvertPolygon`, `HasShortening`, `HasShorteningX`, `HasShorteningY`,
  `XIntervals`, `YIntervals`.
- `TeklaMcpServer.Api/Drawing/Geometry/ViewShorteningAttributesReader.cs` —
  читает `view.Attributes.Shortening.Offset` и переводит в координаты вида;
  результат — `SpaceBetweenCutPartsInViewCoordinates`, который надо передать
  вторым аргументом в `FromAabbs(boxes, spaceBetweenCutParts)`.
- `TeklaMcpServer.Api/Drawing/Dimensions/Placement/DimensionDrawingTextBoxDebugReader.cs` —
  public debug facade, переиспользует `DimensionTextBoxContextLoader`.
- `TeklaMcpServer.Tests/ViewShorteningCoordinateMapperTests.cs` — 11 тестов:
  identity, X-only, Y-only, X+Y, polygon, точка в gap, точка на границе,
  invalid input.
- `TeklaMcpServer.Host/DrawingViewRestrictionBoxProbe.cs` — visual probe для
  эмпирической проверки raw/visual hypotheses. Mapper overlay используется
  только для сравнения координатных систем; текущий force default для
  presentation segment boxes — без mapper. Mark geometry и part blocker
  geometry проверяются без mapper. Сейчас Host тестовый: probe запускается из
  `Program.cs` напрямую, без аргумента командной строки.

#### Current production use: presentation-first blockers

##### Главный риск: двойное / неправильное shortening

Probe подтвердил, что mark geometry и part geometry уже находятся в нужной
системе координат для force-flow. Если применить mapper ко всем blockers "на
всякий случай", part blocker geometry уедет от детали и появятся ложные
конфликты. Если применить неверное направление transform к presentation text
boxes, появится двойное / неправильное shortening.

Поэтому **нельзя подключать mapper глобально к force-flow или presentation
collector. Любая будущая конвертация должна быть source-aware и включаться
только после отдельного visual smoke.**

##### Корректный порядок шагов

1. **Dimension text boxes** — читать presentation boxes до тяжёлой сборки
   part/bolt/grid context, чтобы не деградировать в fallback.

2. **Presentation-first provider** — сначала `DimensionDrawingTextBoxCollector`
   по presentation sources (`dimensionSet` + `segment`), затем runtime fallback,
   затем analytical fallback только если presentation ничего не дал.

3. **Trace source counts** — обязательно сохранять `sources=segment:N` /
   `runtimeFallback:N` / `dimensionSet.analyticalFallback:N`, чтобы сразу видеть
   деградацию источника.

4. **Marks и parts не конвертировать.**
   - mark geometry рисуется и сравнивается без mapper;
   - part blocker geometry рисуется и сравнивается без mapper;
   - mapper для них создаёт неверные дублирующие polygons.

5. **Force-flow** — не менять общую логику obstacles. Dimension text box
   polygons из `DrawingViewContext.DimensionTextBoxes` идут в
   `FixedTextBoxPolygons` / synthetic `PartBbox` obstacles. Дальше solver
   работает по прежней схеме.

6. **Debug trace** для каждого source во время сборки blockers:
   ```
   blocker_source kind=dimension_text_box source=segment converted=false hasShortening=true axes=X
   blocker_source kind=mark_geometry converted=false hasShortening=true axes=X
   blocker_source kind=part_polygon converted=false hasShortening=true axes=X
   ```
   Через месяц будет понятно почему blocker пришёл из presentation или fallback.

7. **`draw_dimension_text_boxes`** — должен использовать тот же
   presentation-first pipeline, иначе debug overlay может показывать не то, что
   реально использует force-flow.

##### Чего не делать

- Не применять mapper "на всякий случай" ко всем polygons.
- Не применять mapper к mark geometry и part blocker geometry.
- Не строить mapper повторно на каждом blocker / mark.
- Не считать analytical fallback эквивалентом Tekla presentation boxes.
- Не считать один dimension object равным одному text box: absolute dimensions
  могут давать несколько text boxes.
- Не подключать mapper к force-flow без debug trace по источникам и visual smoke.

Попытки решить через `Tekla.Structures.Drawing.Tools.DrawingCoordinateConverter`
описаны ниже в отдельном перечне; ни одна не дала shortening-aware координаты
текста размера внутри view.

Tekla support подтвердил: Open API сейчас не учитывает shortening при
преобразовании координат. `DrawingCoordinateConverter` обещает в документации
"empty areas in views", но эмпирически работает для transform между разными
coordinate systems (view ↔ sheet через origin/scale), а не для shortening
внутри одного view.

При этом `View.GetVisibleAreaRestrictionBoxes()` возвращает `AABB` видимых
областей вида, что делает задачу решаемой через собственный mapper.

Актуальный алгоритм и применение описаны выше в разделе
"Решение: `ViewShorteningCoordinateMapper`". Старое описание из этого места
удалено, чтобы roadmap не противоречил сам себе.

Что подтверждено эмпирически:

- На views **без shortening** — dimension blockers работают корректно,
  визуально метки уходят от размерных текстов, конфликты устраняются.
- На views **с shortening** — force-flow работает с presentation-first
  dimension blockers. Подтверждённый smoke показал `sources=segment:7` и
  `shorteningMode=none`; это корректнее, чем fallback `5` boxes. Mapper
  остаётся диагностическим / edge-case инструментом, но не применяется
  глобально и не нужен для подтверждённого presentation segment path.

Открытые edge-cases (не критичны для текущего MVP):

- Поведение `CutSkewParts = true` (наклонные cut planes) — пока не проверено.
- Несколько independent shortening intervals на одной оси (3+ visible boxes)
  — алгоритм корректен по тестам, но empirically на реальном чертеже с 3+
  shortening regions ещё не проверен.
- Mapper берёт `Offset` из `view.Attributes.Shortening.Offset` — если
  пользователь сменил Offset после построения чертежа без regenerate, mapper
  может использовать stale значение. Стоит подтвердить runtime обновление.

#### Tekla API references

Документация по `DrawingCoordinateConverter` и связанным классам, изученная во
время попыток решения:

- [DrawingCoordinateConverter Class](https://developer.tekla.com/doc/tekla-structures/2024/drawing-coordinate-converter-class-25557)
  — описание класса. Заявлено: "used to move coordinates from one view to
  another. This tool takes into account the empty areas in the views."
  Эмпирически: трактуется как inter-view transform (view ↔ sheet),
  shortening внутри одного и того же view не применяет.
- [DrawingCoordinateConverter Methods](https://developer.tekla.com/tekla-structures/api/22/12586)
  — список доступных перегрузок `Convert`.
- [DrawingCoordinateConverter.Convert(ViewBase, ViewBase, Point)](https://developer.tekla.com/tekla-structures/api/22/12589)
  — single-point overload, namespace `Tekla.Structures.Drawing.Tools`.
- [View.ViewAttributes.Shortening Property](https://developer.tekla.com/doc/tekla-structures/2024/shortening-property-25362)
  — точка доступа к настройкам shortening на уровне view; даёт только
  конфигурацию, не runtime ranges.
- [View.ViewShorteningAttributes Constructor](https://developer.tekla.com/doc/tekla-structures/2025/view-view-shortening-attributes-constructor-boolean-boolean-double-double-view-shortening-cut-part-type-50535)
  — поля: `CutParts`, `CutSkewParts`, `MinimumLength`, `Offset`, `CutPartType`.
  Чисто декларативные настройки; **нет** API для получения фактических
  диапазонов укорочения, применённых к виду.
- `View.GetVisibleAreaRestrictionBoxes()` — support-recommended runtime source
  для проверки наличия shortening и получения `AABB` видимых областей. Эти
  boxes можно использовать как основу собственного shortening mapper-а.

#### Документированный пример использования `DrawingCoordinateConverter`

Из официальной документации (view → sheet через `PointList`):

```csharp
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.Tools;
using Tekla.Structures.Geometry3d;

DrawingHandler DrawingHandler = new DrawingHandler();
ViewBase sheet = DrawingHandler.GetActiveDrawing().GetSheet();
DrawingObjectEnumerator Views = sheet.GetAllObjects(typeof(View));
Views.MoveNext();
View myView = Views.Current as View;

PointList PointsInView = new PointList();
PointsInView.Add(new Point(0, 0));
PointsInView.Add(new Point(100, 0));
PointsInView.Add(new Point(100, 100));

Polygon polygonInView = new Polygon(myView, PointsInView);
polygonInView.Insert();

PointList PointsInSheet = DrawingCoordinateConverter.Convert(myView, sheet, PointsInView);
Polygon polygon = new Polygon(sheet, PointsInSheet);
polygon.Insert();
```

В этом примере точки `(0,0)`, `(100,0)`, `(100,100)` — это **drawing units
внутри view** (paper-mm координаты относительно origin вида). Конвертер
переводит их в координаты sheet, чтобы тот же полигон можно было нарисовать
как sheet object.

#### Перечень попыток, которые не дали shortening-aware результата

1. `Convert(view, view, point)` — identity для одного и того же view;
   shortening не применяется.
2. `Convert(view, sheet, point)` + рендер полигона как sheet object —
   полигон рисуется в произвольной точке, не на текстах размеров.
3. То же + предварительное деление координат на `viewScale` — полигон
   попадает на правильную позицию, но в `viewScale` раз меньше реального
   размера текста (двойное масштабирование).
4. Без деления, прямой `Convert(view, sheet, point)` — повтор п.2.

Tekla Open API публично **не даёт готового shortening-aware transform** для
координат внутри view, но `GetVisibleAreaRestrictionBoxes()` даёт visible boxes,
на базе которых можно построить собственный transform через вычитание gaps
между boxes.

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
   - учитываются детали и synthetic dimension text blockers
   - mark-mark repulsion выключен
2. `Foreign/dimension cleanup`
   - последовательно уменьшает частичные пересечения меток с чужими деталями
   - и synthetic dimension text blockers
   - для real parts не трогает `MarkInsideForeignPart` и `ForeignPartInsideMark`
3. `Axis separation`
   - для axis-based меток пробует прямое разъезжание конфликтующей пары вдоль осей
   - применяется до общего mark separation
4. `Post-axis obstacle cleanup`
   - безусловно запускается после `Axis separation`
   - нужен потому, что axis step может сдвинуть метку в part/dimension obstacle
   - выполняется до вычисления `collidingIds` для mark separation
5. `Mark separation`
   - двигаются только marks, которые после `Equilibrium` ещё конфликтуют
   - в расчёте участвуют все детали и все marks
   - включён mark-mark repulsion
   - для baseline/along-line marks работает weak return-to-axis-line
   - есть early exit, когда mark-mark overlaps среди `movableIds` устранены
6. `Final foreign cleanup`
   - после mark separation ещё раз уменьшает частичные foreign-part overlaps
   - не откатывает весь mark separation, но использует per-mark/global rollback внутри cleanup
7. `Apply + Leader anchor optimization`
   - сначала применяется body movement
   - затем leader anchor post-step выбирает безопасную точку крепления на детали
8. `Leader text diagnostics`
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

#### Completed high-impact task: Dimension text blockers in force path

Цель:

- `arrange_marks_force` должен учитывать текстовые боксы размеров так же, как
  обычные `arrange_marks` / `resolve_mark_overlaps` уже учитывают их через
  `MarkLayoutOptions.FixedTextBoxPolygons`;
- метки не должны заезжать на текст размеров во время force-layout;
- текст размеров остаётся неподвижным obstacle, сами размеры на этом этапе не
  двигаются.

Status: completed MVP.

Почему нельзя просто использовать `FixedTextBoxPolygons`:

- `ArrangeMarksForce` не использует `MarkLayoutEngine.Arrange()`;
- force-path работает через `ForceMarkLayoutOrchestrator` и
  `ForceDirectedMarkPlacer`;
- текущий force-solver принимает неподвижные препятствия как `PartBbox` в
  параметре `allParts`.

Выбранный MVP-подход:

- не вводить новый `ForceObstacle` на первом шаге;
- использовать существующий `PartBbox` как carrier для dimension text polygon;
- для каждого dimension text polygon построить:
  - `ModelId = -(index + 1)`;
  - `MinX/MinY/MaxX/MaxY` из bounds polygon-а;
  - `Polygon = dimension text polygon`.

Почему отрицательный `ModelId` допустим:

- `ForeignPartOverlapAnalyzer` и cleanup исключают только собственную деталь:
  `part.ModelId == mark.OwnModelId`;
- `OwnModelId` метки приходит из реальной модели и не должен совпасть с
  отрицательным synthetic id;
- значит dimension text blockers всегда считаются foreign obstacles для всех
  меток, что соответствует нужному поведению.

Точки встраивания:

1. `ForceMarkLayoutOrchestrator.Arrange(...)`
   - создаёт `PresentationConnection` на время всего force-run;
   - внешний `TeklaDrawingMarkApi.ArrangeMarksForce` не меняет сигнатуру.

2. `ForceMarkLayoutOrchestrator.BuildDrawingViewContext(...)`
   - принимает `PresentationConnection?`;
   - после `DrawingViewContextBuilder.Build(...)` вызвать
     `DimensionTextBoxContextLoader.PopulateDimensionTextBoxes(...)`.

3. После построения `partBboxes`
   - получает `dimensionBlockers =
     MarkLayoutFixedBlockerBuilder.BuildDimensionTextBoxPolygons(viewContext)`;
   - добавляет synthetic `PartBbox` entries в тот же список obstacles;
   - логирует:
     - `dimensionTextBoxes`;
     - `dimensionBlockers`;
     - `partObstacles`;
     - `totalObstacles`.

4. `ForceDirectedMarkPlacer.PlaceInitial(...)`
   - без изменения логики: `WouldOverlapForeignPart(...)` учитывает
     dimension blockers как part obstacles.

5. `ForceDirectedMarkPlacer.Relax(...)`
   - без изменения основной физики: `ComputeForce(...)` уже считает repulsion
     от `allParts`;
   - dimension text blockers входят в этот же force component.

6. `CleanupForeignPartOverlaps(...)`
   - без изменения основного cleanup algorithm;
   - dimension text blockers участвуют в
     `ForeignPartOverlapAnalyzer.Analyze(...)` как polygon obstacles.

Обязательная правка порядка cleanup:

- прежний final cleanup запускался только если `markSeparationResult.StopReason
  != ForceRelaxStopReason.NotRun`;
- это было недостаточно, потому что `AxisMarkSeparationCleanup.Resolve(...)`
  выполняется до mark separation и может сдвинуть axis-mark в obstacle даже если
  `collidingIds.Count == 0`;
- добавлен новый отдельный post-axis cleanup сразу после
  `AxisMarkSeparationCleanup.Resolve(...)`, до mark separation;
- этот post-axis cleanup запускается безусловно, даже если последующий
  mark separation не запустится;
- существующий final/post-mark cleanup после mark separation можно сохранить
  отдельным шагом, потому что mark separation тоже может снова создать obstacle
  conflict.

Целевой force-cycle после подключения blockers:

1. `Equilibrium`
   - attraction к own-part;
   - repulsion от real parts + dimension text blockers.
2. `Foreign/dimension cleanup`
   - уменьшает частичные пересечения с real parts и dimension blockers.
3. `Axis separation`
   - раздвигает axis-based mark-mark conflicts.
4. `Post-axis obstacle cleanup`
   - безусловно чистит пересечения, которые мог создать axis step.
5. `Mark separation`
   - если есть mark-mark conflicts, запускает mark-mark repulsion.
6. `Post-mark obstacle cleanup`
   - повторная очистка после mark separation, если mark separation запускался
     или если нужна единая финальная проверка.
7. `Apply + leader anchor optimization + leader text diagnostics`
   - остаются отдельными post-steps.

Поведенческие ограничения MVP:

- dimension text blockers не двигаются;
- сами размеры не переставляются;
- для real parts `MarkInsideForeignPart` и `ForeignPartInsideMark` остаются
  non-fixable categories, как и сейчас;
- для synthetic dimension blockers семантика другая: если dimension text box
  полностью находится внутри mark polygon (`ForeignPartInsideMark`), это всё
  ещё реальная текстовая коллизия, которую нужно как минимум диагностировать;
- MVP может сначала логировать такие случаи как residual dimension blocker
  conflicts, но roadmap не должен закреплять их как permanently non-fixable.

Диагностика:

- добавлен отдельный trace event:
  `arrange_marks_force_dimension_blockers`;
- fields:
  - `viewId`;
  - `dimensionTextBoxes`;
  - `dimensionBlockers`;
  - `partObstacles`;
  - `totalObstacles`;
- `arrange_marks_force_view` также содержит `postAxisCleanup...` summary fields.

Тест-план:

1. Unit test для conversion dimension polygon → synthetic `PartBbox`:
   - bounds считаются правильно через `PolygonGeometry.GetBounds(polygon)`;
   - AABB берётся из фактического polygon-а, а не из внешних cached
     `DrawingTextBox.MinX/MaxX/MinY/MaxY`;
   - ids отрицательные и уникальные.

2. Unit test для force repulsion:
   - mark рядом с synthetic obstacle после `Relax(...)` смещается от obstacle.

3. Unit test для cleanup:
   - mark частично пересекает synthetic obstacle;
   - `CleanupForeignPartOverlaps(...)` уменьшает severity.

4. Regression test для own-part exclusion:
   - real own part всё ещё исключается через `OwnModelId`;
   - synthetic negative blocker не исключается.

5. Manual smoke в Tekla:
   - включить `PerfTrace`;
   - проверить `dimensionTextBoxes > 0`;
   - проверить `dimensionBlockers > 0`;
   - сравнить before/after на чертеже с текстом размеров рядом с метками.

Выполненные тесты MVP:

- `BuildSyntheticObstacles_ComputesBoundsFromPolygonGetBounds`;
- `BuildSyntheticObstacles_AssignsNegativeUniqueIds`;
- `BuildSyntheticObstacles_SkipsDegeneratePolygons`;
- `BuildSyntheticObstacles_SkipsCollinearZeroAreaPolygons`;
- `BuildSyntheticObstacles_SkipsZeroWidthPolygons`.

Остаётся как follow-up:

- integration/unit test на фактическое влияние synthetic blocker на
  `Relax(...)` или `CleanupForeignPartOverlaps(...)`;
- residual diagnostics для случаев, где synthetic dimension blocker полностью
  внутри mark polygon (`ForeignPartInsideMark`);
- проверить поведение на реальных Tekla drawings с `PerfTrace`.

Future refactor, только если MVP подтвердится:

- переименовать `PartBbox` в более общий internal тип (`ForceObstacle` или
  similar);
- добавить `Kind = Part | DimensionText`;
- разделить trace на `foreign_part` и `dimension_blocker`;
- сохранить текущий force behavior без изменения физики.

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

Следующий кандидат на реализацию после диагностики и проверки реального чертежа.

Цель:

- уменьшить случаи, когда leader polyline проходит через собственный или чужой text box;
- не ухудшать body placement;
- не двигать body марки на первом шаге.

Текущий вывод по Tekla API / `markAligner` reference:

- `LeaderLinePlacing.StartPoint` можно менять, но Tekla всё равно владеет частью связки `leader end / text body`;
- даже при in-place изменении `StartPoint` Tekla может пересчитать body/leader end для некоторых anchor-позиций;
- поэтому `anchor shift` не должен быть основным способом чинить self-crossing leader/text;
- безопасный путь — apply/reload/post-verify остаётся обязательным;
- для self-crossing cleanup первым кандидатом пробовать `LeaderLine.ElbowPoints`, потому что elbow меняет shape лидера без дальнего переноса anchor/body.

Рекомендуемый MVP v2:

- кандидаты:
  - horizontal elbow;
  - vertical elbow;
  - small anchor shift вдоль текущей грани собственной детали только как fallback;
  - anchor shift + elbow только если простой elbow не помогает;
- scoring:
  - own leader/text crossing;
  - foreign leader/text crossing;
  - foreign-part severity;
  - leader length growth;
  - body movement forbidden или очень большой penalty;
- apply только если dry-run candidate строго лучше текущего состояния;
- после apply обязательно reload и проверка:
  - body не сдвинулся больше `MovementVerificationEpsilon`;
  - фактический leader/text severity уменьшился;
  - если Tekla пересобрала body/leader не так, как ожидалось — rollback.

Implementation notes:

- elbow применять через runtime `LeaderLine.ElbowPoints.Clear/Add/Modify`, а не через пересоздание `LeaderLinePlacing`;
- anchor-only изменения делать только через существующий `LeaderLinePlacing.StartPoint` и с последующей factual verification;
- не коммитить каждую метку отдельно: `Modify()` per mark, затем batch `Drawing.CommitChanges()`;
- сохранять подробный PerfTrace для accepted/rejected candidates: `kind`, projected severity, actual severity, body shift.

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
