# Роадмап слоя DimensionDefinitions

## Цель

`DimensionDefinitions` отвечает не за query, arrange или low-level create
операции над размерами, а за **определение набора размеров и правил их
построения**.

Это слой про intent:

- какие dimension scenarios нужны;
- от каких geometry sources их строить;
- какие point policies использовать;
- какие placement defaults и preset rules применить;
- как выразить dimension intent для `Assembly` и `GA` без привязки к runtime.

## Граница с Dimensions

- `DimensionDefinitions`
  - описывает **какие размеры хотим получить**
  - задаёт scenarios, sources, point policies и placement defaults
  - может быть использован для `Assembly`, `GA` и later других scopes
- `Dimensions`
  - работает с **уже существующими или создаваемыми runtime dimensions**
  - отвечает за query, create, move, delete, grouping, reduction, arrangement,
    debug и control-diagonal operations

Коротко:

- `DimensionDefinitions` = desired dimension set
- `Dimensions` = runtime operations and analysis for actual dimensions

## Что входит в этот слой

### 1. Scope и scenario definition

Слой должен уметь описывать:

- drawing scope:
  - `Assembly`
  - `Ga`
- dimension scenarios:
  - `Overall`
  - `Part`
  - `Assembly`
  - `Node`
  - `Bolt`
  - `ControlDiagonal`
  - later `Section`

Это не runtime dimension set, а definition-level request.

### 2. Source-driven dimension intent

Слой должен явно хранить и объяснять, от каких источников строятся размеры:

- `Axis`
- `Part`
- `Assembly`
- `Node`
- `Bolt`
- `Grid`

Это отражает legacy-подход:

- dimensioning строится не от абстрактного drawing object,
  а от выбранного geometry source.

### 3. Point policy

Слой должен задавать, какой тип точек использовать:

- characteristic points;
- extreme points;
- bolt points;
- work points.

Важно:

- это не сами точки;
- точки уже приходят из `Geometry/*`;
- этот слой только определяет intent их использования.

### 4. Placement defaults

Слой должен задавать definition-level параметры:

- default distance;
- direction hint;
- attributes file.

Важно:

- это ещё не runtime placement engine;
- это входные правила для future generation / runtime dimension creation.

### 5. Preset / profile layer

Слой должен поддерживать reusable presets:

- assembly-oriented preset;
- ga-oriented preset;
- compact preset;
- full-detail preset;
- later custom presets from config/storage.

Preset должен быть агрегатом:

- scope;
- scenarios;
- sources;
- point policies;
- placement defaults.

## Что пока не входит

На первом этапе сюда **не** входят:

- query существующих размеров;
- runtime grouping/reduction;
- actual Tekla merge/combine;
- actual create/move/delete calls;
- arrangement;
- text geometry debug.

То есть этот модуль не должен дублировать `Dimensions`.

## Предлагаемые типы

Минимальный первый набор:

- `DrawingDimensionDefinitionScope`
- `DrawingDimensionSourceKind`
- `DrawingDimensionScenarioKind`
- `DrawingDimensionPlacementPolicy`
- `DrawingDimensionPointPolicy`
- `DrawingDimensionDefinition`
- `DrawingDimensionDefinitionSet`
- `DrawingDimensionPreset`
- `GetDimensionDefinitionPresetResult`

Потом API:

- `IDimensionDefinitionApi`
- `TeklaDimensionDefinitionApi`

## Целевой сценарий использования

Потребитель должен уметь сказать примерно так:

- для `Assembly` нужны `Overall + Assembly` размеры;
- source брать из `Axis + Assembly`;
- использовать characteristic points и extremes;
- `Bolt` и `Node` ветки пока выключены;
- default distance = `10`;
- control diagonals как отдельный scenario по `Assembly` extremes.

И уже потом другой слой решает:

- как собрать нужные точки;
- как превратить definition в `PointList`;
- как вызвать runtime create operation в `Dimensions`;
- как потом разложить и проанализировать actual dimension sets.

## Этапы

### Phase 1. Contracts and presets

Сделать:

- folder structure;
- definition-layer models;
- базовые enums;
- первый preset object;
- roadmap и naming freeze.

Статус:

- folder создан;
- базовые contracts введены;
- первый preset API введён;
- стартовый assembly preset использует legacy-inspired baseline:
  - `Overall` scenario
  - `Assembly` scenario
  - `Node` branch disabled by default
  - `Bolt` branch disabled by default
  - `ControlDiagonal` disabled by default
  - source-driven setup around `Axis`, `Assembly`, `Part`, `Node`, `Bolt`
  - characteristic/extreme/work/bolt point policies expressed separately

### Phase 2. Definition builders

Сделать:

- assembly preset builder;
- ga preset builder;
- mapping preset -> definition set;
- initial scenario defaults by scope.

### Phase 3. Geometry integration contracts

Сделать:

- bridge from `DimensionDefinitions` to geometry point providers;
- explicit mapping:
  - source -> point provider
  - scenario -> point list strategy
- separation between requested and resolved point plan.

### Phase 4. Runtime Dimensions integration

Сделать:

- bridge from `DimensionDefinitions` to `Dimensions`;
- apply preset during dimension generation flow;
- handoff to grouping/arrangement only after actual dimensions exist.

### Phase 5. Config/persistence

Опционально позже:

- load/save presets;
- profile-based preset registry;
- user-defined preset sets.

## Acceptance criteria

Слой считается полезно введённым, когда:

- `DimensionDefinitions` и `Dimensions` не пересекаются по ответственности;
- `Assembly` и `GA` могут использовать одну contract family;
- source-driven dimension intent задаётся без прямой привязки к runtime DTO;
- preset object можно читать как dimension intent без знания внутренней
  grouping/arrangement логики.
