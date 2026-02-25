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
MCP SDK требует **net8+**. Совместить в одном процессе невозможно.

TeklaBridge — тонкая net48-обёртка: принимает команду аргументом, выполняет Tekla API, возвращает JSON в stdout.

### Критический баг: IPC Channel Name

#### Суть проблемы

Tekla API вычисляет имя IPC-канала по типу stdout дочернего процесса:
- stdout = консоль → `Tekla.Structures.Model-Console:2021.0.0.0` ✓
- stdout = pipe (перенаправлен MCP сервером) → `Tekla.Structures.Model-:2021.0.0.0` ✗

Tekla сервер всегда создаёт pipe с `-Console`. Каждая подсистема имеет свой канал:
- `ModelInternal.Remoter.ChannelName` → `Tekla.Structures.Model-Console:2021.0.0.0`
- `DrawingInternal.Remoter.ChannelName` → `Tekla.Structures.Drawing-Console:2021.0.0.0`
- `TeklaStructuresInternal.Remoter.ChannelName` → `Tekla.Structures.TeklaStructures-Console:2021.0.0.0`

#### Диагностика

1. TeklaBridge из терминала → `{"status":"connected"}` ✓
2. TeklaBridge из Claude Desktop → `RemotingException: Failed to connect to an IPC Port` ✗
3. Все параметры процесса идентичны (пользователь, сессия, integrity level)
4. `CreateFile` на pipe-имя с "Console" — SUCCESS из обоих контекстов
5. Reflection: `Remoter.ChannelName = "Tekla.Structures.Model-:2021.0.0.0"` — слово "Console" пропущено

#### Фикс

Перезапись `ChannelName` через reflection до первого `new Model()` / `new DrawingHandler()`.
В .NET Framework 4.8 readonly static поля перезаписываются через `FieldInfo.SetValue(null, value)`.

Алгоритм фикса в `TeklaBridge/Program.cs`:
1. Force-load всех `Tekla.Structures.*Internal*.dll` из директории TeklaBridge
2. Сканировать все static string поля во всех Tekla сборках
3. Любое поле со значением `Tekla.Structures.*-:*` → заменить `-:` на `-Console:`

```csharp
// Force-load Internal assemblies
var dir = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location)!;
foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll"))
    try { Assembly.LoadFrom(dll); } catch { }

// Fix ALL broken channel names by value pattern
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
    if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
    foreach (var t in asm.GetTypes())
        foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
            if (f.FieldType != typeof(string)) continue;
            var val = f.GetValue(null)?.ToString() ?? "";
            if (val.StartsWith("Tekla.Structures.") && val.Contains("-:"))
                f.SetValue(null, val.Replace("-:", "-Console:"));
        }
}
```

## Структура проекта

```
src/
├── TeklaMcpServer/           # MCP сервер (net8.0-windows)
│   ├── Program.cs            # Точка входа, конфигурация MCP host
│   ├── Tools/
│   │   ├── Shared/           # RunBridge(), общий код
│   │   ├── Connection/       # CheckConnection
│   │   ├── Model/            # Работа с элементами модели
│   │   └── Drawing/          # Работа с чертежами
│   └── TeklaBridge/          # Bridge процесс (net48)
│       ├── Program.cs        # Точка входа + reflection fix
│       └── Commands/
│           ├── ModelCommandHandlers.cs
│           └── DrawingCommandHandlers.cs
└── svMCP/                    # Заглушка (не используется)
```

## Доступные инструменты

### Соединение

| Инструмент | Описание |
|---|---|
| `check_connection` | Проверить соединение с Tekla Structures; вернуть имя и путь модели |

### Модель

| Инструмент | Описание |
|---|---|
| `get_selected_elements_properties` | Свойства выделенных элементов: GUID, имя, профиль, материал, класс, вес |
| `get_selected_elements_total_weight` | Суммарный вес выделенных элементов (кг) |
| `select_elements_by_class` | Выделить элементы по номеру класса Tekla |

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

## Диагностика

| Файл | Содержимое |
|---|---|
| `C:\temp\teklabridge_log.txt` | Детали последней ошибки (JSON) |
| `C:\temp\tekla_channel.txt` | Результат фикса IPC channel names (сколько каналов исправлено) |
