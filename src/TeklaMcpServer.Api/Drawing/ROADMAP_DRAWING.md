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

Минимальный состав:

- drawing identity / drawing type
- sheet info
- coarse list of views
- reserved areas / table layout
- warnings

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

То есть detailed per-view путь уже появился.

Следующий естественный шаг:

- ввести coarse `DrawingContext`

## Phase 1. Ввести `DrawingContext`

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

Нужно включить в `DrawingContext`:

- reserved rects
- title block / table zones
- итоговые blocked areas для layout

Это сделает `DrawingContext` полезным для реального `ViewLayout`.

## Phase 3. Добавить coarse view geometry

В `DrawingContext.Views` нужно зафиксировать:

- текущий frame/bbox вида
- sheet position
- width/height
- scale/type

Это должно стать каноническим coarse source of truth для компоновки видов.

## Phase 4. Перевести `ViewLayout` на `DrawingContext`

Дальше `ViewLayout` должен использовать:

- `DrawingContext`

как явный входной coarse context для:

- fit
- packing
- centering
- reserved-area avoidance

## Phase 5. Использовать те же уровни в других модулях

После этого архитектура должна выглядеть так:

- `DrawingContext` -> sheet-level planning
- `DrawingViewContext` -> per-view geometry
- domain contexts -> dimensions / marks / future annotation logic

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
