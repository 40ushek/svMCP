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
| `move_view` / `set_view_scale` / `place_views` / `fit_views_to_sheet` | Управление видами |
| `get_drawing_marks` / `create_part_marks` / `set_mark_content` / `delete_all_marks` | Марки, их bbox/OBB/resolvedGeometry, content, arrowhead и leader line данные |
| `resolve_mark_overlaps` / `arrange_marks` | Расстановка марок |
| `get_drawing_dimensions` / `create_dimension` / `move_dimension` / `delete_dimension` / `place_control_diagonals` | Размеры (`place_control_diagonals` пока experimental) |
| `get_part_geometry_in_view` / `get_all_parts_geometry_in_view` | Геометрия деталей в виде |
| `get_drawing_parts` / `get_grid_axes` | Объекты и сетка |
| `draw_debug_overlay` / `clear_debug_overlay` / `draw_selected_mark_part_axis_geometry` | Dev-only overlay слой и debug-геометрия марок |

**Архитектура**
- Персистентный TeklaBridge (`--loop`): существенно снижает latency повторных вызовов, bridge живёт всю сессию
- TS2021 + TS2025 поддержка
- `MarkGeometryHelper`: единая точка расчета геометрии меток для diagnostics/debug overlay

**Геометрические утилиты**
- `ConvexHull` — Graham scan по 2D точкам (`Tekla.Structures.Geometry3d.Point`, Z игнорируется)
- `FarthestPointPair` — диаметр множества точек (две самые удалённые)

---

## К реализации

### Размеры
- `add_dimension_point` — добавить точку в цепочку. Workaround: `delete` + `create` с новым набором точек
- Cumulative тип: сохранить стиль в Tekla UI → `create_dimension(..., attributesFile="cumulative")`
- `get_part_openings(modelId, viewId)` — проёмы в стенах через `part.GetBooleans()`
- Размеры как препятствия для марок: `StraightDimension.GetObjectAlignedBoundingBox()` → `CanMove=false`
- `place_control_diagonals`: перевести с bbox-экстремумов на реальные крайние точки видимого контура (без "точек в воздухе")
- `place_control_diagonals`: текст второй диагонали ставится на `distance * 2` только когда диагонали пересекаются (иначе тексты и так не накладываются)

### Виды — проекционная связь (часть `fit_views_to_sheet`)
- После расстановки видов выровнять их по проекционной связи:
  - **FrontView ↔ SectionView**: выравнивание по Y — одна и та же деталь/ось должна быть на одной высоте на листе
  - **FrontView ↔ TopView**: выравнивание по X — одна и та же деталь/ось на одной вертикали
- Алгоритм: `get_part_geometry_in_view` / `get_grid_axes` для опорного объекта → пересчёт из локальных координат вида в лист-координаты с учётом `Origin`, `Scale` и post-correction рамки вида → `move_view`
- Опорный объект зависит от типа чертежа:
  - **AssemblyDrawing**: из чертежа получить `GUID` сборки → в модели получить сборку по `GUID` → взять её главную деталь (`Assembly.GetMainPart()`) и СК главной детали как общий якорь → проецировать её в каждый вид через `TransformationPlane(view.DisplayCoordinateSystem)` → view-local координаты → лист
  - **GADrawing**: общая ось (`get_grid_axes`) — использовать `GUID` оси как основной ключ идентификации между видами, если API/объект его стабильно даёт; fallback: `Label + Direction`
- Замечание: для `GADrawing` важно использовать именно стабильный идентификатор модельной оси. Если `GUID` окажется view-specific для drawing-объекта, оставлять сопоставление по `Label + Direction`
- Реализация: отдельный класс/метод, вызывается из `fit_views_to_sheet` после расстановки

### Марки
- Obstacle-aware score (размеры, тексты, рамки видов)
- COG как якорь: `part.GetReportProperty("COG_X/Y/Z")` + трансформация в локальную СК вида
- Использовать `MarkGeometryHelper` внутри resolver/arrange, чтобы debug и layout считали одну и ту же геометрию

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
- **Шаблонные объекты (штамп, таблицы) — частичный доступ через DrawingInternal:**
  `sheet.GetAllObjects()` не возвращает элементы `.tpl`-шаблона. Но через internal API доступно:
  - `LayoutTable`: `FileName`, `XOffset`, `YOffset`, `Scale`, `TableCorner`, `RefCorner`, `OverlapVithViews` — placement metadata для каждой таблицы
  - `TableLayout`: `GetCurrentTables()`, `GetMarginsAndSpaces()` — список таблиц и отступы layout
  - `LayoutManager`: `GetDrawingFrames()`, `GetDrawingSize()` — фреймы и размер чертежа
  - `LayoutAttributes`: скрытые поля `InternalLayout`, `InternalTableLayoutId`; `LoadAttributes()` загружает layout через `DRAWING_LOAD_LAYOUT_ATTRIBUTES`

  **Что НЕ найдено**: `GetTableRect()` / `GetTitleBlockRect()`. В `LayoutTable` и `dotGrLayTable_t` нет `Width`/`Height`/`BoundingBox`. `REPORT_TEMPLATE` в managed-слое бросает `NotImplementedException`.

  **Главное ограничение**: таблицы динамические — высота parts list зависит от количества строк модели, итоговая геометрия определяется только после генерации содержимого. Для фиксированных штампов (title block) статическое размещение можно приблизительно вычислить из `XOffset`/`YOffset`/`RefCorner`, но не высоту.

  `TableLayout` и `LayoutManager` требуют запуска внутри процесса Tekla — из TeklaBridge (Remoting) не работают. Решение — in-process plugin (см. ниже).

  Из `LayoutAttributes` через рефлексию доступны поля: `_tableLayoutId` (int, напр. 8562), `_layout` (string, имя файла layout, напр. `"MPD_multizone_wall"`), `_sheetSize`, `_DrawingType`. `LayoutTable.Select()` с любым ID возвращает `false` из внешнего процесса — данные из БД не загружаются.

  При реализации плагина проверять в таком порядке:
  1. `TableLayout.GetCurrentTables()` + `LayoutTable.Select()` — placement metadata по каждой таблице
  2. `LayoutManager.GetDrawingFrames()` — возвращает `List<Tuple<bool,double,double,double,double,int>>`, по декомпиляции `UnmarshalFrames()` ожидает ровно 2 entry; скорее frame metadata листа, не прямоугольники таблиц — **гипотеза, не доказано**
  3. Сравнение на реальном drawing с таблицами и штампом даст факт

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
