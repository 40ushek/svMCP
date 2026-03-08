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
| `create_general_arrangement_drawing` | Legacy workaround: создать GA-чертёж через макрос; не развивать дальше |
| `create_single_part_drawing` | Создать Single Part drawing через Open API |
| `create_assembly_drawing` | Создать Assembly drawing через Open API |
| `get_drawing_context` | Активный чертёж + выделенные объекты |
| `select_drawing_objects` | Выделить объекты по model ID |
| `filter_drawing_objects` | Фильтр объектов по типу (Mark, Part, DimensionBase…) |
| `set_mark_content` | Изменить содержимое и шрифт марок |
| `get_drawing_views` | Виды активного чертежа: позиция, масштаб, размер, размеры листа |
| `move_view` | Переместить вид (абсолютно или на смещение) |
| `set_view_scale` | Изменить масштаб одного или нескольких видов |
| `fit_views_to_sheet` | Авторасстановка: подбор стандартного масштаба, ортографическая раскладка |
| `get_drawing_marks` | Марки: позиция, bbox текстового блока, список перекрытий, содержимое PropertyElement; фильтрация по виду |
| `get_drawing_parts` | Модельные объекты чертежа: PART_POS, ASSEMBLY_POS, PROFILE, MATERIAL, NAME |
| `get_drawing_dimensions` | `StraightDimensionSet`: id, distance, координаты сегментов |
| `move_dimension` | Сдвинуть размерную линию (delta к `StraightDimensionSet.Distance`) |
| `create_dimension` | Создать `StraightDimensionSet` по набору точек; поддерживает горизонтальные, вертикальные, диагональные и цепочки размеров |
| `delete_dimension` | Удалить `StraightDimensionSet` по ID |
| `get_part_geometry_in_view` | Геометрия детали (bbox, start/end, оси) в локальной СК вида; используется для точного размещения размеров |
| `resolve_mark_overlaps` | Разрешить перекрытия текстовых блоков марок внутри каждого вида — локальный overlap resolver |
| `arrange_marks` | Полная расстановка марок внутри каждого вида вокруг anchor point |
| `create_part_marks` | Создать марки детали с заданным содержимым и стилем |
| `delete_all_marks` | Удалить все марки на активном чертеже |

### Создание чертежей

- `create_single_part_drawing` и `create_assembly_drawing` — основной путь, развивать через Open API
- `create_general_arrangement_drawing` — legacy workaround через macro/UI automation
- новые возможности по созданию чертежей добавлять только через Open API, не через макросы

---

## Идеи по размерам (из сессии 2026-03-07)

### Реализовано
- `create_dimension` поддерживает горизонтальные, вертикальные, **диагональные** и **цепочки** размеров из коробки
- `get_part_geometry_in_view` даёт bbox в локальной СК вида — достаточно для любого типа размеров
- Диагональные размеры по противоположным углам bbox — простая проверка геометрии (деревянные/панельные конструкции)
- Цепочки: передать 3+ точек в `points` — один `StraightDimensionSet` с несколькими сегментами

### Стратегия простановки размеров (2026-03-08)
- Тики цепочки ставить на **края рёбер** (bboxMin.X / bboxMax.X), а не в центры — быстрее измерять рулеткой
- **Вариант A (обе грани):** 14 точек, 13 сегментов `100, 300, 100, 300, ...` — симметрично, показывает ширину ребра и просвет; применён в текущей реализации
- **Вариант B (одна грань, левая):** 8 точек `0, 400, 800, ..., 2400, 2500` → `400×6 + 100` — одна риска на ребро, проще для монтажника: наметил карандашом линию = поставил ребро; менее симметрично
- Выбор варианта зависит от контекста: A — для КМД/деталировки, B — для монтажных схем
- `delete_dimension` + `create_dimension` — рабочий паттерн для пересоздания цепочек

### К реализации
- `add_dimension_point` — добавить точку в существующую цепочку. Публичный API (`StraightDimensionSet`) не имеет свойства `Points`. В UI такая возможность есть → искать в `Tekla.Structures.DrawingInternal` или внутренних сборках. Временный workaround: `delete_dimension` + `create_dimension` с новым набором точек.
- Bbox текста размера как препятствие для марок: `StraightDimension.GetObjectAlignedBoundingBox()` → `CanMove=false` в `MarkOverlapResolver`
- `get_part_openings(modelId, viewId)` — проёмы (двери/окна) в стенах: итерировать `part.GetBooleans()`, возвращать bbox каждого выреза в СК вида. Нужен для цепочек вида: ось → до проёма → ширина проёма → после проёма → ось

### Идеи из примеров (cs/ и dim/)
- `GetProjectedShape.GetShape(partId, coordinateSystem)` — реальный контур из `FaceEnumerator`, не bbox; нужен для размеров по граням сложных профилей
- `ReturnDimensionValue` — форматированная строка значения; не нужна, значение = евклидово расстояние между точками сегмента

---

## Идеи из внешних MCP/Tekla репозиториев (к внедрению)

Ниже зафиксированы практические идеи из публичных репозиториев, которые стоит перенести в `svMCP`.

### Источники

- `teknovizier/tekla_mcp_server` — runtime-инструменты, тестовая стратегия, alias/semantic mapping:
  <https://github.com/teknovizier/tekla_mcp_server>
- `pawellisowski/tekla-api-mcp` — отдельный docs-MCP и локальный индекс API:
  <https://github.com/pawellisowski/tekla-api-mcp>
- `HuVelasco/structural-mcp-servers` — safety-подход и модульная структура операций:
  <https://github.com/HuVelasco/structural-mcp-servers>

### Приоритет P1 (высокий)

- Разделить серверы на `runtime MCP` и `docs MCP`.
  Комментарий: runtime-инструменты и поиск по документации решают разные задачи; разделение уменьшит связность и упростит поддержку.

- Добавить локальный индекс Tekla Open API и code examples для docs-сервера.
  Комментарий: уменьшает зависимость от интернета и снижает риск "галлюцинаций" по API.

- Ввести тестовую матрицу `unit + functional`.
  Комментарий: unit-тесты без Tekla, functional-тесты только при доступной Tekla (иначе skip).

### Приоритет P2 (средний)

- Добавить `check_tekla_connection` и расширенный `get_context` как обязательные health-инструменты.
  Комментарий: все потенциально изменяющие команды должны опираться на пред-проверку состояния.

- Ввести `safe mode` для массовых/опасных операций (`delete`, bulk modify, bulk arrange).
  Комментарий: включить флаг подтверждения и ограничители по объему изменений.

- Оформить слой `operations` (пайплайны вида `select -> filter -> apply`) поверх низкоуровневых tools.
  Комментарий: позволит предсказуемо собирать сложные сценарии и легче тестировать оркестрацию.

- Добавить словарь alias-атрибутов (rule-based) для фильтрации/поиска.
  Комментарий: сначала простой mapping без embeddings/ML, затем при необходимости расширять.

- Сделать `one-command setup` для локального запуска/проверки окружения.
  Комментарий: уменьшает время на подключение новых пользователей и диагностику инсталляции.

### Ограничения и правила заимствования

- Не копировать код напрямую из GPLv3-проектов в `svMCP` без отдельной лицензонной проверки.
  Комментарий: в первую очередь это относится к `teknovizier/tekla_mcp_server` (GPLv3).

- Заимствовать архитектурные идеи, протоколы и тестовые практики; реализацию держать собственной.

---

## Структура TeklaMcpServer.Api (целевая)

```
TeklaMcpServer.Api/
├── Connection/
├── Selection/
├── Filtering/
├── Drawing/
│   ├── Marks/        # IDrawingMarkApi, TeklaDrawingMarkApi, DTOs ✅
│   ├── Views/        # IDrawingViewApi, TeklaDrawingViewApi, DTOs ✅
│   └── Dimensions/   # IDimensionApi, TeklaDimensionApi, DTOs — следующий шаг
└── Model/
```

Каждая подпапка: интерфейс + реализация + DTOs. `DrawingCommandHandler` остаётся тонким диспетчером.

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

### `resolve_mark_overlaps` ✅ — минимальные сдвиги

Использует `MarkOverlapResolver` напрямую: итеративно толкает перекрывающиеся пары ровно на величину перекрытия + gap. Марки сдвигаются минимально, якорь не учитывается.

**Архитектурное правило:** обработка идет отдельно для каждого `View`. Марки из других видов в расчет не попадают.

**Система координат:** все вычисления делаются в локальной СК вида. В sheet coordinates выполняется только чтение из Tekla и применение `InsertionPoint`.

**Когда использовать:** марки уже расставлены вручную или Tekla, нужно только разлепить перекрывающиеся.

---

### `arrange_marks` ✅ — полная расстановка по видам

Использует `MarkLayoutEngine` для размещения марок внутри каждого `View` с нуля: учитывает якорь, длину выноски, плотность и предпочтительный квадрант.

**Когда использовать:** чертёж новый или метки в хаосе, нужна полная расстановка.

**Алгоритмический слой уже готов:**
- `MarkLayoutEngine` — greedy placement + overlap resolver
- `SimpleMarkCandidateGenerator` — кандидаты в 3 кольца вокруг якоря (8 направлений × 3 мультипликатора), упорядоченные по квадрантному сходству с текущей позицией
- `SimpleMarkCostEvaluator` — штраф за overlap, crowding, длину выноски, удаление от текущей позиции
- `MarkOverlapResolver` — post-processing push-apart
- `MarkLayoutOptions` — `Gap`, `CandidateDistanceMultipliers`, веса score

**Текущее правило:** один `View` = один набор меток = один layout pass. Другие виды полностью игнорируются.

**Текущая реализация adapter layer:**
1. Для каждого `View` отдельно собираются `MarkLayoutItem`
2. В `MarkLayoutItem` попадают локальные координаты вида, а не координаты листа
3. Для меток с leader line якорь берется из `LeaderLinePlacing.StartPoint` в СК вида
4. После расчета результат применяется обратно в `InsertionPoint` в той же локальной СК вида

**Ближайшие улучшения:**
- уточнить owner-view для `get_drawing_marks`, чтобы диагностический `viewId` был так же строгим, как в auto-layout
- obstacle-aware score (размеры, тексты, рамки видов)
- настройка весов через `MarkLayoutOptions`

**Более поздние улучшения:**
- **COG как якорь**: `part.GetReportProperty("COG_X/Y/Z")` + трансформация в локальную СК вида
- **Sliding anchor**: для длинных деталей стрелка скользит вдоль bbox
- **Размеры как препятствия**: bbox текста `StraightDimension.GetObjectAlignedBoundingBox()` — передавать в `MarkOverlapResolver` как `CanMove=false` объекты

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

## Деплой и UX для конечного пользователя

### Текущая схема (stdio)

TeklaMcpServer.exe запускается Claude Code как дочерний процесс через stdio. Работает для разработки, но неудобно для конечного пользователя — процесс невидим, нельзя пересобрать exe без перезапуска клиента.

### Целевая схема (трей + SSE/HTTP)

Для готового продукта на Win 10/11:

- **TeklaMcpServer** переключается на SSE/HTTP транспорт (`WithSseServerTransport()`) и запускается как Windows tray-приложение (NotifyIcon)
- Пользователь видит значок в трее: зелёный = подключён к Tekla, красный = нет
- Правой кнопкой → контекстное меню: "Перезапустить", "Выйти"
- Конфиг Claude Code меняется с `"command": "TeklaMcpServer.exe"` на `"url": "http://localhost:5555/sse"`
- Опционально: автозапуск с Windows

**Преимущества:**
- Пересборка без перезапуска Claude Code/VSCode
- Пользователь видит что сервер работает
- Легко установить/удалить

**Windows 11 ODR (будущее):** Microsoft встраивает реестр MCP-серверов (On-Device Agent Registry) в Windows 11. Поддерживает stdio/SSE/HTTP, изоляцию, MSIX-упаковку. На Win 10 недоступен. Когда ODR станет стандартом — возможна упаковка в MSIX с авто-регистрацией при установке.

**Объём работ:** ~2-3 дня. Делать когда основной функционал стабилизируется.

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
