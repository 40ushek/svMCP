# Project Map

## Назначение

`svMCP` это локальный MCP-сервер для работы с `Tekla Structures` через отдельный
`net48` bridge-процесс.

Базовый runtime-путь:

`MCP client -> TeklaMcpServer (net8) -> PersistentBridge -> TeklaBridge (net48) -> TeklaMcpServer.Api -> Tekla Open API`

## Solution

Файл решения: `svMCP.sln`

Проекты:

- `TeklaMcpServer`
  - `net8.0-windows`
  - основной MCP server
  - регистрирует инструменты через `WithToolsFromAssembly()`
- `TeklaBridge`
  - `net48`
  - bridge-процесс, который реально подключается к Tekla runtime
  - умеет работать в одноразовом режиме и в persistent `--loop`
- `TeklaMcpServer.Api`
  - `net48`
  - основной слой Tekla API, доменная логика, DTO, layout/geometry/mark/dimension APIs
- `TeklaMcpServer.Host`
  - `net48`
  - локальный host/debug utility для ручной проверки drawing runtime
- `TeklaMcpServer.Tests`
  - `net8.0-windows`
  - unit/integration tests для layout, geometry, dimensions, bridge
- `svMCP`
  - `netstandard2.0`
  - сейчас фактически заглушка

## Архитектурные слои

### 1. MCP transport

`TeklaMcpServer/Program.cs`

- поднимает MCP server по stdio
- не содержит Tekla-логики
- инструменты живут в `TeklaMcpServer/Tools`

### 2. Tool facade

`TeklaMcpServer/Tools`

- `Connection`
- `Model`
- `Drawing`
- `Shared`

Паттерн:

- один статический partial-класс `ModelTools`
- каждый tool это thin wrapper над `RunBridge(...)`
- транспорт к bridge сделан через `PersistentBridge`

### 3. Bridge transport/runtime boundary

`TeklaMcpServer/Tools/Shared/ModelTools.Shared.cs`
`TeklaMcpServer/Tools/Shared/PersistentBridge.cs`

- выбирает путь к `TeklaBridge.exe`
- для TS2025+ предпочитает bridge из `extensions` папки
- держит один persistent bridge-процесс
- сериализует запросы в JSON protocol

### 4. Bridge entry/dispatch

`TeklaBridge/Program.cs`
`TeklaBridge/Commands`

- подготавливает Tekla environment
- применяет фиксы IPC/MMF channel names
- перехватывает `Console.Out`, чтобы Tekla не ломала JSON protocol
- маршрутизирует команды через `CommandDispatcher`

Handlers:

- `ModelCommandHandler`
- `DrawingCommandHandler`

### 5. Core API/domain layer

`TeklaMcpServer.Api`

Это главный рабочий слой. Здесь находятся реальные адаптеры Open API, planner'ы,
алгоритмы и transport DTO.

Основные блоки:

- `Selection`
  - выбор объектов модели, кэш, результаты выбора
- `Filtering`
  - общий tokenizer/parser AST
  - model/drawing filtering APIs
- `Drawing/Query`
  - список и поиск чертежей
- `Drawing/Creation`
  - создание GA / single-part / assembly drawings
- `Drawing/Interaction`
  - выбор и интеракции в открытом drawing editor
- `Drawing/ViewLayout`
  - чтение видов, layout, fit-to-sheet, projection alignment
  - ключевые сущности: `BaseViewSelection`, `NeighborSet`, `StandardNeighborResolver`
  - стратегии: `BaseProjectedDrawingArrangeStrategy`, `GaDrawingMaxRectsArrangeStrategy`, `ShelfPackingDrawingArrangeStrategy`
- `Drawing/Dimensions`
  - line-first dimension model и операции
  - split на `Grouping / Arrangement / Placement`
  - ключевые сущности: `DimensionItem`, `DimensionGroup`, `DimensionOperations`
- `Drawing/Marks`
  - чтение марок, layout, overlap resolution
- `Drawing/Geometry`
  - part geometry, grid axes, reserved areas
- `Drawing/Parts`
  - DTO и API по деталям чертежа
- `Drawing/DebugOverlay`
  - отрисовка временного overlay в drawing runtime
- `Drawing/Parsing`
  - parser layer для bridge-команд drawing-модуля
- `Algorithms/Marks`
  - `MarkLayoutEngine`, `MarkOverlapResolver`, candidate/cost logic
- `Algorithms/Packing`
  - `MaxRectsBinPacker`
- `Algorithms/Geometry`
  - `ConvexHull`, `FarthestPointPair`
- `Diagnostics`
  - perf tracing

## Точки входа

- `TeklaMcpServer/Program.cs`
  - MCP entry point
- `TeklaBridge/Program.cs`
  - bridge entry point
- `TeklaMcpServer.Host/Program.cs`
  - локальный debug/host entry point

## Ключевые зависимости между модулями

- `TeklaMcpServer -> TeklaBridge`
  - через `PersistentBridge` и JSON protocol
- `TeklaBridge -> TeklaMcpServer.Api`
  - через command handlers и вызовы API-слоя
- `TeklaMcpServer.Api -> Tekla Open API`
  - прямой доступ к `Tekla.Structures.*`
- `TeklaMcpServer.Tests -> TeklaMcpServer + TeklaMcpServer.Api`
  - покрытие planner/geometry/dimensions/bridge contract

## Архитектурные паттерны

- thin transport layer
- explicit runtime boundary между `net8` и `net48`
- command dispatcher + handlers
- partial tool facade
- strategy pattern для layout видов
- DTO/public-contract layer отдельно от domain logic
- persistent worker process для снижения стоимости repeated Tekla calls

## Текущий фокус roadmap внутри `src`

Документы:

- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_DRAWING_LAYOUT.md`
  - активный roadmap по компоновке чертежа и lightweight layout context
- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_VIEWS.md`
  - исторический roadmap по реализованному `fit_views_to_sheet`
- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_RUNTIME.md`
  - возможная будущая runtime boundary между planner/server/local host
- `TeklaMcpServer.Api/Drawing/Dimensions/ROADMAP_DIMENSIONS.md`
  - перевод dimensions на каноническую `dim` domain model

По состоянию кода:

- `ViewLayout` это активный planner-блок по компоновке листа
- `Dimensions` уже переведены на line-first model, но redesign еще продолжается
- `Marks` уже имеют отдельный layout engine и overlap resolver

## Инварианты проекта

- диагональные размеры трактуются как контроль геометрии между крайними дальними точками сборки
- для layout-таблиц канонический источник видимых границ:
  `Segment.Primitives[0/2]` из presentation model
- контракт layout-table markers:
  - `Primitives[0]` = min-corner marker
  - `Primitives[2]` = max-corner marker
- marker-based path нельзя заменять общей аккумуляцией примитивов без явного доказательства, что canvas markers недоступны

## Родительская папка: найденные `.md`

В `D:\repos\svMCP`:

- `AGENTS.md`
  - общие инструкции по работе агента
- `README.md`
  - обзор проекта, установка, архитектура, инструменты, история отладки
- `ROADMAP.md`
  - общий roadmap проекта: размеры, виды, марки, batching, ограничения
- `ANNOTATION_PLACEMENT.md`
  - алгоритмические заметки по маркам и размерам
- `docs/drawings/marks-roadmap.md`
  - архитектура и roadmap для mark placement
- `docs/drawings/view-projection-link.md`
  - отдельный план по проекционной связи в `fit_views_to_sheet`

В `D:\repos\svMCP\src`:

- `AGENTS.md`
- `CLAUDE.md`
- `PROJECT_MAP.md`
- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_DRAWING_LAYOUT.md`
- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_VIEWS.md`
- `TeklaMcpServer.Api/Drawing/ViewLayout/ROADMAP_RUNTIME.md`
- `TeklaMcpServer.Api/Drawing/Dimensions/ROADMAP_DIMENSIONS.md`
