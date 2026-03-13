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
| `create_single_part_drawing` / `create_assembly_drawing` | Создать чертёж через Open API |
| `get_drawing_context` / `select_drawing_objects` / `filter_drawing_objects` | Контекст и выделение |
| `get_drawing_views` | Виды + размеры листа (sheetWidth, sheetHeight) |
| `move_view` / `set_view_scale` / `fit_views_to_sheet` | Управление видами |
| `get_drawing_marks` / `create_part_marks` / `set_mark_content` / `delete_all_marks` | Марки |
| `resolve_mark_overlaps` / `arrange_marks` | Расстановка марок |
| `get_drawing_dimensions` / `create_dimension` / `move_dimension` / `delete_dimension` | Размеры |
| `get_part_geometry_in_view` / `get_all_parts_geometry_in_view` | Геометрия деталей в виде |
| `get_drawing_parts` / `get_grid_axes` | Объекты и сетка |

**Архитектура**
- Персистентный TeklaBridge (`--loop`): существенно снижает latency повторных вызовов, bridge живёт всю сессию
- TS2021 + TS2025 поддержка

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
- **Шаблонные объекты (штамп, таблицы) недоступны через Open API** — `sheet.GetAllObjects()` возвращает только content-объекты (детали, марки, размеры); элементы `.tpl`-шаблона (title block, parts list и т.п.) в объектной модели не присутствуют. `DrawingInternal.TableLayout` и `LayoutTable` требуют запуска внутри процесса Tekla (плагин) — из TeklaBridge (отдельный процесс через Remoting) не работают. Следствие: `fit_views_to_sheet` не знает где штамп и может разместить вид поверх него. Решение — in-process plugin (см. ниже).

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
