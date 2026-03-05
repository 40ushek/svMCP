# svMCP — Tekla Structures MCP Server

MCP (Model Context Protocol) сервер для работы с **Tekla Structures 2021** через Claude Desktop и другие MCP-клиенты.

## Требования

- Windows 10/11
- .NET 8 SDK
- .NET Framework 4.8 (входит в Windows 10+)
- Tekla Structures 2021 (установленная и запущенная)

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

## Архитектура

```
Claude Desktop
    │  stdio (JSON-RPC / MCP)
    ▼
TeklaMcpServer.exe  (net8.0-windows)
    │  Process.Start → stdout pipe
    ▼
TeklaBridge.exe  (net48)
    │  .NET Remoting IPC
    ▼
Tekla Structures 2021
```

### Почему два процесса?

Tekla Structures 2021 Open API требует **net48** и работает через **.NET Remoting IPC**.
MCP SDK требует **net8+**. Совместить в одном процессе невозможно — разные рантаймы, разные CLR.

TeklaBridge — тонкая net48-обёртка: принимает команду первым аргументом, выполняет Tekla API, возвращает JSON в stdout.

## Структура проекта

```
src/
├── TeklaMcpServer.Api/       # Весь Tekla API код (net48) — интерфейсы, DTO, реализации
│   ├── Connection/           # ITeklaConnectionApi, ConnectionInfo
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
| `export_drawings_to_pdf` | Экспорт чертежей в PDF по GUID |
| `create_general_arrangement_drawing` | Создать GA-чертёж из сохранённого вида модели |
| `get_drawing_context` | Активный чертёж и выделенные объекты |
| `select_drawing_objects` | Выделить объекты чертежа по ID модельных объектов |
| `filter_drawing_objects` | Фильтр объектов чертежа по типу (Mark, Part, DimensionBase…) |
| `set_mark_content` | Изменить содержимое и шрифт марок |
| `get_drawing_views` | Все виды активного чертежа: позиция, масштаб, размер, размеры листа |
| `move_view` | Переместить вид на листе (абсолютно или на смещение) |
| `set_view_scale` | Изменить масштаб одного или нескольких видов |
| `fit_views_to_sheet` | Авторасстановка видов: подбор стандартного масштаба, ортографическая раскладка без перекрытий |
| `get_drawing_marks` | Прочитать марки: позиция, bbox текстового блока, перекрытия, содержимое PropertyElement; фильтрация по виду |
| `get_drawing_parts` | Все модельные объекты чертежа: PART_POS, ASSEMBLY_POS, PROFILE, MATERIAL, NAME |
| `get_drawing_dimensions` | Все `StraightDimensionSet` активного чертежа: id, distance, координаты сегментов |
| `move_dimension` | Сдвинуть размерную линию на delta (изменяет `StraightDimensionSet.Distance`) |
| `resolve_mark_overlaps` | Автоматически разрешить перекрытия текстовых блоков марок — mark layout engine + локальный overlap resolver |

`resolve_mark_overlaps` сейчас работает так:
- Tekla-слой собирает нейтральные `MarkLayoutItem` через adapter, а сам алгоритм живет в `TeklaMcpServer.Api/Algorithms/Marks/`
- сначала размещаются самые конфликтные марки
- для leader-line marks генерируются 8 позиций вокруг якоря + текущая позиция
- для non-leader marks используются локальные смещения вокруг текущего положения
- score учитывает overlap, crowding, длину leader line, смещение от текущей позиции и мягкий приоритет кандидата
- после greedy placement запускается локальный overlap resolver; `LeaderLinePlacing.StartPoint` не меняется, двигается только `InsertionPoint`

## Диагностика

| Файл | Содержимое |
|---|---|
| `C:\temp\teklabridge_log.txt` | Детали последней ошибки (JSON) |
| `C:\temp\tekla_channel.txt` | Результат фикса IPC channel names (сколько каналов исправлено) |

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
dotnet restore src/TeklaMcpServer/TeklaBridge/TeklaBridge.csproj
dotnet build ...
```

#### Конфликт файлов после рефакторинга

При переходе от монолитного `ModelTools.cs` к partial-классам в поддиректориях, старый файл остался на диске (не был удалён через git). Компилятор жаловался на дублирование классов.

**Решение**: явно удалить `rm src/TeklaMcpServer/ModelTools.cs` после git restore.

---

### Этап 7. Создание GA-чертежей через макрос

Tekla API не предоставляет прямого метода для создания GA (General Arrangement) чертежей из кода. Единственный способ — через **Tekla Macro** (скрипт на C#, исполняемый внутри Tekla процесса через `akit` интерфейс).

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

### Итог

| Проблема | Причина | Решение |
|---|---|---|
| IPC не работает из pipe | Tekla вычисляет имя канала по типу stdout | Reflection-фикс всех `*Internal` каналов до `new Model()` |
| Drawing API не подключается | `DrawingInternal.dll` не грузится автоматически | Force-load всех `*Internal*.dll` |
| PDF экспорт падает | `TeklaStructuresInternal` тоже сломан | Универсальное сканирование по значению `-:` |
| Мусор в JSON выводе | Tekla пишет в Console.Out | Перехват Console.Out до API вызовов |
| EXE заблокирован | Claude Desktop держит файл открытым | Закрывать Claude Desktop перед пересборкой |
| NETSDK1004 после git | `obj/` в .gitignore | `dotnet restore` перед build |
| `filter_model_objects` всегда возвращал 0 | `is Beam` не работает для IPC-прокси + `"count"` vs `"Count"` | `GetType().Name` + обе формы ключа |

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
