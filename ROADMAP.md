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
| `export_drawings_pdf` | Экспорт в PDF |
| `create_single_part_drawing` / `create_assembly_drawing` | Создать чертёж через Open API |
| `get_drawing_context` / `select_drawing_objects` / `filter_drawing_objects` | Контекст и выделение |
| `get_drawing_views` | Виды + размеры листа (sheetWidth, sheetHeight) |
| `move_view` / `set_view_scale` / `fit_views_to_sheet` | Управление видами |
| `get_drawing_marks` / `create_part_marks` / `set_mark_content` / `delete_all_marks` | Марки, их bbox/content, arrowhead и leader line данные |
| `resolve_mark_overlaps` / `arrange_marks` | Расстановка марок |
| `get_drawing_dimensions` / `create_dimension` / `move_dimension` / `delete_dimension` | Размеры |
| `get_part_geometry_in_view` / `get_all_parts_geometry_in_view` | Геометрия деталей в виде |
| `get_drawing_parts` / `get_grid_axes` | Объекты и сетка |
| `draw_debug_overlay` / `clear_debug_overlay` | Dev-only overlay слой: линии, прямоугольники, полилинии, полигоны, текст |

**Архитектура**
- Персистентный TeklaBridge (`--loop`): существенно снижает latency повторных вызовов, bridge живёт всю сессию
- TS2021 + TS2025 поддержка

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

### Марки
- Obstacle-aware score (размеры, тексты, рамки видов)
- COG как якорь: `part.GetReportProperty("COG_X/Y/Z")` + трансформация в локальную СК вида
- Для `BaseLinePlacing` перевести collision geometry с `StartPoint/EndPoint` на более надежную модель, используя debug overlay и фактическую визуальную ориентацию текста

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
