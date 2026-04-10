# Роадмап слоя Drawing

## Цель

Слой `Drawing` должен стать общей архитектурной рамкой для работы с чертежом.

Не как набор несвязанных API по видам, размерам и меткам, а как система из
нескольких уровней контекста:

- контекст чертежа
- контекст вида
- специализированные annotation/domain-модули

Ключевая цель:

- ввести явный `DrawingContext` как coarse sheet-level model
- сохранить `DrawingViewContext` как detailed per-view model
- использовать эти уровни повторно в:
  - `ViewLayout`
  - `Dimensions`
  - `Marks`
  - `TableLayout`

## Главный принцип

Нужно различать два уровня:

### 1. `DrawingContext`

Это **грубый контекст всего чертежа**.

Он нужен для:

- компоновки видов на листе
- анализа sheet-level ограничений
- table/title-block layout
- coarse planning

Он **не должен** содержать:

- детальную геометрию деталей внутри вида
- болты/детали/узлы по каждому виду
- dimension/mark geometry

### 2. `DrawingViewContext`

Это **детальный контекст одного вида**.

Он нужен для:

- размеров
- меток
- локальной геометрии вида
- reasoning внутри конкретного view

Он может содержать:

- part geometry
- bolts
- `PartsBounds`
- `PartsHull`
- view-local warnings

Коротко:

- `DrawingContext` = sheet-level coarse context
- `DrawingViewContext` = per-view detailed context

## Почему это нужно

Сейчас в проекте уже есть:

- сильный `ViewLayout`
- сильный `Dimensions`
- `TableLayout` / reserved areas logic
- появившийся `DrawingViewContext`

Но пока нет явной общей модели самого чертежа как sheet-level сущности.

Из-за этого:

- layout-код опирается на runtime/result models, но не на явный coarse context
- table/reserved-area logic не оформлена как часть общего drawing context
- нет одной понятной точки входа для sheet-level reasoning

## Целевая модель

Нужны три уровня.

### 1. `DrawingContext`

Состав:

- `Drawing`
- `Sheet`
- `Views`
- `ReservedLayout`
- `Warnings`

`Views` здесь должны быть только в грубом формате:

- `ViewId`
- `ViewType`
- `Scale`
- frame / bbox
- позиция на листе
- sheet-local size

Без внутренней геометрии вида.

### 2. `DrawingViewContext`

Отдельный уровень detailed geometry для одного view.

Минимально:

- `ViewId`
- `ViewScale`
- `Parts`
- `Bolts`
- `PartsBounds`
- `PartsHull`
- `GridIds`
- `Warnings`

### 3. Domain contexts

Это специализированные уровни поверх drawing/view contexts:

- `DimensionContext`
- future mark context
- future section/detail reasoning context

## Граница ответственности модулей

### `DrawingContext`

Отвечает за:

- sheet-level факты
- coarse geometry видов
- reserved areas
- table/title block zones

Не отвечает за:

- детальную геометрию одного вида
- dimensions
- marks

### `DrawingViewContext`

Отвечает за:

- локальную геометрию одного вида
- окружение annotations внутри вида

Не отвечает за:

- весь лист
- sheet-level layout между видами

### `ViewLayout`

Должен работать в первую очередь поверх:

- `DrawingContext`

А не собирать sheet-level модель неявно по кускам.

### `Dimensions`

Должны использовать:

- `DrawingViewContext`
- `DimensionContext`

### `Marks`

Должны в будущем использовать:

- `DrawingViewContext`
- future mark-specific context

### `TableLayout`

Должен стать частью sheet-level drawing context, а не только локальным helper'ом.

## Что уже есть

Уже есть хорошая база:

- `DrawingViewContext`
- `DrawingViewContextBuilder`
- `get_drawing_view_context`
- `get_dimension_contexts`
- `ViewLayout` с развитой layout-логикой
- `DrawingReservedAreaReader`
- `DrawingContext`
- `DrawingLayoutContextBuilder`
- `get_drawing_layout_context`
- `DrawingLayoutScorer`
- `DrawingCaseSnapshotWriter`
- `DrawingCaseCaptureService`

То есть базовый drawing-level слой уже введён:

- coarse `DrawingContext` есть
- `ViewLayout` уже читает `DrawingContext`
- before/after snapshot pipeline для drawing layout уже собран

## Phase 1. Ввести `DrawingContext`

Статус: выполнено.

Нужно сделать:

- новую model/read layer для `DrawingContext`
- builder для sheet-level context
- coarse `ViewInfo` внутри drawing context
- sheet info
- warnings

Важно:

- не тащить туда внутреннюю геометрию вида
- не превращать его в dump всего drawing runtime

## Phase 2. Добавить `TableLayout` / reserved areas

Статус: выполнено.

Нужно включить в `DrawingContext`:

- reserved rects
- title block / table zones
- итоговые blocked areas для layout

Это сделает `DrawingContext` полезным для реального `ViewLayout`.

## Phase 3. Добавить coarse view geometry

Статус: выполнено.

В `DrawingContext.Views` нужно зафиксировать:

- текущий frame/bbox вида
- sheet position
- width/height
- scale/type

Это должно стать каноническим coarse source of truth для компоновки видов.

## Phase 4. Перевести `ViewLayout` на `DrawingContext`

Статус: выполнено для `fit_views_to_sheet`.

Дальше `ViewLayout` должен использовать:

- `DrawingContext`

как явный входной coarse context для:

- fit
- packing
- centering
- reserved-area avoidance

## Phase 5. Использовать те же уровни в других модулях

Статус: начато.

После этого архитектура должна выглядеть так:

- `DrawingContext` -> sheet-level planning
- `DrawingViewContext` -> per-view geometry
- domain contexts -> dimensions / marks / future annotation logic

## Phase 6. Сохранять кейсы как before/after contexts

### 6a. Сохранение снэпшотов

Статус: базовый pipeline собран.

Следующий практический слой после самого `DrawingContext`:

- использовать один и тот же `get_drawing_layout_context`
- читать состояние чертежа до изменений
- читать состояние чертежа после изменений
- сохранять это как пример трансформации

Минимальный формат кейса:

- папка кейса
- `before.json`
- `after.json`
- `meta.json`
- `views/` (опционально, для per-view contexts)

Где:

- `before.json` = `DrawingContext` до layout/manual edits
- `after.json` = `DrawingContext` после layout/manual edits
- `meta.json` = краткое описание кейса + оценка качества
- `views/` = дополнительные данные по отдельным видам, если кейс расширяется контекстами размеров/меток

Структура dataset:

- `drawing_cases/`
  - `assembly/`
    - `{drawing_guid}/`
  - `ga/`
    - `{drawing_guid}/`
  - `single_part/`
    - `{drawing_guid}/`
  - `cast_element/`
    - `{drawing_guid}/`

Идентификатор кейса:

- использовать существующий `drawing_guid`
- это соответствует уже существующей семантике `ListDrawings()` / drawing catalog API
- `drawing_guid` является каноническим id кейса чертежа

Минимальный `meta.json`:

- `drawing_guid`
- `drawing_type`
- `drawing_name`
- `operation`
- `note`
- `score_before`
- `score_after`

Важно:

- не нужен отдельный synthetic `layout_result.json`
- канонический результат кейса это новый `DrawingContext` после изменений
- для dataset/agent examples нужно хранить именно пару состояний:
  - before
  - after

Реализация:

- `DrawingCaseSnapshotWriter` — пишет `before.json` / `after.json` / `meta.json`
- `DrawingCaseCaptureService`:
  - читает текущий `DrawingContext`
  - считает `score_before` / `score_after`
  - сохраняет кейс через writer

Важно:

- операция layout/fit выполняется снаружи
- `DrawingCaseCaptureService` не должен сам вызывать `fit_views_to_sheet`

Per-view расширение:

- размеры и метки остаются view-level данными
- они не включаются в `DrawingContext`
- если кейс потом расширяется:
  - использовать подпапку `views/`
  - внутри хранить per-view context по `view_id`

### 6b. Оценка качества компоновки

Статус: минимальный scorer реализован.

`score_drawing_layout` — инструмент который принимает `DrawingContext` и возвращает числовую оценку компоновки.

Критерии (все считаются из `DrawingContext`):

- **Заполнение листа** — `суммарная площадь видов / доступная площадь листа`
- **Единый масштаб** — штраф за разнобой масштабов между видами
- **Проекционная связь** — количество корректных проекционных связей по drawing-type-specific rules:
  - `GA` — через grid / оси
  - `assembly` / `SK` — от системы координат главной детали
- **Hard constraints** — перекрытия видов между собой или с таблицами (штраф)

Первый practical scope scorer:

- overlap между видами
- overlap с `ReservedLayout`
- `fill_ratio`
- `uniform_scale`

Правила расчёта:

- для overlap использовать `BBox` вида как основной rect
- если `BBox` недоступен, использовать fallback `Origin + Width/Height`
- `fill_ratio` считать по формуле:
  - `sum(view area) / available sheet area`
- `available sheet area` считать как:
  - `sheet area - union(ReservedLayout.Areas)`
- для `ReservedLayout.Areas` нельзя использовать простую сумму площадей, если rect'ы перекрываются
- `uniform_scale` считать без `detail views`
- для этого использовать `SemanticKind` вида

Нормализация и веса:

- дефолтные веса должны быть заданы явно с первого запуска
- пока не будет отдельной target policy, не вводить самостоятельный `scale_score`
- первый scorer не должен допускать, чтобы один ненормализованный scale-сигнал доминировал над остальными

Расширенная формула:

```
score = w1 * fill_ratio
      + w2 * uniform_scale_score
      + w3 * projection_score
      - penalty(overlaps)
```

Важно:

- `projection_score` не должен считаться как универсальная эвристика только по bbox
- правила должны зависеть от типа чертежа и доступных sheet-level signals в `DrawingContext`
- `projection_score` не входит в первый минимальный scorer и добавляется отдельным шагом

Веса подбираются на реальных кейсах.

Зачем это нужно:

- детерминированный алгоритм и агентный результат оцениваются по одной метрике
- before/after автоматически получает числовую метрику без ручной оценки
- агент может сравнивать варианты и выбирать лучший

## Следующий этап

После стабилизации drawing-level слоя следующий целевой consumer:

- marks

Направление:

- сохранить тот же паттерн:
  - context
  - scorer/evaluator
  - snapshot writer
  - capture service
- начать с view-level mark context
- не вводить общий интерфейс заранее

## Что не нужно делать

Не нужно:

- смешивать `DrawingContext` и `DrawingViewContext`
- складывать в `DrawingContext` всю геометрию деталей всех видов
- делать `DrawingContext` вторым `DrawingViewContext`
- превращать `DrawingContext` в transport dump без ясной роли

## Acceptance criteria

Работа считается успешной, когда:

1. Есть явный `DrawingContext` как coarse sheet-level model.
2. В нём есть:
   - sheet
   - coarse views
   - reserved areas / table layout
3. `DrawingViewContext` остаётся отдельным detailed per-view model.
4. `ViewLayout` может использовать `DrawingContext` как естественный вход.
5. Нет смешения:
   - sheet-level context
   - per-view detailed context
   - annotation/domain contexts

## Итоговая архитектурная формула

- `DrawingContext` = coarse context чертежа
- `DrawingViewContext` = detailed context вида
- `DimensionContext` / future mark context = domain-specific context

Именно такое разделение должно стать базой дальнейшей работы по `Drawing`.
