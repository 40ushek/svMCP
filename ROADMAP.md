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
| `open_drawing` | Открыть (активировать) чертёж по GUID |
| `close_drawing` | Закрыть активный чертёж |
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
| `get_grid_axes` | Получить оси сетки в заданном виде чертежа |
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
- **Вариант B (одна грань, левая) ✅ предпочтительный:** 8 точек `0, 400, 800, ..., 2400, 2500` → `400×6 + 100` — одна риска на ребро + замыкание; монтажник наметил карандашом линию = поставил ребро
- **Вариант A (обе грани):** 14 точек, 13 сегментов `100, 300, 100, 300, ...` — симметрично, показывает ширину ребра и просвет; детально, но сложнее читать на монтаже
- Выбор варианта зависит от контекста: B — для монтажных схем, A — для КМД/деталировки
- `delete_dimension` + `create_dimension` — рабочий паттерн для пересоздания цепочек
- **Общий (габаритный) размер ставить всегда** — даже если цепочка полная. Складывать сегменты в уме долго и ошибочно. Общий размер = быстрый однозначный ответ на вопрос "сколько всего".

### Cumulative (нарастающий) тип размера
- В Tekla есть тип "Straight" с нарастающим итогом: показывает и каждый сегмент, и суммарное расстояние от начала
- Удобно для монтажа рулеткой: лента не двигается, просто отмечаешь 400, 800, 1200... и не считаешь в уме
- **Реализация уже возможна**: сохранить стиль в Tekla UI (Dimension Properties → Straight type → cumulative → Save as `"cumulative"`), затем `create_dimension(..., attributesFile="cumulative")`
- Альтернатива: установить программно через `StraightDimensionSetAttributes.DimensionType` — нужно проверить enum значения через reflection

### К реализации
- `add_dimension_point` — добавить точку в существующую цепочку. Публичный API (`StraightDimensionSet`) не имеет свойства `Points`. В UI такая возможность есть → искать в `Tekla.Structures.DrawingInternal` или внутренних сборках. Временный workaround: `delete_dimension` + `create_dimension` с новым набором точек.
- Bbox текста размера как препятствие для марок: `StraightDimension.GetObjectAlignedBoundingBox()` → `CanMove=false` в `MarkOverlapResolver`
- `get_part_openings(modelId, viewId)` — проёмы (двери/окна) в стенах: итерировать `part.GetBooleans()`, возвращать bbox каждого выреза в СК вида. Нужен для цепочек вида: ось → до проёма → ширина проёма → после проёма → ось

### Идея: обучение на эталонных чертежах (долгосрочно)
- Иметь набор готовых правильно размеренных чертежей как примеров
- AI читает их через `get_drawing_dimensions` + `get_part_geometry_in_view`, извлекает паттерн:
  какой тип детали → какие размеры → какая стратегия расстановки
- Применяет ту же логику к новым чертежам аналогичных деталей
- Правила могут меняться под проект — не хардкодить, а читать из примеров
- **Статус:** идея; сначала нужны базовые инструменты для чтения/создания размеров ✅

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

## Поддержка версий Tekla

| Версия | Статус | IPC | Особенности |
|---|---|---|---|
| **TS2021** | ✅ Работает | .NET Remoting, named pipes | Reflection-фикс `-:` → `-Console:` до `new Model()` |
| **TS2025** | ✅ Работает | Trimble.Remoting, MMF | TeklaBridge в extensions-папке + `exe.config` с `<codeBase>` |

### Деплой TeklaBridge для TS2025

Для TS2025 TeklaBridge.exe должен запускаться из папки расширений Tekla:
`C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`

Это необходимо из-за несоответствия версий: NuGet пакет `2025.0.0.0` vs. установленная FileVersion `2025.0.52577.0`.
Канал MMF содержит FileVersion — поэтому NuGet DLL не может найти канал, а установленная DLL — может.

**Требуется:**
1. `TeklaBridge.exe` + `TeklaMcpServer.Api.dll` + не-Tekla зависимости → скопировать в extensions-папку
2. `TeklaBridge.exe.config` с `<codeBase>` записями → хранится в extensions-папке вручную
3. Генератор конфига: `C:\temp\GenConfig2.ps1`

TeklaMcpServer автоматически выбирает bridge из extensions-папки если файл существует
(`ResolveBridgePath()` в `src/TeklaMcpServer/Tools/Shared/ModelTools.Shared.cs`).

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

## Персистентный TeklaBridge — критически важная оптимизация производительности

### Проблема: каждый MCP-вызов = запуск нового процесса (~2-3 секунды)

Сейчас каждый вызов любого MCP-инструмента проходит через такую цепочку:

```
Claude вызывает MCP tool
    ↓
TeklaMcpServer: Process.Start("TeklaBridge.exe", command)
    ↓  ~200 мс  .NET Framework 4.8 CLR init
    ↓  ~500 мс  загрузка Tekla DLL (~20 сборок)
    ↓  ~300 мс  reflection-сканирование IPC-каналов (фикс -: → -Console:)
    ↓  ~500 мс  new Model() — подключение к Tekla по IPC
    ↓  ~200 мс  new DrawingHandler() — подключение к Drawing IPC
    ↓  ~100 мс  сама команда (get_part_geometry_in_view, create_dimension, etc.)
    ↓  процесс завершается
= 1.8 – 3 секунды на КАЖДЫЙ вызов
```

**Последствия:**

| Сценарий | Кол-во вызовов | Время |
|---|---|---|
| Поставить 8 размеров на чертеже W-14 | ~15 вызовов | ~35 секунд |
| Полный цикл авторазмерования (geometry + dimensions + marks) | ~30 вызовов | ~75 секунд |
| Человек с рулеткой и карандашом | — | ~5 минут |
| **Цель** | — | **< 30 секунд** |

При таком overhead'е инструмент не может конкурировать с ручной работой. Это фундаментальная архитектурная проблема, а не медленный алгоритм.

---

### Решение: TeklaBridge как постоянный процесс

TeklaBridge запускается **один раз** при старте TeklaMcpServer и живёт всё время его работы. Команды передаются через stdin/stdout JSON-протокол.

```
Claude вызывает MCP tool
    ↓
TeklaMcpServer: пишет JSON в stdin уже запущенного TeklaBridge
    ↓  ~5 мс   парсинг команды
    ↓  ~10 мс  сама команда (model already connected)
    ↓  ~5 мс   запись JSON в stdout
    ↓
TeklaMcpServer: читает ответ
= 20 – 50 мс на вызов (100× быстрее)
```

**Startup платится один раз** — при первом вызове любого инструмента (или при старте MCP-сервера), а не при каждом.

---

### Протокол stdin/stdout

Каждое сообщение — одна строка JSON (newline-delimited).

**Request** (TeklaMcpServer → TeklaBridge stdin):
```json
{"id": 1, "cmd": "get_all_parts_geometry_in_view", "args": ["1129"]}
```

**Response** (TeklaBridge stdout → TeklaMcpServer):
```json
{"id": 1, "ok": true, "result": "{...json string...}"}
```

или при ошибке:
```json
{"id": 1, "ok": false, "error": "View 1129 not found"}
```

Поле `id` нужно для сопоставления запрос/ответ — позволяет в будущем делать параллельные вызовы.

---

### Изменения в коде

**TeklaBridge/Program.cs** — вместо single-shot: цикл чтения stdin:

```csharp
// Текущий код (one-shot):
var handler = new DrawingCommandHandler(realOut);
handler.Handle(args[0], args);

// Новый код (persistent loop):
var handler = new DrawingCommandHandler(realOut);
string? line;
while ((line = Console.In.ReadLine()) != null)
{
    var req = JsonSerializer.Deserialize<BridgeRequest>(line);
    try {
        var result = handler.HandleJson(req.Cmd, req.Args);
        realOut.WriteLine(JsonSerializer.Serialize(new { id = req.Id, ok = true, result }));
    }
    catch (Exception ex) {
        realOut.WriteLine(JsonSerializer.Serialize(new { id = req.Id, ok = false, error = ex.Message }));
    }
}
```

**TeklaMcpServer/Tools/Shared/ModelTools.Shared.cs** — вместо `Process.Start()`:

```csharp
// Текущий код:
static string RunBridge(string command, params string[] args) {
    var proc = Process.Start(new ProcessStartInfo {
        FileName = BridgePath,
        Arguments = string.Join(" ", new[] { command }.Concat(args)),
        ...
    });
    return proc.StandardOutput.ReadToEnd();
}

// Новый код:
static string RunBridge(string command, params string[] args) {
    return PersistentBridge.Instance.Send(command, args);
}
```

**Новый файл TeklaMcpServer/PersistentBridge.cs**:

```csharp
sealed class PersistentBridge : IDisposable
{
    public static readonly PersistentBridge Instance = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _nextId = 0;
    private readonly SemaphoreSlim _lock = new(1, 1); // один запрос за раз

    public string Send(string cmd, string[] args)
    {
        _lock.Wait();
        try {
            EnsureStarted();
            var id = Interlocked.Increment(ref _nextId);
            var req = JsonSerializer.Serialize(new { id, cmd, args });
            _stdin!.WriteLine(req);
            _stdin.Flush();

            var response = _stdout!.ReadLine()
                ?? throw new Exception("TeklaBridge process died");

            var resp = JsonSerializer.Deserialize<BridgeResponse>(response)!;
            if (!resp.Ok) throw new Exception(resp.Error);
            return resp.Result ?? "";
        }
        catch {
            KillProcess(); // при ошибке — убить, следующий вызов перезапустит
            throw;
        }
        finally {
            _lock.Release();
        }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false }) return;
        // запустить TeklaBridge в persistent-режиме (без аргументов = loop mode)
        _process = Process.Start(new ProcessStartInfo {
            FileName = BridgePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        _stdin  = _process.StandardInput;
        _stdout = new StreamReader(_process.StandardOutput.BaseStream);
    }
}
```

---

### Что НЕ меняется

- Все MCP-инструменты (`[McpServerTool]` методы) — без изменений
- Все bridge-команды и их JSON-форматы — без изменений
- Вся логика в `TeklaMcpServer.Api` — без изменений
- `DrawingCommandHandler`, `ModelCommandHandler` — без изменений (только добавляется `HandleJson` обёртка)

**Внешнее поведение идентично. Меняется только транспортный слой.**

---

### Риски и mitigation

| Риск | Mitigation |
|---|---|
| TeklaBridge зависает (Tekla disconnected) | Таймаут 30 сек на чтение stdout → KillProcess() → авторестарт |
| Tekla выбрасывает exception в команде | Catch в loop → отправить `{ok:false, error}` → процесс живёт дальше |
| Tekla пишет в Console.Out (diagnostic noise) | Уже решено: `Console.SetOut(teklaLog)` до loop, realOut отдельно |
| IPC-канал Tekla умирает | Перезапуск TeklaBridge — reconnection не поддерживается Tekla API |
| Параллельные вызовы из Claude | `SemaphoreSlim(1)` — сериализация; в будущем можно pool из N процессов |

---

### Порядок реализации

```
1. TeklaBridge: добавить loop-режим (args пустые = stdin loop)
   Обратная совместимость: если args не пустые — старый one-shot режим
   Файл: TeklaBridge/Program.cs
   Объём: ~30 строк

2. PersistentBridge: singleton с EnsureStarted + Send + авторестарт
   Файл: TeklaMcpServer/PersistentBridge.cs (новый)
   Объём: ~80 строк

3. RunBridge(): заменить Process.Start на PersistentBridge.Instance.Send
   Файл: TeklaMcpServer/Tools/Shared/ModelTools.Shared.cs
   Объём: ~10 строк изменений

4. Тест: get_drawing_views → открыть чертёж → 10 последовательных вызовов
   Ожидаемое время: < 500 мс суммарно (vs ~25 секунд сейчас)
```

**Оценка объёма:** ~120 строк кода, ~0.5 дня работы.
**Приоритет: ВЫСОКИЙ** — без этого инструмент непригоден для реального использования.

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

## Роадмап рефакторинга архитектуры (2026-03-08)

Цель: снизить архитектурный долг без изменения внешнего поведения MCP-инструментов.

### Инварианты (не меняем)

- Имена MCP tools.
- Имена bridge-команд.
- JSON-форматы успешных ответов и ошибок.
- Текущую семантику выполнения команд.

### Фаза 1 — Контракт и baseline (0.5–1 день)

- Зафиксировать контракт MCP ↔ bridge в документации (`commands`, `args`, `response shape`).
- Вынести список несовместимых изменений (что нельзя менять в рефакторинге).

Результат: `docs/CONTRACT.md` и проверяемый список контрактов.

### Фаза 2 — Регрессионная защита (1–2 дня)

- Добавить contract/snapshot-тесты для ключевых bridge-команд.
- Проверять валидность JSON и ключевые поля в ответах.

Результат: тесты, которые блокируют незаметные breaking changes.

### Фаза 3 — Декомпозиция bridge-слоя (2–3 дня)

- Разбить `DrawingCommandHandler` на доменные обработчики:
  - Drawings
  - Views
  - Dimensions
  - Marks
  - Geometry
  - Grid
- Только extraction/move без логических изменений.

Результат: меньше монолита, проще поддержка и ревью.

### Фаза 4 — Вынос Tekla-логики в `TeklaMcpServer.Api` (3–5 дней)

- Переместить из bridge прямые вызовы Tekla API в `TeklaMcpServer.Api`.
- Оставить в bridge только parse args → вызов API → serialize result.

Результат: bridge становится тонким диспетчером по фактической архитектуре.

### Фаза 5 — Укрепление межпроцессного контракта (2–3 дня)

- Ввести typed DTO для request/response между слоями (с сохранением текущих команд для совместимости).
- Добавить timeout/cancellation и более явную диагностику subprocess-вызовов.

Результат: меньше зависаний и runtime-ошибок в интеграции.

### Фаза 6 — Build и docs cleanup (0.5–1 день)

- Убрать дублирующие copy/build target-цепочки.
- Синхронизировать архитектурную документацию с фактической реализацией.

Результат: предсказуемая сборка и актуальная документация.

### Контрольные точки

1. После Фазы 2 — все контрактные тесты зеленые.
2. После Фазы 3 — структура изменена, контракт полностью совместим.
3. После Фазы 4 — в bridge нет бизнес-логики Tekla (кроме bootstrap/infra).
4. После Фазы 6 — `build + tests + docs` консистентны.
