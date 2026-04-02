# Роадмап слоя DrawingGeneration

## Цель

`DrawingGeneration` это orchestration-layer между:

- `ViewDefinitions`
- низкоуровневым `Creation`
- `ViewLayout`

Слой отвечает не за отдельный вызов `new Drawing(...)`, а за
**сборку сценария генерации чертежа**.

## Граница ответственности

- `ViewDefinitions`
  - описывает, какие виды и правила хотим получить
- `Creation`
  - низкоуровнево создаёт drawing artifact
- `DrawingGeneration`
  - координирует generation flow
- `ViewLayout`
  - работает с уже существующими видами после их появления

Коротко:

- `ViewDefinitions` = intent
- `Creation` = low-level creation backend
- `DrawingGeneration` = orchestration / builder layer
- `ViewLayout` = runtime layout layer

## Что входит в этот слой

### 1. Generation request

Слой должен принимать единый request, который описывает:

- kind:
  - `Assembly`
  - `Ga`
  - `SinglePart`
- target model object или view name
- drawing properties
- open drawing flag
- нужно ли подтягивать default view preset

### 2. Builder orchestration

Слой должен:

- валидировать request;
- определить, нужен ли `ViewDefinitions` preset;
- делегировать low-level creation в `IDrawingCreationApi`;
- вернуть unified result generation-сценария.

### 3. Unified result

Результат должен уметь выразить:

- success / error;
- warnings;
- low-level drawing creation result;
- resolved view preset, если он был найден;
- generation kind.

## Что пока не входит

На первом этапе сюда **не** входят:

- реальное применение `ViewDefinitions` к runtime view creation;
- handoff в `ViewLayout`;
- orientation / visibility execution;
- layout fit / arrangement;
- geometry-driven view filtering.

То есть первый этап это именно orchestration contract.

## Предлагаемые типы

Минимальный стартовый набор:

- `DrawingGenerationKind`
- `DrawingGenerationRequest`
- `DrawingGenerationResult`
- `IDrawingBuilder`
- `DrawingBuilder`

## Phase 1. Contracts and builder shell

Сделать:

- folder structure;
- request/result types;
- builder interface;
- builder shell поверх `IDrawingCreationApi` и `IViewDefinitionApi`;
- roadmap.

Статус:

- стартовые contracts введены;
- `DrawingBuilder` делегирует в существующий `IDrawingCreationApi`;
- для `Assembly` и `Ga` builder может подтянуть default preset из `ViewDefinitions`;
- runtime-применение preset пока не делается.

## Phase 2. Definition-aware generation

Сделать:

- handoff preset -> creation flow;
- explicit generation stages;
- richer warnings/diagnostics;
- separation between requested and resolved generation plan.

## Phase 3. ViewLayout integration

Сделать:

- integration points с `ViewLayout`;
- generation result enrichment actual-view info;
- optional post-create layout step.

## Acceptance criteria

Слой считается полезно введённым, когда:

- `Creation` остаётся узким low-level backend;
- high-level drawing scenario можно выразить единым request;
- `ViewDefinitions` не смешивается с `ViewLayout`;
- builder становится естественной точкой входа для future orchestration.
