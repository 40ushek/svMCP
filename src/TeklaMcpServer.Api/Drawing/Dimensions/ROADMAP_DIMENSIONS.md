# Dimensions Roadmap

## Goal

Rebuild `Drawing/Dimensions` around the domain model already proven in
`D:\repos\svMCP\dim`.

For `Dimensions`, the legacy `dim` project is the canonical source for:

- domain vocabulary
- geometry invariants
- grouping semantics
- line-first arrangement logic

The current `src` implementation should adapt Tekla Open API data into that
model, not invent a parallel model around API DTOs.

## Document Split

This roadmap is the strategic document for the module.

It should answer:

- what the target dimension architecture is
- what still remains to align with `dim`
- what functionality is intentionally deferred

The operational/current-state description lives in
[README.md](D:\repos\svMCP\src\TeklaMcpServer.Api\Drawing\Dimensions\README.md).

That file should answer:

- how the folder is structured today
- which tools are public today
- which APIs/debug surfaces are internal-only today

## Module Shape Today

The folder is now intentionally split into:

- root facade/API files
- `Grouping/`
- `Arrangement/`
- `Placement/`

Root facade/API files:

- `TeklaDrawingDimensionsApi.cs`
- `TeklaDrawingDimensionsApi.Query.cs`
- `TeklaDrawingDimensionsApi.Commands.cs`
- `TeklaDrawingDimensionsApi.Arrangement.cs`
- `IDrawingDimensionsApi.cs`

Current intent of the split:

- `Grouping` owns `DimensionItem` / `DimensionGroup` and reduction logic
- `Arrangement` owns spacing analysis and `Distance` adjustment planning
- `Placement` owns projection, create/diagonal placement, text placement and
  text formatting helpers

## Canonical Domain Model

The target internal model should follow `dim` and be centered on:

- `DimensionItem`
- `DimensionGroup`
- `DimensionOperations`

The core geometry/state of a dimension item is:

- `DimensionType`
- `Direction`
- `TopDirection`
- `LeadLineMain`
- `LeadLineSecond`
- `PointList`
- `StartPoint`
- `EndPoint`
- `CenterPoint`
- `LengthList`
- `RealLengthList`

The core geometry/state of a dimension group is:

- `DimensionType`
- shared `Direction`
- shared/compatible `TopDirection`
- compatible lead-line geometry
- `MaximumDistance`
- ordered `DimensionList`

This is the model to preserve and extend.

## Architectural Decision

`DrawingDimensionInfo` and related read DTOs are public transport contracts.
They are not the domain model.

Target layering:

`Tekla Open API snapshot -> DimensionItem/DimensionGroup domain -> arrangement/move/debug -> public DTOs/tools`

Not acceptable as the long-term center of the module:

- DTO-first modeling
- bbox-first grouping
- orientation-first grouping
- public semantics derived mainly from convenience summaries

## Current State

Publicly supported today:

- `get_drawing_dimensions`
- `arrange_dimensions`
- `combine_dimensions`
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`
- `draw_dimension_text_boxes`

Bridge/internal debug helpers currently available:

- `get_dimension_text_placement_debug`
- `get_dimension_source_debug`
- `get_dimension_groups_debug`
- `get_dimension_arrangement_debug`

Not currently exposed as public MCP tools:

- arrangement debug
- source/group/text-placement debug helpers

These helpers are for live validation only. They do not define the long-term
domain model.

Implemented in the current `src` code:

- root/facade vs `Grouping` / `Arrangement` / `Placement` split is done
- internal `DimensionItem` / `DimensionGroup` model exists
- `get_drawing_dimensions` returns the real line-based groups, not summary
  buckets
- grouping is geometry-first and line-first
- arrangement layer exists as a post-processing pipeline for already-created
  dimensions
- placement helper layer exists for:
  - projection math
  - text placement
  - create-dimension placement
  - control-diagonal placement
  - text value formatting via temporary Tekla dimensions
  - mapping dimension attributes to synthetic `Text.TextAttributes`
- first `DimensionOperations`-style reduction step exists:
  - simple redundant items can be rejected when a more informative item in the
    same group already covers the same span
- exact duplicate reduction for simple items exists
- packet-based representative selection exists for nearby items inside one group
- reduction debug now explains what happened to each item:
  - raw group
  - reduced group
  - per-item decision and reason
  - representative packets
- packet debug now also exposes conservative combine-candidate analysis:
  - packet members
  - whether the packet is a potential combination candidate
  - blocking reasons when it is not
  - current combine policy can already block by:
    - source-kind mismatch
    - reference-line band mismatch
    - excessive distance delta
    - free-dimension prohibition
    - adjacent-order fallback prohibition
- controlled Tekla dimension merging now exists as a separate conservative
  runtime action through `combine_dimensions`
- current runtime combine action is now driven by `group.CombineCandidates`,
  while representative packets remain a separate reduction/debug concept

## Confirmed Findings

Confirmed on the current implementation and live drawings:

- `viewScale` is read correctly from the owning view
- paper-gap semantics are valid:
  - paper gap in
  - drawing gap via `viewScale`
- current public default for `arrange_dimensions` is `10 mm` paper gap
- `arrange_dimensions` live validated on real drawings:
  - idempotent: second run with same targetGap produces `appliedCount: 0`
  - push works: lines too close are moved outward
  - pull works: lines too far apart are moved inward toward target gap
- negative `Distance` values occur on real drawings (observed: `distance=-280.467`
  on a Horizontal/Relative dimension); sign semantics for
  negative-distance dimensions remains a risk area for future policy/layout work
- line-based grouping/spacing foundation already exists

Confirmed limitation for native dimension value text:

- native dimension text can be moved manually in Tekla
- the moved text point is not currently observable through the validated Tekla
  Open API surface we have checked so far
- checked sources:
  - `StraightDimension.GetRelatedObjects()`
  - `StraightDimensionSet.GetRelatedObjects()`
  - recursive `GetObjects()` traversal where available
  - drawing presentation model text primitives
  - reflected public/nonpublic members on `StraightDimension`,
    `StraightDimensionSet` and related attributes

Consequence:

- text polygon debug may use runtime text geometry when Tekla exposes it
- otherwise text geometry remains synthetic fallback
- this limitation must not distort the main domain redesign

## Design Principles

- `dim` is the canonical domain reference.
- Prefer porting domain logic from `dim` over inventing new abstractions.
- Keep Tekla API adaptation separate from domain semantics.
- Keep debug geometry separate from arrangement semantics.
- Prefer line geometry over bbox for grouping and spacing.
- Treat `orientation` as a summary only, never as the main grouping key.
- Keep Tekla raw type and domain type separate when they diverge.
- Keep grouping, elimination and future merge rules configurable through
  explicit policies instead of hard-coding one permanent formula.

## Group Semantics

`DimensionGroup` should mean a compatible geometric family or cluster of
dimensions, not a filter and not an automatic merge target.

The intent of grouping is to make it possible to:

- cluster similar dimensions into one geometric working set
- analyze similar/neighboring dimensions together
- detect when some dimensions are redundant and may be rejected
- detect when dimensions are compatible candidates for controlled combination
- drive spacing, arrangement and conflict analysis from shared geometry

Grouping must not imply:

- immediate merge into one Tekla dimension set
- loss of the original individual dimensions
- summary bucketing by `Horizontal` / `Vertical` / `Free` as the main domain
  model

In other words:

- cluster first into a geometric family for analysis and operations
- combine only when a separate rule explicitly allows it
- keep the `dim` meaning of a group as a geometric working set

## Target Internal Types

### 1. Raw Snapshot Layer

Purpose:

- read Tekla API safely
- normalize runtime data into stable internal snapshots

Examples:

- `TeklaDimensionSetSnapshot`
- `TeklaDimensionSegmentSnapshot`

This layer may contain:

- Tekla ids
- view metadata
- raw measured points
- raw distance
- raw Tekla dimension type
- raw text metadata if available

### 2. Domain Layer

Purpose:

- represent dimensions the way `dim` does

Canonical internal entities:

- `DimensionItem`
- `DimensionGroup`

Rules:

- `DimensionItem` is the main logical unit
- a logical item may represent one segment or a chained dimension sequence
- grouping must operate on `DimensionItem`, not directly on transport DTOs

### 3. Operations Layer

Purpose:

- pure domain operations over items/groups

Canonical direction:

- port/translate `DimensionOperations` ideas from `dim`

Examples:

- grouping
- elimination / rejection of redundant items
- alignment
- combination
- play adjustment
- diagonal/control selection

### 4. Policy Layer

Purpose:

- make grouping and reduction rules tunable without rewriting the domain model

Canonical direction:

- add explicit policies for grouping, elimination and later combination

Examples:

- `DimensionGroupingPolicy`
- `DimensionReductionPolicy`
- later `DimensionMergePolicy`

### 5. Public API Layer

Purpose:

- expose stable MCP-facing read/write contracts

Rules:

- DTOs are projections from the domain model
- DTO structure must not dictate domain structure

## Domain Semantics To Preserve From `dim`

The following semantics are the main migration target:

- group by `DimensionType`
- group by parallel `Direction`
- require compatible `TopDirection`
- use `LeadLineMain` / `LeadLineSecond` as core placement geometry
- preserve `LengthList` and `RealLengthList` distinction
- support center-point based behavior where needed
- keep `MaximumDistance` as a real geometric concept, not just a display metric
- treat a group as a candidate set for analysis, elimination and optional
  controlled combination
- keep merge decisions separate from grouping decisions
- allow grouping and elimination tolerances to be policy-driven

## Explicit Non-Goals

Do not center the redesign on:

- `Bounds`
- `TextBounds`
- `Orientation`
- `Absolute` / `Relative` / `RelativeAndAbsolute` as the main domain taxonomy

Those may remain useful, but only as:

- Tekla metadata
- summaries
- fallbacks
- debug aids

## Phases

### Phase 1: Make `dim` the Explicit Canonical Model

Status: done.

Done when:

- roadmap and code comments clearly state that `dim` is the canonical domain
  reference
- target internal entities are explicitly defined around `DimensionItem` /
  `DimensionGroup`
- current DTO-first compromises are documented as temporary

### Phase 2: Introduce a Real `DimensionItem`

Status: done in first form.

Implement an internal `DimensionItem` modeled after `dim`.

It should carry at least:

- ids
- `DimensionType`
- `Direction`
- `TopDirection`
- `LeadLineMain`
- `LeadLineSecond`
- `PointList`
- `LengthList`
- `RealLengthList`
- `CenterPoint`
- source snapshot reference if needed

Done when:

- grouping no longer depends on `DimensionGroupMember` as the primary internal
  concept
- Tekla snapshot data can be projected into `DimensionItem`
- current status:
  - `DimensionItem` is now the main internal unit
  - some legacy helpers still exist around it and can be reduced later

### Phase 3: Rebuild Grouping Around `DimensionItem`

Status: done in first form, still tunable.

Replace DTO/member-first grouping with `dim`-style grouping semantics.

Grouping must be based on:

- same view
- compatible domain `DimensionType`
- parallel `Direction`
- compatible `TopDirection`
- compatible lead-line geometry
- value compatibility where required by the scenario

Done when:

- `DimensionGroupFactory` effectively becomes a `DimensionItem -> DimensionGroup`
  builder
- grouping logic is explainable in `dim` terms
- current status:
  - public API now exposes the real line-based groups
  - earlier summary bucketing has been removed from the main read path
  - future work is about policy tuning, not reintroducing a second grouping model

### Phase 4: Port `DimensionOperations` Concepts

Status: in progress.

The current arrangement/spacings code should be aligned with `dim` operations,
especially for:

- eliminate / reject redundant dimensions
- align
- combine
- play-aware adjustment
- same-line/near-line grouping transitions

Current status:

- first elimination step is present
- internal line-first arrangement pipeline is present:
  - arrange-specific dedup
  - align normalization for planning/stacks
  - runtime normalization of close `Distance` values inside aligned clusters
  - spacing analysis
  - arrangement planning
  - distance-adjustment translation
  - runtime apply method
  - public MCP apply command now exists as `arrange_dimensions`
- current stack planner is anchor-based:
  - the first surviving dimension in a stack remains fixed
  - later dimensions are moved relative to that anchor
  - they may be pushed outward or pulled inward toward target gap
  - single-dimension stacks are not moved
- current elimination is intentionally conservative:
  - simple items may be rejected when a more informative item in the same group
    already covers the same span
- exact duplicate elimination for simple items is present
- first representative-selection step is present:
  - nearby packets inside a group can now keep one representative item
  - current selection is still intentionally simple and policy-driven
- reduction debug now exposes:
  - raw vs reduced groups
  - per-item rejection reasons such as `covered`, `equivalent_simple` and
    `representative_packet`
  - representative packet structure and selection data
- conservative combine-candidate analysis is present:
  - candidate sets are detected
  - blocking reasons are exposed
  - combine preview is built from the candidate analysis
  - conservative real merge is now available through `combine_dimensions`
- arrangement debug remains internal/bridge-only
- combine and arrange remain separate actions:
  - `combine_dimensions` performs controlled merge
  - `arrange_dimensions` performs post-placement spacing and normalization

## Ближайшие шаги по `arrange_dimensions`

Простыми словами, текущий `arrange_dimensions` уже делает не только базовую
раздвижку, но все еще остается консервативным post-processing слоем, а не
полноценным layout-движком аннотаций.

Он пока не является полноценным layout-движком размеров.

Что уже есть сейчас:

- группировка размеров в рабочие геометрические семьи
- arrange-specific dedup перед planning
- align normalization для planning/stacks
- runtime-нормализация близких `Distance` внутри align-кластера
- line-first spacing analysis
- anchor-based план смещений по стеку параллельных размеров
- применение смещений через `Distance`
- входной `targetGap` задается в мм бумаги, текущий public default `10 мм`

Как это работает сейчас:

- из стека сначала убираются только явные дубли и консервативно покрытые
  случаи
- первый surviving dimension в stack остается anchor-ом
- следующие размеры приводятся к target gap относительно anchor-а и уже
  выставленных соседей
- если gap меньше target, размер толкается наружу
- если gap больше target, размер может быть подтянут ближе
- если после dedup в stack остался один размер, он не двигается

Чего пока нет в готовом виде ни здесь, ни в `dim`, ни в `xDrawer`:

- post-processing слоя, который смотрит на уже созданные размеры, их текст и
  метки как на единый набор аннотаций
- collision-aware arrangement по `TextBounds` / text polygons / mark boxes
- выбора лучшего варианта размещения из нескольких попыток

Поэтому следующий реалистичный roadmap для `arrange_dimensions` такой:

### 1. Нормализация до раздвижки

Перед тем как раздвигать размеры, сначала чистить входной набор:

- убирать дубли и почти-дубли размеров
- выравнивать близкие размеры на одну общую линию
- объединять совместимые размеры в одну цепочку, если это безопасно

Эта часть в основном уже есть в `dim` как источник правил и приемов.

### 2. Более умная раздвижка

После нормализации:

- двигать размеры более осмысленно, а не просто толкать весь стек наружу
- лучше объяснять, почему конкретный размер нельзя сдвинуть
- позже добавить выбор стороны и/или `ExaggerationDirection`, если это даст
  более стабильный layout

### 3. Учет текста и меток

Это уже следующий уровень:

- учитывать текст размеров
- учитывать пересечения с метками
- пробовать несколько вариантов размещения и выбирать лучший

Это уже новый слой, а не перенос готового кода из старых проектов.

## Текущий техдолг

### Arrangement / layout debt

- `arrange_dimensions` пока не является полноценным annotation-aware layout
  engine
- stack planner сейчас anchor-based и не умеет выбирать более удачный anchor
  или подтягивать весь stack ближе к детали как единый блок
- нет collision-aware layout для:
  - text boxes размеров
  - marks
  - других annotation objects
- нет выбора лучшей стороны размещения
- нет retry/fallback layout strategy
- align/runtime normalization пока рассчитаны только на консервативные
  поддерживаемые осевые параллельные случаи
- часть mapping logic через `Distance` остается неоднозначной для unsupported
  cases

### Combine debt

- `combine_dimensions` сейчас консервативный и intentionally narrow
- combine разрешается только там, где текущий combine-candidate analysis уже
  уверен, что случай безопасен
- merge не делает post-layout cleanup после создания replacement dimension
- preview/result surface есть, но главным explain/debug surface по-прежнему
  остается `get_dimension_groups_debug`
- targeted combine работает только по combine-candidates, полностью попавшим в
  выбранный набор `dimensionIds`

### Placement / heuristics debt

- не перенесены richer placement heuristics из `xDrawer`
- нет отдельного policy-слоя для:
  - `Placing`
  - `ExaggerationDirection`
  - preset-by-source
- scale/text-height heuristics пока базовые, не полные
- для native moved dimension text по-прежнему нет надежно наблюдаемой позиции
  через доступный Tekla API surface
- synthetic text geometry остается fallback path

### Tooling / debug debt

- arrangement debug и group/source/text debug остаются bridge/internal-only
- public MCP surface пока intentionally narrower, чем internal debug surface
- если позже потребуется operator-friendly inspection, нужен отдельный
  публичный debug/read model, а не прямой слив internal DTO

### Validation / test debt

- unit tests есть, но live acceptance на реальных drawings остается
  обязательной для:
  - arrange
  - combine policy/layout follow-up changes
- `TeklaMcpServer.Tests` в текущем окружении нестабилен из-за `NU1701`/warning
  policy
- часть новых dimension tests нельзя считать надежно подтвержденными, пока test
  project не станет стабильно rebuild-иться

## Архитектурная граница ответственности

Текущий устойчивый принцип такой:

- `Dedup` отвечает только за явные дубли и остается консервативным
- спорные решения уходят в `Layout Policy`
- верхний порядок шагов собирает `Arrangement Orchestrator`

Ниже этот следующий этап уже разложен подробнее как согласованный roadmap.

## Кратко согласованный roadmap

### 1. `Dedup`

- убирать только явные дубли
- не принимать спорные layout-решения
- оставаться простым, консервативным и безопасным

### 2. `Dimension Context`

У каждого размера должен появиться собственный контекст.

Минимально он должен отвечать на вопросы:

- что именно размерится
- от каких точек построен размер
- какие детали / болты / grid / control-объекты являются источником
- какая локальная геометрия и видимые границы относятся к этому размеру
- внешний это размер, внутренний, bolt-chain, control dimension или другой
  сценарий

Без этого более умный layout остается полуслепым.

Follow-up для текущей реализации context:

- не смешивать `DrawingObjectId` и `ModelId` в один канонический список ids;
  следующий шаг здесь — разделить context summary хотя бы на
  `SourceDrawingObjectIds` и `SourceModelIds`
- warning `source_geometry_partial` считать нормальным состоянием partial
  coverage:
  - local bounds могут быть успешно построены
  - но не по всем source candidates
  - это не failure, а признак неполного geometry coverage

### 3. `Layout Policy`

Правила layout должны зависеть от контекста размера.

Сюда входят решения:

- richer chain vs poorer chain
- partial overlap / subchain
- part vs bolt vs control vs grid
- что оставить, что скрыть, что объединить, а что только раздвигать

Текущий статус:

- `Layout Policy` уже существует как debug-first слой классификации
- `RecommendedAction` уже существует как debug-first слой поверх classification
- текущие explainable cases уже покрывают:
  - equivalent measured geometry
  - richer chain vs poorer chain
  - mergeable chain
  - distinction between:
    - `DuplicateChain`
    - `InformationPreservingMerge`

Назначение этого слоя:

- не заменять `combine` и `arrange`
- не смешивать classification и execution
- стать мостом между debug-first policy и будущим orchestration

Правила первой версии recommendation layer:

- `SuppressCandidate` использовать только для точных дублей
  (`equivalent_measured_geometry`)
- если размер одновременно `LessPreferred` и `CombineCandidate`, рекомендация
  должна быть `PreferCombine`, а не suppression
- poorer subchain без merge verdict оставлять как `OperatorReview`
- mergeable cases различать так:
  - `DuplicateChain`: удаление одного размера или combine дают одинаковую
    информацию, поэтому это не strong `PreferCombine`
  - `InformationPreservingMerge`: combine сохраняет или добавляет измерительную
    информацию, поэтому это нормальный `PreferCombine`

### 4. `Candidate Placements`

Для размера или stack нужно генерировать несколько допустимых вариантов:

- оставить как есть
- придвинуть ближе
- отодвинуть дальше
- сменить сторону
- использовать специальные варианты для отдельных source/type сценариев

### 5. `Cost Function`

Лучший вариант должен выбираться не по одному правилу, а по штрафам.

Минимально нужно учитывать:

- пересечения с геометрией
- пересечения с текстом размеров
- пересечения с marks и другими annotation objects
- излишний разлет stack-а
- компактность и читаемость

### 6. `Arrangement Orchestrator`

Верхний pipeline должен постепенно стать таким:

- `dedup`
- `dimension context`
- `layout policy`
- `combine`
- `arrange`

## Следующий backlog

### 1. Stabilize current runtime behavior

- `combine_dimensions` success-path is live-validated
- rollback/failure path is validated via internal fault-injection seam and live
  smoke
- `arrange_dimensions` behavior on real drawings: validated
- `combine v2` local post-combine arrange handoff is implemented as
  best-effort and live-smoke-validated on a real mergeable pair
- dimension read/debug paths now use bounded consistency retry to reduce the
  immediate stale-read window after mutate commands

Этот блок по сути закрыт для current conservative runtime path.

### 2. Improve arrangement quality

- collision-aware layout
- mark/text avoidance
- side switching / smarter placement policy

### 3. Improve combine quality

- `combine v2` local arrange handoff now exists
- refine when handoff should be considered `no changes` vs `applied`
- broader but still explainable combine policy only after current conservative
  path proves stable
- keep reread stabilization bounded and internal-only; do not silently grow it
  into a generic drawing refresh layer without explicit need
- document and keep explicit that combine commit and arrange handoff commit are
  non-transactional; handoff rollback failure may leave partial post-merge
  rearrangement

### 3a. Add policy recommendation layer

- `RecommendedAction` already exists as debug-first policy output
- next step here is orchestration/runtime consumption, not another debug layer
- keep recommendation explainable and separate from direct execution

### 4. Placement policy expansion

- preset resolver
- `Placing` / `ExaggerationDirection` policy
- richer scale/text-height heuristics from `xDrawer`

Done when:

- arrangement logic talks in terms of items/groups, not transport DTO hacks
- line-first spacing is expressed with domain entities
- elimination, representative selection and combination rules are separated into
  explicit operations

### Phase 5: Introduce Configurable Policies

Status: done in first form.

Add explicit policy objects so grouping and reduction remain flexible.

Initial direction:

- `DimensionGroupingPolicy`
- `DimensionReductionPolicy`

These policies should control things like:

- line-band tolerance
- collinearity tolerance
- extent overlap tolerance
- strict vs soft `TopDirection` matching
- shared-point requirements
- how aggressively similar dimensions are reduced inside a group

Done when:

- grouping and elimination no longer depend on magic constants only
- policy changes do not require redesigning the domain model
- different drawing scenarios can tune grouping/reduction behavior explicitly

Current status:

- `DimensionGroupingPolicy` is introduced and used by `DimensionGroupFactory`
- `DimensionReductionPolicy` is introduced and used by `DimensionOperations`
- representative selection mode is already policy-driven
- next work is not introducing policies, but tuning them and porting more exact
  `dim` rules on top of them

### Phase 6: Reproject Public Read API

Status: partially done, continue refining.

`get_drawing_dimensions` should be generated from the domain model.

The public response may still expose:

- Tekla dimension type
- orientation
- bounds
- text debug info
- raw vs reduced counts

But those are projections, not the model itself.

Current status:

- `get_drawing_dimensions` is already projected from the domain model
- the public response exposes raw vs reduced counts so reduction does not hide
  how many dimensions and items existed before analysis
- deep reduction transparency stays in `get_dimension_groups_debug`, not in the
  main read path

### Phase 7: Text Geometry As A Separate Track

Status: secondary.

Text geometry remains important, but must stay outside the core redesign.

Rules:

- native runtime text geometry is used when Tekla exposes it
- otherwise text geometry remains synthetic
- text geometry must not define grouping semantics

## Acceptance Criteria

The redesign is on track when:

- internal code centers on `DimensionItem` and `DimensionGroup`
- current DTO-first grouping layer is reduced or removed
- grouping is explainable directly from `dim`
- grouping remains the only real grouping model
- arrangement logic consumes domain entities, not raw API DTOs
- `orientation` is only a summary
- bbox logic is only fallback/debug
- public APIs are projections from the domain model
- elimination is a separate operation on top of groups
- merge stays a separate operation on top of reduced groups
- grouping and elimination rules are policy-driven rather than hard-coded
- debug can explain why an item was kept, rejected or selected as a packet
  representative
- combine-candidate analysis remains visible and explainable before
  `combine v2` broadens the current conservative merge path

The redesign is ready to expose further arrange functionality when:

- grouping is line-first and `dim`-aligned
- spacing is line-first and `dim`-aligned
- move mapping is validated on live drawings
- text geometry status is explicit:
  - runtime-observed when Tekla exposes native text objects/position
  - synthetic fallback when Tekla does not expose them
