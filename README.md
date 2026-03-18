# svMCP — Tekla Structures MCP Server

MCP (Model Context Protocol) сервер для работы с **Tekla Structures 2021 и 2025** через Claude Desktop и другие MCP-клиенты.

## Требования

- Windows 10/11
- .NET 8 SDK
- .NET Framework 4.8 (входит в Windows 10+)
- Tekla Structures **2021** или **2025** (установленная и запущенная)

## Установка и настройка

### 1. Сборка

```bash
dotnet build src/TeklaMcpServer/TeklaMcpServer.csproj -c Release
```

Автоматически собирает `TeklaMcpServer` (net8.0) + `TeklaBridge` (net48) и копирует TeklaBridge.exe в `bin/Release/net8.0-windows/bridge/`.

### 2. Настройка Claude Desktop

`%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "tekla": {
      "command": "D:\\repos\\svMCP\\src\\TeklaMcpServer\\bin\\Release\\net8.0-windows\\TeklaMcpServer.exe"
    }
  }
}
```

### 3. Деплой после изменений

```bash
# 1. Закрыть Claude Desktop
# 2. Собрать
dotnet build src/TeklaMcpServer/TeklaMcpServer.csproj -c Release
# 3. Открыть Claude Desktop
```

> **Важно:** TeklaMcpServer.exe заблокирован пока Claude Desktop открыт.
> Если менялся только TeklaBridge — его можно пересобрать без закрытия Claude Desktop,
> результат копируется в `bridge/` автоматически через цель `BuildAndCopyTeklaBridge`.

### Дополнительно для Tekla Structures 2025

TeklaBridge должен запускаться из папки расширений Tekla, а не из стандартной `bridge/` директории.
Это необходимо, потому что Tekla генерирует `exe.config` с `<codeBase>` записями, которые
указывают на установленные DLL с правильными версиями (FileVersion `2025.0.52577.0`, а не NuGet `2025.0.0.0`).

Папка расширений: `C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`

**Деплой TeklaBridge для TS2025:**

```bash
# Сборка TeklaBridge (без закрытия Claude Desktop)
dotnet build src/TeklaBridge/TeklaBridge.csproj -c Release

# Скопировать TeklaBridge.exe и TeklaMcpServer.Api.dll в папку расширений
$src = "src\TeklaBridge\bin\Release\net48"
$dst = "C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP"
New-Item -ItemType Directory -Force $dst
Copy-Item "$src\TeklaBridge.exe" $dst
Copy-Item "$src\TeklaMcpServer.Api.dll" $dst
# Также скопировать сторонние зависимости (System.Text.Json, Newtonsoft.Json и т.д.)
```

**exe.config:** создаётся вручную один раз и хранится в extensions-папке.
Содержит `<bindingRedirect>` + `<codeBase>` для всех Tekla DLL, указывающие на `C:\TeklaStructures\2025.0\bin\`.
Исходник-генератор: `C:\temp\GenConfig2.ps1`.

TeklaMcpServer автоматически определяет наличие TS2025 и запускает мост из extensions-папки,
если файл существует (`ResolveBridgePath()` в `ModelTools.Shared.cs`).

## Архитектура

```
Claude Desktop
    │  stdio (JSON-RPC / MCP)
    ▼
TeklaMcpServer.exe  (net8.0-windows)
    │  Process.Start → stdout pipe
    ▼
TeklaBridge.exe  (net48)
    │  .NET Remoting IPC (TS2021) / Trimble.Remoting MMF (TS2025)
    ▼
Tekla Structures 2021 / 2025
```

### Почему два процесса?

Tekla Structures Open API требует **net48**.
MCP SDK требует **net8+**. Совместить в одном процессе невозможно — разные рантаймы, разные CLR.

| Версия | IPC-механизм | Особенность запуска |
|---|---|---|
| TS2021 | .NET Remoting, named pipes | Reflection-фикс имени канала (суффикс `-Console:`) |
| TS2025 | Trimble.Remoting, Memory Mapped Files | Запуск из extensions-папки с `exe.config` + `<codeBase>` |

TeklaBridge — тонкая net48-обёртка: принимает команду первым аргументом, выполняет Tekla API, возвращает JSON в stdout.

## Структура проекта

```
src/
├── TeklaMcpServer.Api/       # Весь Tekla API код (net48) — интерфейсы, DTO, реализации
│   ├── Selection/            # IModelSelectionApi, ModelObjectInfo, TeklaModelSelectionApi
│   │                         # ISelectionCacheManager, SelectionCacheManager
│   │                         # SelectionResult, ToolInputSelectionHandler
│   ├── Drawing/              # IDrawingQueryApi, DrawingInfo
│   │                         # IDrawingViewApi, TeklaDrawingViewApi
│   │                         # IDrawingMarkApi, TeklaDrawingMarkApi
│   │                         # DrawingViewInfo, DrawingViewsResult, DrawingMarkInfo, …
│   ├── Algorithms/
│   │   ├── Packing/          # MaxRectsBinPacker
│   │   └── Marks/            # MarkLayoutEngine, candidate generation, scoring, overlap resolver
│   └── Filtering/
│       ├── Common/           # FilterExpressionParser, FilterTokenizer, FilterAstBuilder, FilterHelper…
│       ├── Drawing/          # DrawingObjectsFilterHelper
│       └── Model/            # IModelFilteringApi, DTOs, TeklaModelFilteringApi
├── TeklaMcpServer/           # MCP сервер (net8.0-windows)
│   ├── Program.cs            # Точка входа, конфигурация MCP host
│   └── Tools/                # Тонкие MCP-обёртки
│       ├── Shared/           # RunBridge(), общий код
│       ├── Connection/       # check_connection
│       ├── Model/            # Model tools
│       └── Drawing/          # Drawing tools
├── TeklaBridge/              # Bridge процесс (net48) — только диспетчер команд
│   ├── Program.cs            # Точка входа + IPC fix + Console capture
│   └── Commands/
│       ├── ModelCommandHandler.cs
│       └── DrawingCommandHandler.cs
└── svMCP/                    # Заглушка (не используется)
```

### Разделение ответственности

| Слой | Проект | Роль |
|---|---|---|
| MCP Tools | `TeklaMcpServer/Tools/` | **Только тонкие обёртки** — вызов `RunBridge()`, разбор JSON, возврат строки |
| Bridge dispatcher | `TeklaBridge/Commands/` | Диспетчеризация команд к классам `TeklaMcpServer.Api`, сериализация результата |
| Весь Tekla код | `TeklaMcpServer.Api/` | Интерфейсы, DTO и все реализации Tekla API |

`TeklaMcpServer.Api` — отдельный net48-проект (не shared с net8-сервером), его ценность — в чёткой границе контрактов внутри bridge-процесса: обзорность, тестируемость через mock и готовность к поддержке нескольких версий Tekla.

## Доступные инструменты

### Соединение

| Инструмент | Описание |
|---|---|
| `check_connection` | Проверить соединение с Tekla Structures; вернуть имя и путь модели |

### Модель

| Инструмент | Описание |
|---|---|
| `get_selected_elements_properties` | Свойства выделенных элементов: Part, BoltGroup, Weld, RebarGroup — все типы |
| `get_selected_elements_total_weight` | Суммарный вес выделенных элементов (кг) |
| `select_elements_by_class` | Выделить элементы по номеру класса Tekla |
| `filter_model_objects_by_type` | Найти и выделить объекты модели по типу (beam, plate, bolt, assembly…) |

### Чертежи

> Требуется открытый Drawing Editor в Tekla Structures

| Инструмент | Описание |
|---|---|
| `list_drawings` | Список всех чертежей модели |
| `find_drawings` | Поиск по имени и/или марке (contains, без учёта регистра) |
| `find_drawings_by_properties` | Поиск по нескольким свойствам (JSON-фильтры) |
| `open_drawing` | Открыть (активировать) чертёж по GUID |
| `close_drawing` | Закрыть активный чертёж |
| `export_drawings_to_pdf` | Экспорт чертежей в PDF по GUID |
| `create_general_arrangement_drawing` | Legacy workaround: создать GA-чертёж из сохранённого вида модели через макрос |
| `create_single_part_drawing` | Создать Single Part drawing через Tekla Open API |
| `create_assembly_drawing` | Создать Assembly drawing через Tekla Open API |
| `get_drawing_context` | Активный чертёж и выделенные объекты |
| `get_sheet_objects_debug` | Dev/debug: все объекты листа, их bbox и кандидаты reserved areas |
| `select_drawing_objects` | Выделить объекты чертежа по ID модельных объектов |
| `filter_drawing_objects` | Фильтр объектов чертежа по типу (Mark, Part, DimensionBase…) |
| `set_mark_content` | Изменить содержимое и шрифт марок |
| `get_drawing_views` | Все виды активного чертежа: позиция, масштаб, размер, размеры листа |
| `move_view` | Переместить вид на листе (абсолютно или на смещение) |
| `set_view_scale` | Изменить масштаб одного или нескольких видов |
| `fit_views_to_sheet` | Основной путь авторасстановки видов: подбор стандартного масштаба, ортографическая раскладка без перекрытий и post-alignment по проекционной связи |
| `get_drawing_marks` | Прочитать марки: позиция, bbox/OBB, `resolvedGeometry`, перекрытия, leader lines, содержимое PropertyElement; фильтрация по виду |
| `create_part_marks` | Создать марки детали с заданным содержимым и стилем |
| `delete_all_marks` | Удалить все марки на активном чертеже |
| `get_drawing_parts` | Все модельные объекты чертежа: PART_POS, ASSEMBLY_POS, PROFILE, MATERIAL, NAME |
| `get_drawing_dimensions` | Все `StraightDimensionSet` активного чертежа: id, `dimensionType`, distance, `viewId/viewType`, orientation, `direction`, `topDirection`, `referenceLine`, bbox set/segments, `dimensionLine`, `leadLineMain/Second`, `textBounds` |
| `move_dimension` | Сдвинуть размерную линию на delta (изменяет `StraightDimensionSet.Distance`) |
| `create_dimension` | Создать `StraightDimensionSet` по набору точек |
| `delete_dimension` | Удалить `StraightDimensionSet` по ID |
| `place_control_diagonals` | Экспериментальный tool: поставить контрольный диагональный размер в целевом виде и вернуть тайминги этапов |
| `get_part_geometry_in_view` | Получить геометрию детали (bbox, start/end, оси) в локальной СК вида |
| `get_all_parts_geometry_in_view` | Пакетно получить геометрию всех деталей вида за один вызов |
| `get_grid_axes` | Получить оси сетки в заданном виде чертежа |
| `resolve_mark_overlaps` | Автоматически разрешить перекрытия текстовых блоков марок внутри каждого вида — минимальные локальные сдвиги |
| `arrange_marks` | Полная автоматическая расстановка марок внутри каждого вида вокруг anchor point |
| `arrange_marks_no_collisions` | Комбинированный проход: `arrange_marks` + повторные `resolve_mark_overlaps` до стабилизации |
| `draw_debug_overlay` | Dev/debug: отрисовать временный overlay (line/polygon/text/cross) в чертеже |
| `clear_debug_overlay` | Dev/debug: очистить overlay слой целиком или по группе |
| `draw_selected_mark_part_axis_geometry` | Dev/debug: показать ось детали и геометрию для выбранных марок |

Раскладка марок сейчас устроена так:
- единица обработки — один `View`, а не весь drawing sheet
- все вычисления layout engine идут в локальной системе координат вида
- Tekla-слой только собирает нейтральные `MarkLayoutItem` и переводит смещения между координатами вида и листа
- `resolve_mark_overlaps` использует только локальный `MarkOverlapResolver` для минимальных сдвигов внутри вида
- `arrange_marks` использует `MarkLayoutEngine`: candidate generation, scoring, greedy placement и затем локальный overlap resolver
- для leader-line marks якорь берется из `LeaderLinePlacing.StartPoint` в координатах вида; `StartPoint` не меняется, двигается только `InsertionPoint`
- геометрия метки теперь централизована в `MarkGeometryHelper`: `LeaderLinePlacing` берет `ObjectAlignedBoundingBox`, `BaseLinePlacing` пытается брать ось связанной детали в текущем виде, fallback — `ObjectAlignedBoundingBox`

Раскладка видов сейчас устроена так:
- legacy `PlaceViews()` удалён из MCP/bridge слоя и больше не является поддерживаемым tool
- основной путь авторасстановки видов — `fit_views_to_sheet`
- post-processing проекционной связи выполняется внутри `DrawingProjectionAlignmentService`
- reserved areas таблиц: `DrawingReservedAreaReader.ReadLayoutTableGeometries()` использует `TableLayout.GetCurrentTables()` → `PresentationConnection.GetObjectPresentation()` → canvas-маркеры (`Segment.Primitives[0/2]`) → точный bbox. Таблицы с `OverlapVithViews=true` пропускаются.
- отступы листа: `TableLayout.GetMarginsAndSpaces()` возвращает реальные margins (обычно 5–10мм), используются как `sheetMargin` в ответе
- канонический источник границ layout-таблиц — именно canvas-маркеры `Segment.Primitives[0/2]`; общая аккумуляция примитивов допускается только как fallback

Размеры сейчас устроены так:
- `get_drawing_dimensions` уже возвращает line-based read model: `dimensionType`, `viewId/viewType`, `orientation`, `direction`, `topDirection`, `referenceLine`, bbox set/segments и `dimensionLine/leadLineMain/leadLineSecond`
- `TextBounds` пока остаётся `null`, пока Tekla-side text geometry не подтверждена runtime-spike'ом
- блок `Drawing/Dimensions` сейчас перепроектируется по эталону `D:\repos\svMCP\dim`
- публичные `arrange_dimensions` и `get_dimension_arrangement_debug` временно скрыты до завершения line-based redesign

## Диагностика

| Файл | Содержимое |
|---|---|
| `C:\temp\teklabridge_log.txt` | Детали последней ошибки (JSON) |
| `C:\temp\tekla_channel.txt` | Результат фикса IPC channel names (сколько каналов исправлено) |
| `%TEMP%\svmcp-perf.log` | Профилирование по слоям `mcp/transport/bridge/api` |

Профилирование:
- `SVMCP_PERF=1` — включить запись таймингов
- `SVMCP_PERF_LOG=<path>` — путь к файлу логов (по умолчанию `%TEMP%\svmcp-perf.log`)

---

## История отладки: как это всё заработало

Документация процесса разработки — что шло не так и как находили решения.

### Этап 1. Начальная архитектура

Изначально проект задумывался как единый MCP-сервер с прямым вызовом Tekla API.
Быстро выяснилось: **это невозможно**.

- MCP SDK (`ModelContextProtocol`) требует .NET 8+
- Tekla Structures 2021 Open API поставляется только для **.NET Framework 4.8**
- Два рантайма в одном процессе не совместимы

**Решение**: разделить на два процесса. `TeklaMcpServer.exe` (net8) запускает `TeklaBridge.exe` (net48) через `Process.Start`, получает JSON из stdout.

---

### Этап 2. Первый критический баг — IPC не работает из-под MCP

После написания TeklaBridge первые тесты показали странную картину:

- `TeklaBridge.exe check_connection` из терминала → `{"status":"connected"}` ✅
- Тот же бинарь, запущенный через MCP сервер → `RemotingException: Failed to connect to an IPC Port` ❌

Поверхностная проверка ничего не давала: процесс запускается под тем же пользователем, в той же сессии, с теми же правами (integrity level не отличался).

#### Что значит "IPC Port"?

Tekla Structures использует **.NET Remoting** — устаревший механизм межпроцессного взаимодействия на базе именованных каналов (named pipes). Клиент подключается к именованному каналу вида:

```
\\.\pipe\Tekla.Structures.Model-Console:2021.0.0.0
```

Соответственно, если имя не совпадает с тем, что создал сервер (сама Tekla Structures), подключение невозможно.

#### Диагностика через reflection

Чтобы понять, какое имя канала вычисляет клиент, добавили диагностику — через reflection читаем статическое поле `ChannelName` из внутренней сборки:

```csharp
var remoterType = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
    .FirstOrDefault(t => t.Name == "Remoter" && t.Namespace?.Contains("ModelInternal") == true);

var channelField = remoterType?.GetField("ChannelName",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

var channelName = channelField?.GetValue(null)?.ToString();
// Результат из-под MCP: "Tekla.Structures.Model-:2021.0.0.0"
// Должно быть:          "Tekla.Structures.Model-Console:2021.0.0.0"
```

**Находка**: слово `Console` в имени канала пропущено. Сервер слушает `...-Console:...`, клиент пытается подключиться к `...-:...`.

#### Причина

Покопавшись в исходниках Tekla (через dotPeek/ILSpy), нашли логику вычисления имени:

```csharp
// Упрощённо — внутри Tekla.Structures.ModelInternal
static Remoter() {
    var stdoutHandle = GetStdHandle(STD_OUTPUT_HANDLE); // WinAPI
    var handleType = GetFileType(stdoutHandle);
    // FILE_TYPE_CHAR (0x0002) = консоль
    // FILE_TYPE_PIPE (0x0003) = pipe
    var suffix = (handleType == FILE_TYPE_CHAR) ? "Console" : "";
    ChannelName = $"Tekla.Structures.Model-{suffix}:{version}";
}
```

То есть Tekla **намеренно** меняет имя канала в зависимости от того, куда смотрит stdout процесса. Когда stdout перенаправлен в pipe (что делает MCP сервер для захвата JSON-вывода), суффикс `-Console` не добавляется. Но **сервер Tekla Structures создаёт канал с суффиксом `-Console` всегда**, потому что у него stdout = консоль.

Результат: клиент ищет несуществующий pipe, соединение падает.

#### Попытка обхода: CreateFile напрямую

Проверили: `CreateFile` на `\\.\pipe\Tekla.Structures.Model-Console:2021.0.0.0` — SUCCESS из обоих контекстов (терминал и MCP). То есть pipe существует и доступен. Проблема исключительно в неправильном имени, которое вычисляет клиентская библиотека.

#### Первый вариант фикса — подмена одного поля

```csharp
// Загружаем ModelInternal вручную (он не грузится автоматически)
var modelInternalPath = Path.Combine(bridgeDir, "Tekla.Structures.ModelInternal.dll");
Assembly.LoadFrom(modelInternalPath);

// Находим класс Remoter и правим ChannelName
var remoter = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
    .First(t => t.Name == "Remoter" && t.Namespace?.Contains("ModelInternal") == true);

var field = remoter.GetField("ChannelName", BindingFlags.Static | BindingFlags.NonPublic);
field.SetValue(null, "Tekla.Structures.Model-Console:2021.0.0.0");
```

Результат: **Model подключился**. `check_connection` заработал. 🎉

---

### Этап 3. Чертежи не работают — Drawing IPC тоже сломан

После успеха с Model добавили Drawing API инструменты. Тесты показали:

- `check_connection`, `get_selected_elements_properties` — работают ✅
- `list_drawings` → `RemotingException: Failed to connect to an IPC Port` ❌

**Причина**: у Tekla Drawing API — **отдельный IPC канал** в отдельной внутренней сборке.

Каналы у трёх подсистем:
- `ModelInternal.dll` → `Tekla.Structures.Model-Console:2021.0.0.0`
- `DrawingInternal.dll` → `Tekla.Structures.Drawing-Console:2021.0.0.0`
- `TeklaStructuresInternal.dll` → `Tekla.Structures.TeklaStructures-Console:2021.0.0.0`

Первый фикс исправлял только `ModelInternal`. При вызове Drawing API подгружается `DrawingInternal.dll` со своим сломанным каналом.

#### Попытка: явная загрузка DrawingInternal

```csharp
Assembly.LoadFrom(Path.Combine(dir, "Tekla.Structures.DrawingInternal.dll"));
// + повторить поиск-и-замену для Drawing Remoter
```

Сработало для Drawing. Но теперь PDF экспорт (`export_drawings_to_pdf`) всё равно падал — уже через `TeklaStructuresInternal`.

---

### Этап 4. Финальный фикс — сканирование всех Internal сборок по паттерну значения

Вместо явного перечисления каждой `*Internal.dll` сделали универсальный алгоритм:

1. Force-load **всех** `Tekla.Structures.*Internal*.dll` из директории TeklaBridge
2. Просканировать **все** статические string-поля во **всех** загруженных Tekla-сборках
3. Любое значение `Tekla.Structures.*-:*` (т.е. есть `-:` — пустой суффикс) → заменить `-:` на `-Console:`

```csharp
// 1. Touch public assemblies чтобы они загрузились
_ = typeof(Tekla.Structures.Model.Model);
_ = typeof(Tekla.Structures.Drawing.DrawingHandler);

// 2. Force-load Internal сборки — они не грузятся автоматически
var dir = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location) ?? "";
foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll"))
    try { Assembly.LoadFrom(dll); } catch { }

// 3. Сканировать все поля, исправить по значению
var bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
    if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
    Type[] types; try { types = asm.GetTypes(); } catch { continue; }
    foreach (var t in types)
        foreach (var f in t.GetFields(bindingFlags)) {
            if (f.FieldType != typeof(string)) continue;
            try {
                var val = f.GetValue(null)?.ToString() ?? "";
                if (val.StartsWith("Tekla.Structures.") && val.Contains("-:"))
                    f.SetValue(null, val.Replace("-:", "-Console:"));
            } catch { }
        }
}
```

**Ключевые нюансы:**

- Проверяем **по значению**, а не по имени класса/поля — имена внутренних классов могут меняться между версиями Tekla
- `GetTypes()` оборачиваем в try/catch — некоторые сборки содержат типы, которые не резолвируются без зависимостей
- В .NET Framework 4.8 можно перезаписывать readonly static поля через reflection (в .NET 8 это уже не работает из-за RVA-полей)
- Фикс должен выполняться **до** создания `new Model()` или `new DrawingHandler()` — конструкторы статических классов выполняются один раз при первом обращении

Результат `C:\temp\tekla_channel.txt` после финального фикса:
```
Fixed 3 channel(s):
FIXED Tekla.Structures.ModelInternal.Remoter.ChannelName: Tekla.Structures.Model-:2021.0.0.0 -> Tekla.Structures.Model-Console:2021.0.0.0
FIXED Tekla.Structures.DrawingInternal.Remoter.ChannelName: Tekla.Structures.Drawing-:2021.0.0.0 -> Tekla.Structures.Drawing-Console:2021.0.0.0
FIXED Tekla.Structures.TeklaStructuresInternal.Remoter.ChannelName: Tekla.Structures.TeklaStructures-:2021.0.0.0 -> Tekla.Structures.TeklaStructures-Console:2021.0.0.0
```

---

### Этап 5. Побочная проблема: Tekla пишет в Console.Out

При первых тестах JSON-вывод TeklaBridge был "загрязнён" внутренней диагностикой Tekla:

```
[TeklaDebug] Connecting to IPC...
[TeklaDebug] Channel resolved: ...
{"status":"connected","modelName":"..."}
```

MCP сервер не мог распарсить ответ — JSON был не первой строкой.

**Решение**: перед первым обращением к Tekla API перехватываем `Console.Out`:

```csharp
var realOut = Console.Out;
var teklaLog = new StringWriter();
Console.SetOut(teklaLog); // Tekla пишет сюда

// ... работаем с Tekla API ...

// Весь наш вывод — только через realOut
realOut.WriteLine(JsonSerializer.Serialize(result));

// Диагностика Tekla доступна при ошибках
var diag = teklaLog.ToString().Trim();
```

---

### Этап 6. Проблемы с деплоем

#### EXE заблокирован Claude Desktop

Claude Desktop держит `TeklaMcpServer.exe` открытым всё время работы. При попытке пересобрать:

```
error MSB3021: Unable to copy file ... Access to the path is denied.
```

**Решение**: всегда закрывать Claude Desktop перед `dotnet build`. Если менялся только TeklaBridge — его можно пересобирать горячо, потому что `bridge/TeklaBridge.exe` не заблокирован.

#### NuGet restore после git rollback

После `git checkout` на более ранний коммит появлялась ошибка:

```
NETSDK1004: Assets file 'project.assets.json' not found.
Run a NuGet restore to generate this file.
```

**Причина**: `obj/` был добавлен в `.gitignore` и удалён из git. После checkout `project.assets.json` отсутствует.

**Решение**:
```bash
dotnet restore src/TeklaBridge/TeklaBridge.csproj
dotnet build ...
```

#### Конфликт файлов после рефакторинга

При переходе от монолитного `ModelTools.cs` к partial-классам в поддиректориях, старый файл остался на диске (не был удалён через git). Компилятор жаловался на дублирование классов.

**Решение**: явно удалить `rm src/TeklaMcpServer/ModelTools.cs` после git restore.

---

### Этап 7. Legacy: создание GA-чертежей через макрос

`create_general_arrangement_drawing` сохранен только как временный legacy workaround. Для дальнейшего развития блока создания чертежей используем Open API, не макросы.

Исторически для GA здесь использовался macro-подход, потому что он повторял штатный UI workflow выбора saved model view. Это не рекомендуемый путь для дальнейшей разработки: он зависит от UI, локализации и внутренних имен элементов диалога.

Алгоритм:
1. Узнать `XS_MACRO_DIRECTORY` через `TeklaStructuresSettings.GetAdvancedOption`
2. Записать временный `.cs` файл с макросом в поддиректорию `modeling/`
3. Вызвать `Operation.RunMacro(macroName)` и ждать завершения через `IsMacroRunning()`
4. Удалить временный файл

Нюансы:
- Имя файла должно быть без пути — только `macroname.cs`, директория определяется автоматически
- Макросы в `modeling/` доступны только в режиме моделирования, в `drawings/` — в редакторе чертежей
- `RunMacro` асинхронен, необходим polling через `IsMacroRunning()` с таймаутом

---

### Этап 9. Поддержка Tekla Structures 2025

После перехода на TS2025 TeklaBridge перестал подключаться с новой ошибкой:

```
RemotingIOException: Cannot connect to remoting service
'Tekla.Structures.Model-TeklaStructures-Console:2025.0.0.0' because it does not exist
```

#### Смена транспорта в TS2025

TS2025 заменил `.NET Remoting` (named pipes) на `Trimble.Remoting` поверх **Memory Mapped Files** (MMF).
Имя MMF-объекта включает не AssemblyVersion, а **FileVersion** установленного DLL.

| | TS2021 | TS2025 |
|---|---|---|
| Транспорт | `System.Runtime.Remoting`, named pipes | `Trimble.Remoting`, MMF |
| Имя канала | `Tekla.Structures.Model-Console:2021.0.0.0` | `Tekla.Structures.Model-TeklaStructures-Console:2025.0.52577.0` |
| NuGet версия | `2021.0.0.0` = FileVersion | `2025.0.0.0` ≠ FileVersion `2025.0.52577.0` |

#### Попытка 1: Runtime-патч через reflection

Прочитать FileVersion установленного DLL через `FileVersionInfo.GetVersionInfo()` и обновить
статические поля `ChannelName` в загруженных NuGet-сборках. **Не сработало** — статические
конструкторы NuGet DLL ещё не выполнились в момент патча; `Remoter.ChannelName` был `null`.

#### Решение: запуск из extensions-папки с exe.config

Правильный подход — загрузить **установленные** Tekla DLL (с правильной FileVersion) вместо NuGet копий.

1. **TeklaBridge.exe** копируется в:
   `C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`

2. Рядом кладётся **`TeklaBridge.exe.config`** с `<codeBase>` записями для всех Tekla/Trimble DLL:
   ```xml
   <dependentAssembly>
     <assemblyIdentity name="Tekla.Structures.Model" publicKeyToken="..." culture="neutral"/>
     <bindingRedirect oldVersion="0.0.0.0-2025.0.0.0" newVersion="2025.0.0.0"/>
     <codeBase version="2025.0.0.0" href="file:///C:/TeklaStructures/2025.0/bin/Tekla.Structures.Model.dll"/>
   </dependentAssembly>
   ```

3. Когда TeklaBridge запускается из этой папки, CLR подхватывает `exe.config` и загружает
   DLL из `C:\TeklaStructures\2025.0\bin\` — там FileVersion `2025.0.52577.0`, канал работает.

4. **TeklaMcpServer** автоматически выбирает мост из extensions-папки при наличии:
   ```csharp
   private static string ResolveBridgePath() {
       // Newest TS >= 2025 with TeklaBridge.exe in extensions folder takes priority
       var extensionsBridge = Directory.GetDirectories(@"C:\TeklaStructures")
           .Where(d => Version.TryParse(...) && v.Major >= 2025)
           .OrderByDescending(...)
           .Select(d => Path.Combine(d, "Environments", "common", "extensions", "svMCP", "TeklaBridge.exe"))
           .FirstOrDefault(File.Exists);
       return extensionsBridge ?? localBridgePath;
   }
   ```

#### Дополнительные изменения (TeklaBridge/Program.cs)

- Определение версии API: `typeof(Model).Assembly.GetName().Version?.Major`
- `< 2025` → `ApplyIpcChannelFix()` (старый reflection-фикс `-:` → `-Console:`)
- `>= 2025` → `ApplyTs2025ChannelVersionFix()` (читает FileVersion, патчит если поля инициализированы)
- Определение пути bin: TS2021 — `nt/bin/`, TS2025 — `bin/` напрямую
- `XS_DIR` для TS2025 = корневая папка (`C:\TeklaStructures\2025.0`), без `nt/` подкаталога

#### Итог

| Проблема | Причина | Решение |
|---|---|---|
| MMF канал не найден | NuGet DLL имеет FileVersion `2025.0.0.0`, а не `2025.0.52577.0` | Запуск из extensions-папки, DLL грузятся через `<codeBase>` из installed dir |
| Сборка не находит путь TS2025 | Код проверял только `nt/bin/` | Добавлена проверка `bin/` напрямую |
| `XS_SYSTEM` указывал на TS2021 | Нет — `DetectTeklaRootForMajor` возвращал null | Исправлена проверка директории |

---

### Итог (все версии)

| Проблема | Причина | Решение |
|---|---|---|
| IPC не работает из pipe (TS2021) | Tekla вычисляет имя канала по типу stdout | Reflection-фикс всех `*Internal` каналов до `new Model()` |
| Drawing API не подключается | `DrawingInternal.dll` не грузится автоматически | Force-load всех `*Internal*.dll` |
| PDF экспорт падает | `TeklaStructuresInternal` тоже сломан | Универсальное сканирование по значению `-:` |
| Мусор в JSON выводе | Tekla пишет в Console.Out | Перехват Console.Out до API вызовов |
| EXE заблокирован | Claude Desktop держит файл открытым | Закрывать Claude Desktop перед пересборкой |
| NETSDK1004 после git | `obj/` в .gitignore | `dotnet restore` перед build |
| `filter_model_objects` всегда возвращал 0 | `is Beam` не работает для IPC-прокси + `"count"` vs `"Count"` | `GetType().Name` + обе формы ключа |
| MMF канал не найден (TS2025) | FileVersion mismatch: NuGet `2025.0.0.0` ≠ installed `2025.0.52577.0` | extensions-папка + exe.config с `<codeBase>` на installed DLLs |

---

### Этап 8. filter_model_objects_by_type: два скрытых бага

После реализации фильтрации инструмент возвращал `count: 0` для любого типа, хотя объекты в модели есть.

#### Баг 1: `is Beam` не работает для .NET Remoting прокси

`GetAllObjects()` возвращает объекты через IPC как **transparent proxy**. Оператор `is` проверяет идентичность типа относительно локальной сборки — для прокси это всегда `false`, даже если `GetType().Name == "Beam"`.

```csharp
// Было (всегда false для IPC-объектов):
modelObject is Beam

// Стало:
modelObject.GetType().Name.Equals("Beam", StringComparison.OrdinalIgnoreCase)
```

Диагностика показала: `GetType().Name` возвращает `"Beam"` корректно, значит тип правильный — но оператор `is` его не признаёт. Это специфика .NET Remoting: прокси-объект не является экземпляром локального `Beam`, хотя ведёт себя как он.

#### Баг 2: `"count"` vs `"Count"` — регистр JSON-свойств

`System.Text.Json` сериализует свойства **по имени C#-поля as-is** (PascalCase → `"Count"`).

```csharp
// FilteredModelObjectsResult сериализуется как:
{ "Count": 59, "ObjectIds": [...] }

// Обёртка искала (camelCase):
doc.RootElement.TryGetProperty("count", ...)  // всегда false → count = 0

// Исправлено:
doc.RootElement.TryGetProperty("Count", ...) || doc.RootElement.TryGetProperty("count", ...)
```

Почему раньше не замечали: `select_by_class` и `get_selected_weight` используют анонимные C# объекты (`new { count }`) — их поля сериализуются строчными буквами и совпадают с ожидаемым ключом. `FilteredModelObjectsResult` как именованный класс — нет.

#### Дополнительно: AutoFetch и foreach

`GetObjectsByFilter` возвращает `ModelObjectEnumerator`, который реализует `IEnumerable`, но `foreach` в .NET Remoting контексте работает ненадёжно. Заменено на явный `MoveNext()`. Также добавлен `ModelObjectEnumerator.AutoFetch = false` — без него в контексте внешнего процесса фоновая подкачка объектов может обрываться.
