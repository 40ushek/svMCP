# svMCP — Виды: проекционная связь (`fit_views_to_sheet`)

## Цель

После базовой расстановки видов в `fit_views_to_sheet` выполнить дополнительное выравнивание по проекционной связи:

- `FrontView <-> SectionView`: выравнивание по `Y`
- `FrontView <-> TopView`: выравнивание по `X`

Задача должна решаться как отдельный post-processing этап и не менять существующую стратегию раскладки видов.

---

## Границы работ

- Работать только в `src/`
- Не менять MCP API и публичные tool names без необходимости
- Не переписывать `FrontViewDrawingArrangeStrategy` и другие стратегии раскладки
- Не смешивать этап выбора масштаба и этап проекционного выравнивания

Статус на текущий момент:
- решение реализовано и используется в `fit_views_to_sheet`
- документ сохранён как design note / maintenance reference
- актуальные файлы ниже уже соответствуют разнесённой структуре `Drawing/Views`

---

## Затронутые модули

- `src/TeklaMcpServer.Api/Drawing/Views/TeklaDrawingViewApi.Layout.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentService.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentService.Assembly.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentService.Ga.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentService.Helpers.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentModels.cs`
- `src/TeklaMcpServer.Api/Drawing/Views/DrawingProjectionAlignmentMath.cs`
- `src/TeklaMcpServer.Api/Drawing/Geometry/TeklaDrawingGridApi.cs`
- тесты в `src/TeklaMcpServer.Tests/`

---

## Архитектурное решение

- Оставить `fit_views_to_sheet` владельцем orchestration
- Выделить отдельный сервис, например уровня `DrawingProjectionAlignmentService`
- Сервис вызывается в самом конце `fit_views_to_sheet`:
  - после выбора optimal scale
  - после arrange
  - после текущей post-correction рамки вида
  - до финального `CommitChanges()`
- Сервис ничего не знает о подборе масштаба и не принимает решений о новой раскладке
- Сервис только корректирует `View.Origin` по одной оси

---

## Канонические якоря

### AssemblyDrawing

- Из чертежа получить `GUID` сборки
- По `GUID` найти сборку в модели
- Взять `Assembly.GetMainPart()`
- Использовать СК главной детали как canonical anchor для всех видов

### GADrawing

- Из видов получить общие оси через `get_grid_axes`-эквивалентный API
- Основной ключ сопоставления: `GUID` оси, если он стабилен между видами
- Fallback: `Label + Direction`
- Для выравнивания использовать одну и ту же ось, найденную в сравниваемых видах

---

## Этапы реализации

### Этап 1. Подготовка контекста вида

- Зафиксировать, какие виды участвуют в выравнивании:
  - один `FrontView`
  - `TopView`, если есть
  - один или несколько `SectionView`
- Зафиксировать текущую математику преобразования:
  - local coordinates вида
  - `Origin`
  - `Scale`
  - post-correction рамки вида из `fit_views_to_sheet`
- Определить единый helper для перевода точки:
  - view-local -> sheet
  - sheet delta -> изменение `View.Origin`

Результат этапа:
- есть единое место, где описана математика координат для выравнивания

### Этап 2. AssemblyDrawing alignment

- Получить `GUID` сборки из активного чертежа
- Получить сборку из модели
- Получить `MainPart`
- Выбрать canonical point из СК главной детали:
  - сначала `CoordinateSystem.Origin`
  - при необходимости позже добавить альтернативную точку, но не в первой реализации
- Для каждого целевого вида получить положение той же точки в local coordinates вида
- Перевести точку в sheet coordinates
- Посчитать смещения:
  - `Front <-> Top`: только по `X`
  - `Front <-> Section`: только по `Y`
- Аккуратно применить смещения к зависимым видам, не двигая `FrontView`, если это не требуется

Результат этапа:
- assembly drawing выравнивается по одной детали-якорю

### Этап 3. GADrawing alignment

- Для каждого вида собрать оси
- Найти общую ось между `FrontView` и целевым видом
- Проверить стабильность идентификатора оси:
  - primary: `GUID`
  - fallback: `Label + Direction`
- Для найденной оси вычислить canonical coordinate в local coordinates каждого вида
- Перевести coordinate в sheet space
- Применить смещение по нужной оси:
  - `Front <-> Top`: `X`
  - `Front <-> Section`: `Y`

Результат этапа:
- GA drawing выравнивается по общей оси сетки

### Этап 4. Правила отказа и безопасность

- Если не найден `FrontView`, этап пропускается
- Если не найден canonical anchor, этап пропускается без exception
- Если якорь найден только в одном виде, конкретная связь пропускается
- Если после коррекции вид выходит за usable area листа, не применять смещение либо ограничивать его
- Все причины пропуска писать в диагностику/trace, без падения команды

Результат этапа:
- post-processing не делает `fit_views_to_sheet` хрупким

### Этап 5. Тесты

- Unit tests на математику преобразования координат
- Unit tests на выбор оси выравнивания
- Unit tests на fallback для `GADrawing`
- Smoke-level tests на pipeline:
  - arrange completed
  - projection alignment applied
  - final origins ожидаемы

Результат этапа:
- ключевые ошибки ловятся без ручной проверки в Tekla UI

---

## Предлагаемый порядок файловых изменений

1. `Drawing/Views/TeklaDrawingViewApi.Layout.cs`
2. `Drawing/Views/DrawingProjectionAlignmentService*.cs`
3. `Drawing/Geometry/TeklaDrawingGridApi.cs`, если нужен `GUID` оси
4. модели/DTO только если реально не хватает данных
5. тесты

---

## Критерии готовности

- `fit_views_to_sheet` по-прежнему подбирает scale и раскладывает виды как раньше
- После раскладки появляется дополнительное выравнивание по проекционной связи
- `AssemblyDrawing` работает через `GUID` сборки -> `MainPart`
- `GADrawing` работает через общую ось
- При отсутствии якоря команда не падает
- Изменения локализованы в drawing API слое

---

## Что не делать в первой итерации

- Не добавлять новый MCP tool только для проекционной связи
- Не переносить логику в bridge command layer
- Не переписывать стратегии `Arrange`
- Не поддерживать все возможные типы видов сразу
- Не решать в этой задаче коллизии после post-alignment, если они не критичны

---

## После реализации

- Проверить на реальных `AssemblyDrawing` и `GADrawing`
- Перенести краткий итог в основной `ROADMAP.md`, если потребуется
- Временный файл удалить

---

## Статус реализации и известные проблемы

Всё из этапов 1–4 реализовано и работает в production.

### Баг: перекрытие видов после проекционного сдвига ✅ исправлен

**Симптом:** TopView после `fit_views_to_sheet` полностью перекрывал FrontView.

**Причина:** `TryMoveView` проверял только выход за границы листа и пересечение с reserved areas, но не проверял пересечение с другими видами.

**Исправление** (март 2026):
- `DrawingProjectionAlignmentMath`: добавлен `IntersectsAnyView(ProjectionRect, IReadOnlyList<ProjectionViewState>)` и `Intersects(ProjectionRect, ProjectionRect)`
- `DrawingProjectionAlignmentService.Helpers`: `TryMoveView` получил параметр `otherViewStates`; перед применением сдвига вызывается `IntersectsAnyView` — при коллизии движение отменяется
- `DrawingProjectionAlignmentService.Assembly`: для каждого вида формируется список остальных видов (`others`) и передаётся в `ApplyAssemblyMove`

**Побочный эффект:** если пакер разместил TopView так, что любое выравнивание по X вызывало бы коллизию — проекционная связь не применяется (`projection-skip:view-overlap`). Это корректное поведение: лучше не выравнивать, чем перекрыть.

### Баг: TopView размещался ниже FrontView ✅ исправлен

**Симптом:** TopView после проекционного выравнивания оказывался ниже FrontView, а не выше.

**Причина:** пакер размещал FrontView высоко на листе; выравнивание пыталось сдвинуть TopView выше `(sheetHeight - margin)` — место было занято — и никуда не двигало вид.

**Исправление** (март 2026):
- В `ApplyAssemblyAlignment` перед применением проекционного выравнивания вычисляется необходимое место над FrontView для TopView (`topFrameHeight + ProjectionViewGap`)
- Если места недостаточно — FrontView и SectionView сдвигаются вниз на `shiftDown = needed - available`
- После сдвига `frontAnchorY` пересчитывается, `allStates` перестраивается
- Затем выполняется обычное проекционное выравнивание — TopView оказывается выше FrontView

### Важно: TeklaBridge — персистентный процесс

TeklaBridge запускается с флагом `--loop` (`PersistentBridge`) и живёт между вызовами. **DLL загружается один раз при старте процесса.** После обновления `TeklaMcpServer.Api.dll` на диске нужно убить процесс `TeklaBridge.exe` — иначе работает старый код из памяти.
