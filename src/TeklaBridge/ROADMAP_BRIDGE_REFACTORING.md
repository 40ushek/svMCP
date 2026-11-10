# Roadmap: упрощение и рефакторинг TeklaBridge

Состояние на 2026-11-10, ветка `fix/projection-alignment-section-side`.
Основано на чтении `Program.cs`, `Commands/CommandDispatcher.cs`,
`Commands/DrawingCommandHandler.cs`, части `Geometry.cs` и `Dimensions.cs`,
`ViewDimensionContextProvider*.cs`. Что не читалось, помечено ниже.

## Что не трогаем

- Схема из отдельных процессов: Tekla Open API работает на net48, MCP SDK на net8.
- Контроллер-владелец процесса моста (см. `ROADMAP_TRAY_CONTROL.md`).
- Подмена `Console.Out` в `Program.cs`: без неё вывод Tekla ломает JSON-протокол.

## Что не так

1. **Логика в хэндлерах моста.** По `CLAUDE.md` мост должен быть тонким
   диспетчером, вся логика живёт в `TeklaMcpServer.Api`. В
   `Commands/DrawingCommandHandler.Geometry.cs` (около 1800 строк) лежат
   `HandleGetContactCandidatePoints` и `HandleGetPartDegreesOfFreedom`
   (по 100–125 строк), сборка ответа в `HandleGetStructuralOutline`,
   помощники `FindView`, `Describe`, `ExtentAlong`, `ReadExclusions` и код сбора
   текстовых боксов марок, работающий напрямую с `Tekla.Structures.Drawing.Mark`.
   `DrawingCommandHandler.Dimensions.cs` (около 1800 строк) прочитан только
   частично: что там ещё, кроме разбора аргументов, не установлено.
2. **Одна команда описана в четырёх местах**: `[McpServerTool]` в `Tools/`, список
   `case` в `DrawingCommandHandler.cs` (86 штук), вложенный `switch` в
   `TryHandle*Commands` и `or`-список в `CommandDispatcher` (команды с `viewId` в
   `args[1]`, вызывающие `ObserveView`). Связаны они только строками. Опечатка
   видна как `Unknown command` во время работы, а не при сборке.
3. **Аргументы позиционные, `string[]`.** В хэндлерах около 90 обращений `args[`.
   Порядок параметров нигде не зафиксирован типами. Пример: `get_view_dimension_context`
   получает от обёртки 12 аргументов после имени команды (`viewId` ... `layerRules`),
   а с `referenceZones` ещё три (`referenceZones`, `referenceExcludePrefixes`,
   `referenceExcludeMaterials`), то есть до 15. Читаются они в двух местах:
   в теле `HandleGetViewDimensionContext` (индексы до 11) и в парсерах
   `DrawingCommandParsers.ParseViewDimension*` (индексы 12-15), так что полный
   список параметров из одного места не увидеть.
4. **Одинаковая обвязка в `Tools/`**: блок `JsonDocument.Parse` с ветками
   `"Error: ..."` и `"Bridge error: ..."` повторяется примерно в 63 из 80
   вызовов `RunBridge`.
5. **Три копии `PerfTrace`**: `TeklaBridge/PerfTrace.cs`,
   `TeklaMcpServer.Api/Diagnostics/PerfTrace.cs` (107 строк, содержимое не читалось),
   `TeklaMcpServer/Tools/Shared/PerfTrace.cs`.
6. **`Program.cs` (около 530 строк) делает слишком много**: цикл запросов,
   выполнение команд, поиск корня Tekla и два набора reflection-обходов
   имён IPC-каналов (`ApplyIpcChannelFix` для TS2021,
   `ApplyTs2025ChannelVersionFix` для TS2025).

## Результаты ревью моста

Подтверждено:

- `CommandDispatcher` не вызывает `ObserveView` для части команд с `viewId` в
  `args[1]`: `get_part_solid_geometry_in_view`, `get_part_candidate_points_in_view`,
  `get_all_part_points_in_view`, `get_part_points_in_view`,
  `get_part_degrees_of_freedom`, `get_dimension_chain_coverage`. Это не даёт
  неверных данных (`Get` и `RequireContext` сами вызывают `ObserveView`), а лишь
  разное поведение у похожих команд: чтение другого вида одними командами
  закрывает контекст предыдущего вида, другими нет.
- `bool.Parse` и `double.Parse` на позиционных аргументах давали сырой
  `FormatException`. Исправлено (шаг 1).
- Мелкие издержки: `WriteDimensionObservationJson` сериализует `Parts` дважды
  (для хэша и для вывода) и меняет `Header` во входном объекте;
  `TryHandleDimensionCommands` создаёт `TeklaDrawingDimensionObservationApi` на
  каждую команду, хотя нужен он одной.

Проверено и не подтвердилось:

- «`CaptureParts` падает при повторном вызове коллбэка»: коллбэки вызываются
  один раз (`TeklaDrawingViewContactApi.cs`, `StructuralOutline.cs`), исключение
  намеренно защищает этот инвариант.
- «Неинициализированный `SvMcpLogRouter` валит запуск»: `Append` глотает любые
  исключения.
- «Частичная запись в `create_dimensions_batch` скрыта»: `CreateBatch` ловит
  ошибки по каждой цепочке (`failed`, остальные `skipped`) и возвращает
  `dimensionId` созданных размеров; в `catch` хэндлера попадают только ошибки
  проверки входа, до первой записи в чертёж.

## План

1. **Безопасный разбор аргументов.** Сделано: `ReadBoolArg` и
   `ReadOptionalDoubleArg` в `DrawingCommandHandler.Geometry.cs`.
2. **`RunBridgeJson` в `Tools/Shared`.** Один helper вместо повторяющегося
   блока; обёртки переводить по файлам. Поведение не меняется.
3. **Таблица команд.** Словарь «имя команды → обработчик» с флагом
   «`args[1]` — viewId» вместо `case`-списков и `or`-списка в диспетчере.
   Новых библиотек не нужно.
4. **Вынос логики из хэндлеров в `TeklaMcpServer.Api`.** Сначала `Geometry.cs`,
   затем `Dimensions.cs` (предварительно прочитать целиком). В хэндлере остаются
   разбор аргументов, вызов `Api` и `WriteJson`. Делать в отдельной ветке.
5. **Уборка.** Свести `PerfTrace` в один (в `Shared`); вынести обходы IPC-каналов из
   `Program.cs` в отдельный файл; типизированный `ArgReader` для позиционных
   аргументов (постепенно, по одному файлу).

## Открытый вопрос

Нужна ли поддержка TS2021. От ответа зависит, можно ли удалить
`ApplyIpcChannelFix` и ветку одноразового режима `ExecuteLegacyCommand`
(мост запускается только с `--loop`; одноразовый режим вызывается, судя по
поиску, лишь из тестов — вызывают ли именно его, не проверялось).

## Сборка и тесты в этом окружении

- На машине установлен только SDK 10.0.403, README требует .NET 8 SDK. Сборка
  моста и тестов на нём проходит.
- `dotnet test` сначала падал в `TeklaBridge.Controller.csproj` с
  `Unable to find fallback package folder 'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'`:
  эта папка записана в старом `obj/project.assets.json`, а Visual Studio на машине
  нет. Помогло `dotnet restore src/TeklaBridge.Controller/TeklaBridge.Controller.csproj --force-evaluate`
  (то же для тестового проекта).
- Сборка и тесты без остановки MCP-сессии:
  `dotnet test src/TeklaMcpServer.Tests/TeklaMcpServer.Tests.csproj -c Release -p:BaseOutputPath=E:/repos/svMCP/.codex-build/<имя>/`
  (только прямые слэши).
- Развёртывание моста для TS2025 на этой машине выполнено 2026-11-10; что пришлось
  поправить в `TeklaBridge.exe.config` и про перезапуск контроллера, описано в
  `README.md` (раздел «Развёртывание на новой машине»). После этого
  `check_connection` вернул `connected` для модели `!test2025`.
- `.mcp.json` указывал на `D:\repos\svMCP\...`, на этой машине репозиторий на `E:`;
  путь исправлен на `E:`.
- Базовое состояние тестов: 1357 всего, 1347 прошли, 1 пропущен, **9 падают**
  и до любых правок (проверено откатом через `git stash`): 7 случаев
  `TeklaBridgeSmokeTests.LegacyOneShotCommandsReturnValidJson`,
  `TeklaBridgeSmokeTests.LoopModeHandlesSequentialCommandsWithoutProtocolDesync` и
  `PersistentBridgeTests.SendRestartsProcessAfterResponseTimeout`. Первые
  падают с `JsonReaderException: The input does not contain any JSON tokens`
  (запущенный `TeklaBridge.exe` ничего не вывел); почему именно, не выяснялось.
  Живой Tekla в этой сессии не подключался (MCP-сервер `tekla` не стартовал).
