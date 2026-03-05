# svMCP — Drawing Automation Roadmap

Цель: автоматическое приведение чертежей Tekla в читаемый вид —
разрешение конфликтов видов и аннотаций, подбор масштаба, сдвиг объектов.

## Границы работ

- Активная разработка ведется только в `src/` (решение `src/svMCP.sln`).
- Папки `Assistant/` и `WebBridge/` используются только как справочные.
- `Assistant/` и `WebBridge/` не редактируем, если нет отдельного явного запроса.

## Статус реализации

### Готово ✅

**Соединение и модель**

| Инструмент | Описание |
|---|---|
| `check_connection` | Соединение с Tekla |
| `get_selected_elements_properties` | Свойства выделенных элементов: Part, BoltGroup, Weld, RebarGroup — все типы |
| `get_selected_elements_total_weight` | Суммарный вес выделенных элементов (кг) |
| `select_elements_by_class` | Выделить элементы по номеру класса Tekla |
| `filter_model_objects_by_type` | Найти и выделить объекты по типу (beam, plate, bolt, assembly…) или по Tekla filter expression |

**Чертежи**

| Инструмент | Описание |
|---|---|
| `list_drawings` | Список всех чертежей |
| `find_drawings` | Поиск по имени / марке |
| `find_drawings_by_properties` | Поиск по фильтрам (name, mark, type, status) |
| `export_drawings_to_pdf` | Экспорт в PDF по GUID |
| `create_general_arrangement_drawing` | Создать GA-чертёж через макрос |
| `get_drawing_context` | Активный чертёж + выделенные объекты |
| `select_drawing_objects` | Выделить объекты по model ID |
| `filter_drawing_objects` | Фильтр объектов по типу (Mark, Part, DimensionBase…) |
| `set_mark_content` | Изменить содержимое и шрифт марок |
| `get_drawing_views` | Виды активного чертежа: позиция, масштаб, размер, размеры листа |
| `move_view` | Переместить вид (абсолютно или на смещение) |
| `set_view_scale` | Изменить масштаб одного или нескольких видов |
| `fit_views_to_sheet` | Авторасстановка: подбор стандартного масштаба, ортографическая раскладка |
| `get_drawing_marks` | Содержимое марок — имена и значения PropertyElement; фильтрация по виду |
| `get_drawing_parts` | Модельные объекты чертежа: PART_POS, ASSEMBLY_POS, PROFILE, MATERIAL, NAME |
| `get_drawing_dimensions` | `StraightDimensionSet`: id, distance, координаты сегментов |
| `move_dimension` | Сдвинуть размерную линию (delta к `StraightDimensionSet.Distance`) |

---

## Фаза 1 — Чтение геометрии чертежа

> **`get_drawing_views` реализован ✅.** Возвращает `id`, `viewType`, `name`, `originX/Y`, `scale`, `width`, `height`, `sheetWidth`, `sheetHeight`.

### `get_drawing_objects_layout`
Получить bounding box всех аннотационных объектов (марки, размеры, тексты) в координатах листа.

**Возвращает:**
```json
[
  {
    "id": 456,
    "type": "Mark",
    "origin": { "x": 110.5, "y": 200.3 },
    "boundingBox": {
      "min": { "x": 108.0, "y": 198.0 },
      "max": { "x": 125.0, "y": 207.0 }
    },
    "viewId": 123
  }
]
```

**Сложность:** Tekla API возвращает `Origin` для большинства объектов, но `BoundingBox` — нужно проверить наличие через reflection или вычислять по размеру шрифта / количеству строк.

---

## Фаза 2 — Обнаружение конфликтов

### `detect_view_conflicts`
Найти виды, которые перекрываются на листе (пересечение bounding box-ов).

**Возвращает:** список пар `[viewId1, viewId2]` с координатами пересечения.

**Алгоритм:** AABB intersection для всех пар видов. O(n²), но видов обычно < 20.

---

### `detect_annotation_conflicts`
Найти марки / тексты / размеры, которые:
- перекрываются между собой
- перекрываются с рамкой вида
- выходят за пределы листа

**Возвращает:** список объектов с флагом типа конфликта.

---

## Фаза 3 — Редактирование видов

> **`move_view`, `set_view_scale`, `fit_views_to_sheet` реализованы ✅.**

`fit_views_to_sheet` — двухпроходной алгоритм: применяет масштаб + `CommitChanges`, перечитывает реальные размеры видов, затем расставляет:
- **SinglePartDrawing**: ортографическая раскладка — FrontView в центре, TopView выше, BottomView ниже, BackView слева, SectionView справа, 3D-вид в свободный угол
- **GA и прочие**: shelf-packing (полочная укладка по убыванию высоты)

Стандартные масштабы: 1, 2, 5, 10, 15, 20, 25, 50, 100, 200, 250, 500, 1000.

---

## Фаза 4 — Редактирование аннотаций

### `move_mark`
Сдвинуть марку.

**Параметры:** `markId`, `dx`, `dy` или `newOrigin`.

**Tekla API:** `mark.Origin = new Point(...); mark.Modify();`

---

### `auto_resolve_annotation_conflicts`
Автоматически разрешить конфликты марок.

**Алгоритм:**
1. Обнаружить конфликтующие пары через bounding box
2. Для каждой пары — сдвинуть одну марку по 8 стандартным направлениям (N, NE, E, SE…)
3. Выбрать направление с минимальным суммарным перемещением без новых конфликтов
4. Итерировать до разрешения или до `maxIterations`

**Ограничение:** Tekla не гарантирует что `BoundingBox` марки доступен — может потребоваться эвристика по тексту.

---

## Фаза 5 — Комплексная команда

### `tidy_drawing`
Одна команда для приведения активного чертежа в порядок.

**Режимы:**
- `analyze` — только отчёт о конфликтах, ничего не меняет
- `preview` — возвращает план изменений без применения
- `apply` — применяет изменения

**Параметры:**
```
mode: "analyze" | "preview" | "apply"
fixViews: bool       // двигать виды
fixAnnotations: bool // двигать марки
margin: float        // отступ между объектами (мм)
```

---

## Технические риски

| Риск | Вероятность | Обходной путь |
|---|---|---|
| `BoundingBox` объектов недоступен через API | Средняя | Вычислять из `Origin` + размер шрифта × длина текста |
| Виды с фиксированным положением (locked) | Низкая | Проверять флаг и пропускать |
| Марки с лидером меняют позицию непредсказуемо | Высокая | Двигать только марки без лидера или с коротким лидером |
| Макросы для GA не работают в Drawing mode | Низкая | Уже решено — макросы в `modeling/` |

---

## Порядок реализации

```
get_drawing_views
    ↓
get_drawing_objects_layout
    ↓
detect_view_conflicts + detect_annotation_conflicts
    ↓
move_view + set_view_scale
    ↓
move_mark
    ↓
auto_fit_views + auto_resolve_annotation_conflicts
    ↓
tidy_drawing
```

Каждый шаг тестируется отдельно перед переходом к следующему.

---

## Известные ограничения

### Объекты внутри компонентов

`GetAllObjects()` возвращает только объекты верхнего уровня. Части внутри компонентов (соединений) недоступны. Для обхода нужно для каждого `Component` вызывать `component.GetChildren()` рекурсивно.

### `BoltGrade` всегда null

Класс болта не доступен через стандартные свойства `BoltGroup`. Нужен `GetReportProperty("BOLT_GRADE")`.

### Оператор `is` не работает для IPC-прокси

Объекты, полученные через `GetAllObjects()` / `GetObjectsByFilter()`, — transparent proxies .NET Remoting. `is Beam` всегда `false`. Нужно использовать `GetType().Name`. Это касается любого нового кода с `GetAllObjects()`.

---

## Технический долг

### Рефакторинг 2026-03-05: Drawing View API перенесён в TeklaMcpServer.Api ✅

Вся логика видов (`get_drawing_views`, `move_view`, `set_view_scale`, `fit_views_to_sheet`) и марок (`get_drawing_marks`) вынесена из `DrawingCommandHandler` в `TeklaMcpServer.Api/Drawing/`:
- `IDrawingViewApi` / `TeklaDrawingViewApi` — операции с видами
- `IDrawingMarkApi` / `TeklaDrawingMarkApi` — чтение марок
- DTOs: `DrawingViewInfo`, `DrawingViewsResult`, `MoveViewResult`, `SetViewScaleResult`, `FitViewsResult`, `ArrangedView`, `DrawingMarkInfo`, `MarkPropertyValue`, `GetMarksResult`

`DrawingCommandHandler` теперь тонкий диспетчер (~15 строк на команду).

**Известный нюанс:** `View.GetIdentifier()` — extension-метод из `Tekla.Structures.DrawingInternal`, требует явного `using Tekla.Structures.DrawingInternal;` в файлах `TeklaMcpServer.Api`.

### Ревью изменений от 2026-03-04 (фильтрация/bridge)

1. **Medium**: очистка stdout до JSON через последнюю строку с `{`/`[` — хрупко если TeklaBridge когда-либо выведет многострочный JSON.
   - Файл: `src/TeklaMcpServer/Tools/Shared/ModelTools.Shared.cs` (`RunBridge`).

### Selection cache — персистентность между вызовами

**Проблема:** TeklaBridge запускается как новый процесс на каждую команду. `selectionId`, созданный в одном вызове, недоступен в следующем — `SelectionCacheManager` in-memory не переживает завершение процесса.

**Инфраструктура уже написана** (`TeklaMcpServer.Api/Selection/`):
- `ISelectionCacheManager` / `SelectionCacheManager` — кэш с TTL 30 мин
- `SelectionResult` — DTO с пагинацией (`selectionId`, `hasMore`, cursor base64)
- `ToolInputSelectionHandler` — резолвер источника ID (cachedSelectionId / useCurrentSelection / elementIds), работает для модели и чертежей

**Не подключено к командам** — нужно переработать Bridge handlers для использования этой инфраструктуры.

**Варианты реализации (по сложности):**

| Вариант | Сложность | Суть |
|---|---|---|
| Файловый кэш | Низкая | `SelectionCacheManager` читает/пишет `C:\temp\svmcp_selections.json` |
| Кэш в TeklaMcpServer | Средняя | Состояние хранится на стороне net8, IDs передаются в каждый вызов bridge |
| TeklaBridge как long-lived процесс | Высокая | MCP server держит bridge открытым, общается в цикле stdin/stdout |

**Рекомендуется начать с файлового кэша** — наименьший объём изменений, совместим с текущей архитектурой.
