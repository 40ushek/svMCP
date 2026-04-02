# Роадмап слоя SectionDefinitions

## Цель

`SectionDefinitions` отвечает не за runtime query секций, section-mark geometry
или layout уже существующих section views, а за **определение набора сечений и
правил их формирования**.

Это слой про intent:

- какие section scenarios нужны;
- как задаётся направление и символика сечения;
- какие naming и merge rules применить;
- какие scale/style presets использовать для cut view и cut symbol;
- как выразить section intent для `Assembly` и `GA` без привязки к runtime.

## Граница с ViewLayout

- `SectionDefinitions`
  - описывает **какие sections хотим получить**
  - задаёт section scenarios, symbol direction, naming, merge и style policies
- `ViewLayout`
  - работает с уже существующими section views и section marks
  - отвечает за query, placement-side resolution, alignment, scale/layout
    exceptions и section-related runtime arrangement

Коротко:

- `SectionDefinitions` = desired section set
- `ViewLayout` = runtime handling of actual section views and marks

## Legacy baseline from x_drawer

В качестве baseline используются явно наблюдаемые legacy-настройки:

- `Section extension by along axis`
- `Section symbol direction`
- `Section name`
- `Maximum distance between sections, when merging`
- `Set cut symbol for identical sections`
- `SectionScale = 10`
- `FileAttr_CutView = standard`
- `FileAttr_CutViewSymbol = standard`

Это не полный runtime engine, а исходный vocabulary definition-layer.

## Что входит в этот слой

### 1. Scope и scenario definition

Слой должен уметь описывать:

- drawing scope:
  - `Assembly`
  - `Ga`
- section scenarios:
  - `AlongAxis`
  - `AcrossAxis`

### 2. Symbol and naming policy

Слой должен задавать:

- section symbol direction;
- base name policy;
- reuse identical section symbol for same sections.

### 3. Merge policy

Слой должен выражать section merge intent:

- merge similar sections on/off;
- maximum merge distance.

### 4. Style policy

Слой должен задавать definition-level style:

- section scale;
- cut view attributes file;
- cut symbol attributes file.

## Что пока не входит

На первом этапе сюда **не** входят:

- actual section creation calls;
- runtime section-mark query;
- placement-side resolution;
- section view alignment;
- detail-like section handling;
- section layout exceptions.

То есть этот модуль не должен дублировать `ViewLayout`.

## Предлагаемые типы

Минимальный первый набор:

- `DrawingSectionDefinitionScope`
- `DrawingSectionScenarioKind`
- `DrawingSectionSymbolDirection`
- `DrawingSectionNamingPolicy`
- `DrawingSectionMergePolicy`
- `DrawingSectionStylePolicy`
- `DrawingSectionDefinition`
- `DrawingSectionDefinitionSet`
- `DrawingSectionPreset`
- `GetSectionDefinitionPresetResult`

Потом API:

- `ISectionDefinitionApi`
- `TeklaSectionDefinitionApi`

## Целевой сценарий использования

Потребитель должен уметь сказать примерно так:

- для `Assembly` нужны along-axis sections;
- section scale = `1:10`;
- cut view attrs = `standard`;
- cut symbol attrs = `standard`;
- symbol direction = `Auto`;
- merge similar sections пока выключен.

И уже потом другой слой решает:

- как создавать runtime section views;
- как создавать или читать section marks;
- как применять placement/alignment/layout semantics.

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
- стартовый assembly preset использует legacy baseline:
  - `AlongAxis` and `AcrossAxis` scenarios
  - both disabled by default
  - `SectionScale = 10`
  - `CutView = standard`
  - `CutViewSymbol = standard`
  - symbol direction = `Auto`
  - merge disabled by default
  - identical section symbol reuse disabled by default

### Phase 2. Definition builders

Сделать:

- assembly preset builder;
- ga preset builder;
- mapping preset -> definition set;
- initial scenario defaults by scope.

### Phase 3. Runtime integration contracts

Сделать:

- bridge from `SectionDefinitions` to runtime section generation;
- bridge to `ViewLayout` for post-create handling;
- separation between requested and resolved section plan.

## Acceptance criteria

Слой считается полезно введённым, когда:

- `SectionDefinitions` и `ViewLayout` не пересекаются по ответственности;
- `Assembly` и `GA` могут использовать одну contract family;
- section naming/style/merge intent задаются без прямой привязки к runtime
  section DTO;
- preset object можно читать как section intent без знания section layout logic.
