# svMCP — Roadmap

## Границы работ

- Активная разработка только в `src/` (`src/svMCP.sln`).
- `Assistant/` и `WebBridge/` — только справочные, не редактируем.

---

## Реализовано ✅

**Модель**
| Инструмент | Описание |
|---|---|
| `check_connection` | Соединение с Tekla |
| `get_selected_elements_properties` | Свойства Part, BoltGroup, Weld, RebarGroup |
| `get_selected_elements_total_weight` | Суммарный вес (кг) |
| `select_elements_by_class` | Выделить по классу |
| `filter_model_objects_by_type` | Фильтр по типу / Tekla filter expression |

**Чертежи**
| Инструмент | Описание |
|---|---|
| `list_drawings` / `find_drawings` / `find_drawings_by_properties` | Поиск чертежей |
| `open_drawing` / `close_drawing` | Открыть / закрыть |
| `export_drawings_to_pdf` | Экспорт в PDF |
| `create_general_arrangement_drawing` / `create_single_part_drawing` / `create_assembly_drawing` | Создать чертёж |
| `get_drawing_context` / `get_sheet_objects_debug` / `select_drawing_objects` / `filter_drawing_objects` | Контекст, диагностика и выделение |
| `get_drawing_views` | Виды + размеры листа (sheetWidth, sheetHeight) |
| `move_view` / `set_view_scale` / `fit_views_to_sheet` | Управление видами |
| `get_drawing_marks` / `create_part_marks` / `set_mark_content` / `delete_all_marks` | Марки, их bbox/OBB/resolvedGeometry, content, arrowhead и leader line данные |
| `resolve_mark_overlaps` / `arrange_marks` / `arrange_marks_no_collisions` | Расстановка марок |
| `get_drawing_dimensions` / `create_dimension` / `move_dimension` / `delete_dimension` / `place_control_diagonals` | Размеры: rich line-based read API уже есть, `place_control_diagonals` пока experimental |
| `get_part_geometry_in_view` / `get_all_parts_geometry_in_view` | Геометрия деталей в виде |
| `get_drawing_parts` / `get_grid_axes` | Объекты и сетка |
| `draw_debug_overlay` / `clear_debug_overlay` / `draw_selected_mark_part_axis_geometry` | Dev-only overlay слой и debug-геометрия марок |

**Архитектура**
- Персистентный TeklaBridge (`--loop`): существенно снижает latency повторных вызовов, bridge живёт всю сессию
- TS2021 + TS2025 поддержка
- `MarkGeometryHelper`: единая точка расчета геометрии меток для diagnostics/debug overlay
- `Drawing/` разнесён по подпапкам `Views`, `Marks`, `Geometry`, `Dimensions`, `Interaction`, `Query`, `Parsing`, `Creation`, `Parts`, `DebugOverlay`
- Legacy `place_views` удалён; основной путь расстановки видов — `fit_views_to_sheet`

**Геометрические утилиты**
- `ConvexHull` — Graham scan по 2D точкам (`Tekla.Structures.Geometry3d.Point`, Z игнорируется)
- `FarthestPointPair` — диаметр множества точек (две самые удалённые)

---

## К реализации

### Размеры
- `get_drawing_dimensions` уже расширен: `dimensionType`, `viewId/viewType`, `orientation`, `direction`, `topDirection`, `referenceLine`, `dimensionLine`, `leadLineMain/Second`
- текущая задача по размерам: перепроектировать internal grouping и spacing по эталону `D:\repos\svMCP\dim`
- публичные `arrange_dimensions` и `get_dimension_arrangement_debug` временно скрыты до завершения line-based redesign
- `add_dimension_point` — добавить точку в цепочку. Workaround: `delete` + `create` с новым набором точек
- Cumulative тип: сохранить стиль в Tekla UI → `create_dimension(..., attributesFile="cumulative")`
- `get_part_openings(modelId, viewId)` — проёмы в стенах через `part.GetBooleans()`
- Размеры как препятствия для марок: `StraightDimension.GetObjectAlignedBoundingBox()` → `CanMove=false`
- `place_control_diagonals`: перевести с bbox-экстремумов на реальные крайние точки видимого контура (без "точек в воздухе")
- `place_control_diagonals`: текст второй диагонали ставится на `distance * 2` только когда диагонали пересекаются (иначе тексты и так не накладываются)

### Виды — проекционная связь (часть `fit_views_to_sheet`) ✅

**Реализовано** в `DrawingProjectionAlignmentService`, вызывается из `fit_views_to_sheet` после расстановки.

#### AssemblyDrawing ✅
- Якорь: `AssemblyDrawing.AssemblyIdentifier` → `model.SelectModelObject()` → `Assembly.GetMainPart()` → `Part.CoordinateSystem.Origin`
- **FrontView ↔ TopView**: выравнивание по X
- **FrontView ↔ SectionView**: выравнивание по Y
- Если FrontView не найден — пропуск без ошибки

#### GADrawing с FrontView ✅
- Якорь: общая ось через `get_grid_axes`, GUID как первичный ключ, `Label+Direction` как fallback
- **FrontView ↔ TopView**: выравнивание по X (Direction="X" ось)
- **FrontView ↔ SectionView**: выравнивание по Y (Direction="Y" ось)

#### GADrawing без FrontView — соседская выравнивание по осям ✅
- Применяется когда все виды — SectionView (разрезы на отдельном листе GA — типичный случай)
- **Горизонтальные соседи** (|ΔoriginY| < 60mm на листе): выравниваются по Y через общую Direction="Y" ось (отметки уровней)
- **Вертикальные соседи** (|ΔoriginX| < 80mm на листе): выравниваются по X через общую Direction="X" ось
- Опорный вид: левый в паре (для Y), нижний в паре (для X)
- Ограничение: если требуемый сдвиг > 30mm на листе — пропуск (не ломать компоновку)
- Безопасность: проверка выхода за usable area, пересечения с reserved областями, и **пересечения с другими видами** (`IntersectsAnyView`) — проекционный сдвиг отменяется если после него вид перекроет другой вид
- **Pre-shift для TopView**: если над FrontView недостаточно места для TopView — FrontView и SectionView сдвигаются вниз перед выравниванием, чтобы TopView всегда оказывался выше FrontView

#### Масштабы `fit_views_to_sheet` ✅
Стандартный ряд: `1:1, 1:2, 1:5, 1:10, 1:15, 1:20, 1:25, 1:30, 1:40, 1:50, 1:60, 1:70, 1:75, 1:80, 1:100, 1:125, 1:150, 1:175, 1:200, 1:250, 1:300`

### Марки
- Obstacle-aware score (размеры, тексты, рамки видов)
- COG как якорь: `part.GetReportProperty("COG_X/Y/Z")` + трансформация в локальную СК вида
- Использовать `MarkGeometryHelper` внутри resolver/arrange, чтобы debug и layout считали одну и ту же геометрию

### Виды
- Добрать regression tests на `fit_views_to_sheet` для реальных sheet scenarios: reserved areas, tight packing, projection alignment после arrange
- При необходимости упростить orchestration в `TeklaDrawingViewApi.Layout.cs` и `DrawingProjectionAlignmentService`
- ~~Если тема таблиц снова станет приоритетной~~ **Таблицы работают** — см. ниже

### Геометрические утилиты (`TeklaMcpServer.Api/Algorithms/Geometry/`)
- Применение: контрольные размеры по диагонали, улучшение `FrontViewDrawingArrangeStrategy`, obstacle detection для марок

### Прочее
- `tidy_drawing` — комплексная команда (analyze / preview / apply)
- Трей-приложение + SSE/HTTP транспорт для продакшн деплоя

---

## Стратегия размеров

- Тики на **края рёбер** (bboxMin/bboxMax), не в центры — быстрее рулеткой
- **Вариант B** (одна грань): `0, 400, 800, ... 2500` — одна риска на ребро, монтажные схемы
- **Вариант A** (обе грани): `100, 300, 100, 300...` — КМД/деталировка
- **Общий (габаритный) размер ставить всегда** — складывать сегменты в уме ошибочно
- `GetProjectedShape.GetShape(partId, cs)` — реальный контур из `FaceEnumerator` для сложных профилей

---

## Известные ограничения

- **IPC-прокси**: объекты из `GetAllObjects()` — transparent proxies. `is Beam` всегда `false`. Использовать `GetType().Name`
- **BoltGrade**: не доступен через свойства, нужен `GetReportProperty("BOLT_GRADE")`
- **GetAllObjects()**: только верхний уровень; для компонентов нужен `component.GetChildren()` рекурсивно
- **Шаблонные объекты (штамп, таблицы) — ✅ РАБОТАЕТ через `PresentationConnection` + `LayoutManager.CloseEditor()`:**

  `sheet.GetAllObjects()` не возвращает элементы `.tpl`-шаблона, но через internal API работает следующая цепочка в `DrawingReservedAreaReader.ReadLayoutInfo()`:
  1. `LayoutManager.OpenEditor()` → открывает Layout Editor для чтения метаданных
  2. `TableLayout.GetMarginsAndSpaces()` → реальные отступы листа
  3. `TableLayout.GetCurrentTables()` → список `tableId` на текущем чертеже
  4. `LayoutManager.CloseEditor()` → **обязательно** закрыть ДО создания `PresentationConnection`
  5. `new PresentationConnection()` → открывает Layout Editor сам как побочный эффект
  6. `connection.Service.GetObjectPresentation(tableId)` → `Segment` с примитивами рендеринга → `TryGetSegmentBounds()` → точный bbox таблицы включая динамическую высоту (parts list)
  7. `LayoutManager.CloseEditor()` в `finally` → редактор закрывается, пользователь не видит мигания

  **Результат**: `fit_views_to_sheet` получает реальные reserved areas таблиц и не размещает виды поверх штампа/списка деталей.

  **⚠️ Критически важно: двухфазная архитектура `ReadLayoutInfo()`**

  `LayoutManager.OpenEditor()` и `new PresentationConnection()` **не могут** работать в одном блоке — `PresentationConnection` сам открывает Layout Editor как побочный эффект. Если редактор уже открыт нашим кодом, `PresentationConnection` бросает `"Unable to connect to TeklaStructures process"`.

  Правильная структура:
  ```
  // Фаза 1: метаданные (редактор открыт/закрыт явно)
  LayoutManager.OpenEditor()
  try { GetMarginsAndSpaces() + GetCurrentTables() }
  finally { LayoutManager.CloseEditor() }   // ← закрыть ДО фазы 2!

  // Фаза 2: геометрия (редактор открывается внутри PresentationConnection)
  try { new PresentationConnection() → GetObjectPresentation() }
  finally { LayoutManager.CloseEditor() }
  ```

  **⚠️ Деплой: зависимость `Tekla.Structures.GrpcContracts.dll`**

  `PresentationConnection` (из `DrawingPresentationModelInterface.dll`) требует `Tekla.Structures.GrpcContracts.dll` во время загрузки. Эта DLL **не входит** в стандартные зависимости Tekla NuGet-пакетов и не копируется автоматически.

  - Источник: `C:\TeklaStructures\2025.0\bin\Tekla.Structures.GrpcContracts.dll`
  - Должна лежать рядом с `TeklaBridge.exe` в папке расширений
  - Симптом отсутствия: `FileNotFoundException: Tekla.Structures.GrpcContracts` при первом вызове `PresentationConnection`
  - При обновлении TeklaBridge — проверить что этот файл присутствует в деплое

  **Ключевые находки по API таблиц:**
  - `LayoutTable.OverlapVithViews` — если `true`, таблица не создаёт reserved area (виды могут перекрывать декоративные элементы углов и зон)
  - Размеры таблицы **не хранятся** в `LayoutTable` — нужно читать из `Segment.Primitives[0/2]` (canvas-маркеры, паттерн из `QRpresentation.cs`): `Primitives[0]` = `LinePrimitive` с min-corner, `Primitives[2]` = `LinePrimitive` с max-corner. Это даёт точные boundaries без накопления всех примитивов.
  - Marker-based path (`Segment.Primitives[0/2]`) считается каноническим контрактом svMCP для table bounds и не должен подменяться общей аккумуляцией примитивов по умолчанию.
  - `TableLayout.GetMarginsAndSpaces(out top, out bottom, out left, out right)` — реальные отступы листа (на A3 = 10мм по умолчанию). Использовать `Math.Min` — метод возвращает и отступы краёв, и spacing между таблицами (spacing может быть 100+ мм), `Math.Max` дал бы неверно большой отступ.
  - `LayoutManager.GetDrawingFrames()` — фреймы листа для складок/рамки, не прямоугольники таблиц
  - Примитивы шаблона (`GetObjectPresentation`) возвращаются в полных координатах шаблона (до 722мм для A0-шаблона). На A3 canvas-маркеры дают правильные границы видимой части.
  - `GetCurrentTables()` возвращает IDs таблиц текущего чертежа; новый чертёж → другие IDs (переменные, не константы)
  - `tableId` из `GetCurrentTables()` — это не ID объекта в модели, а runtime ID шаблонного объекта для текущего сеанса Layout Editor

---

## Поддержка версий

| Версия | IPC | Особенности |
|---|---|---|
| **TS2021** | .NET Remoting, named pipes | Reflection-фикс `-:` → `-Console:` до `new Model()` |
| **TS2025** | Trimble.Remoting, MMF | TeklaBridge в `extensions\svMCP\` + `exe.config` с `<codeBase>` |

TS2025: NuGet `2025.0.0.0` vs FileVersion `2025.0.52577.0` → channel mismatch → деплой в extensions-папку обязателен. Генератор конфига: `C:\temp\GenConfig2.ps1`. TeklaMcpServer автовыбирает путь через `ResolveBridgePath()`.

---

## Персистентный TeklaBridge ✅ (2026-03-08)

`--loop` режим: bridge стартует один раз, команды идут через stdin/stdout JSON. Даёт заметный выигрыш на повторных коротких вызовах; по текущим локальным замерам около ~7× на серии `check_connection`.

**stdout-дисциплина:** только протокольные JSON-строки, одна на запрос. `Console.SetOut(teklaLog)` — **до** `ApplyTeklaChannelFixes()`. Автовосстановление: IPC-ошибка → `KillProcess()` → рестарт на следующем вызове.

---

## Архитектурный долг

- Довести текущий `DrawingCommandHandler` от partial-декомпозиции до отдельных доменных handler-классов, если это даст практическую пользу
- Расширить текущие smoke/transport tests до более строгих contract/snapshot-тестов на ключевые bridge-команды
- Selection cache (`SelectionCacheManager`) написан но не подключён к bridge handlers

---

## In-Process Plugin (идея)

`PluginBase` DLL загружается **внутрь Tekla.exe** → `new Model()` = прямой вызов в памяти, нуль IPC.

```csharp
[Plugin("svMcpServer")]
public class McpServerPlugin : PluginBase
{
    private static HttpListener _server; // статика живёт пока жива Tekla
    public override bool Run(List<InputDefinition> input)
    {
        if (_server == null) StartServer();
        return true;
    }
}
```

TeklaBridge читает `C:\temp\tekla_mcp_port.txt` → HTTP → плагин. Fallback: нет файла → Remoting.
Регистрация: `.inp` в `applications/`. Пример: `D:\repos\MpdEdit\MpdEdit.Core\Plugins\` (CutPlugin, SvPluginBase).
Объём: ~250 строк + `.inp`.
