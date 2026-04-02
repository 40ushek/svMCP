# Роадмап слоя MarkDefinitions

## Цель

`MarkDefinitions` отвечает не за query, overlap resolution или low-level create
операции над марками, а за **определение набора марок и правил их постановки**.

Это слой про intent:

- какие mark scenarios нужны;
- для каких drawing targets их строить;
- какие placement rules использовать;
- какой content и style preset применять;
- как выразить mark intent для `Assembly` и `GA` без привязки к runtime.

## Граница с Marks

- `MarkDefinitions`
  - описывает **какие marks хотим получить**
  - задаёт scenarios, target kinds, placement/content/style policies
  - может быть использован для `Assembly`, `GA` и later других scopes
- `Marks`
  - работает с **уже существующими или создаваемыми runtime marks**
  - отвечает за query, create, delete, content rewrite, overlap resolution,
    arrangement и geometry/debug

Коротко:

- `MarkDefinitions` = desired mark set
- `Marks` = runtime operations and analysis for actual marks

## Что входит в этот слой

### 1. Scope и scenario definition

Слой должен уметь описывать:

- drawing scope:
  - `Assembly`
  - `Ga`
- mark scenarios:
  - `PartMark`
  - `BoltMark`
  - later `AssemblyMark`

Это не runtime `Mark`, а definition-level request.

### 2. Target-driven mark intent

Слой должен явно хранить, для каких targets нужны marks:

- `Part`
- `Bolt`
- `Assembly`

Это отражает legacy-подход:

- отдельные policies для part marks и bolt marks;
- mark intent задаётся от target family, а не от raw drawing object.

### 3. Placement policy

Слой должен задавать semantic placement rules:

- `Auto`
- `Inside`
- `Outside`
- `LeaderLine`

И дополнительные intent flags:

- `PreferOutsideContour`
- `AllowLeaderLine`
- `AllowInsidePlacement`

Это отражает legacy-идеи:

- outside contour placement;
- inside placement where valid;
- leader-line fallback.

### 4. Content policy

Слой должен задавать definition-level mark content:

- список property attributes;
- later content templates or preset elements.

### 5. Style policy

Слой должен задавать definition-level styling:

- mark attributes file;
- frame type;
- arrowhead type.

## Что пока не входит

На первом этапе сюда **не** входят:

- query существующих marks;
- overlap resolution;
- runtime repositioning;
- layout collision solving;
- actual Tekla mark creation calls;
- exact mark geometry analysis.

То есть этот модуль не должен дублировать `Marks`.

## Предлагаемые типы

Минимальный первый набор:

- `DrawingMarkDefinitionScope`
- `DrawingMarkTargetKind`
- `DrawingMarkScenarioKind`
- `DrawingMarkPlacementMode`
- `DrawingMarkPlacementPolicy`
- `DrawingMarkContentPolicy`
- `DrawingMarkStylePolicy`
- `DrawingMarkDefinition`
- `DrawingMarkDefinitionSet`
- `DrawingMarkPreset`
- `GetMarkDefinitionPresetResult`

Потом API:

- `IMarkDefinitionApi`
- `TeklaMarkDefinitionApi`

## Целевой сценарий использования

Потребитель должен уметь сказать примерно так:

- для `Assembly` нужны `PartMark`;
- `BoltMark` ветка пока выключена;
- marks предпочтительно ставить outside contour;
- если outside placement не проходит, разрешить leader line;
- content = `PART_POS`;
- style = `standard`.

И уже потом другой слой решает:

- как найти actual drawing targets;
- как создать runtime marks;
- как применить content/style;
- как потом раскладывать и разрешать overlap.

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
  - `PartMark` enabled
  - `BoltMark` disabled by default
  - outside-contour preference
  - leader-line fallback enabled
  - content baseline = `PART_POS`
  - style baseline = `standard`

### Phase 2. Definition builders

Сделать:

- assembly preset builder;
- ga preset builder;
- mapping preset -> definition set;
- initial target-specific defaults by scope.

### Phase 3. Geometry integration contracts

Сделать:

- bridge from `MarkDefinitions` to geometry providers;
- explicit mapping:
  - target -> geometry/anchor provider
  - placement mode -> placement strategy
- separation between requested and resolved placement plan.

### Phase 4. Runtime Marks integration

Сделать:

- bridge from `MarkDefinitions` to `Marks`;
- apply preset during mark generation flow;
- handoff to overlap/layout logic only after actual marks exist.

### Phase 5. Config/persistence

Опционально позже:

- load/save presets;
- profile-based preset registry;
- user-defined preset sets.

## Acceptance criteria

Слой считается полезно введённым, когда:

- `MarkDefinitions` и `Marks` не пересекаются по ответственности;
- `Assembly` и `GA` могут использовать одну contract family;
- placement/content/style intent задаются без прямой привязки к runtime mark DTO;
- preset object можно читать как mark intent без знания overlap/layout логики.
