# Роадмап слоя ViewDefinitions

## Цель

`ViewDefinitions` отвечает не за layout уже существующих видов, а за
**определение набора видов и правил их формирования**.

Это слой про intent:

- какие виды должны быть созданы;
- как ориентировать объект на чертеже;
- какие visibility rules применить;
- какие sheet-intent и preset rules использовать;
- какие параметры view family считать частью definition, а не runtime-layout.

## Граница с ViewLayout

- `ViewDefinitions`
  - описывает **что хотим получить**
  - задаёт view families, orientation, visibility, preset values
  - может быть использован для `Assembly`, `GA` и позже других drawing scopes
- `ViewLayout`
  - работает с **уже существующими видами**
  - отвечает за query, fit-to-sheet, scale selection, arrangement,
    projection alignment, section/detail placement

Коротко:

- `ViewDefinitions` = desired view set
- `ViewLayout` = placement and runtime handling of actual views

## Что входит в этот слой

### 1. Scope и family definition

Слой должен уметь описывать:

- drawing scope:
  - `Assembly`
  - `Ga`
  - позже другие scope при необходимости
- required view families:
  - `Front`
  - `Top`
  - `Bottom`
  - `3D`
  - `Section`
  - позже `Detail` как отдельная policy-ветка при необходимости

Это не runtime object `View`, а definition-level request.

### 2. Orientation definition

Слой должен хранить и объяснять:

- как выбирается coordinate system;
- как задаётся axis rotation;
- нужен ли rotation around axis;
- какая orientation policy применяется для текущего scope.

Это должно быть отдельным first-class слоем, а не россыпью флагов.

### 3. Visibility definition

Слой должен определять semantic visibility rules:

- `HideBackParts`
- `HideSideParts`
- позже:
  - hide by depth threshold
  - hide by secondary-role
  - hide by size/noise policy

Важно:

- это не raw hide-by-id API;
- это definition-level intent, который потом может быть вычислен через geometry.

### 4. View parameter definition

Слой должен уметь задавать definition-level параметры:

- default scale
- separate 3D scale
- section scale
- shortening
- create-view mode
- file/attribute profile names, если они реально нужны consumer-слою

### 5. Sheet intent definition

Слой должен выражать intent по листу:

- auto sheet size on/off
- allowed sheet sizes
- drawing size mode
- возможно preferred sheet policy

Важно:

- это ещё не сам layout;
- это входные правила для creation + later layout.

### 6. Preset / profile layer

Слой должен поддерживать reusable presets:

- assembly-oriented preset
- ga-oriented preset
- compact preset
- full-detail preset
- later custom presets from config/storage

Preset должен быть агрегатом:

- scope
- families
- orientation
- visibility
- scale/shortening parameters
- sheet intent

## Что пока не входит

На первом этапе сюда **не** входят:

- fit-to-sheet;
- runtime arrangement;
- collision resolution;
- projection alignment;
- mark placement;
- dimension placement;
- exact geometry/contacts.

То есть этот модуль не должен дублировать `ViewLayout`.

## Предлагаемые типы

Минимальный первый набор:

- `DrawingViewDefinitionScope`
- `DrawingViewFamilyKind`
- `DrawingViewDefinition`
- `DrawingViewDefinitionSet`
- `DrawingViewOrientationPolicy`
- `DrawingViewVisibilityPolicy`
- `DrawingViewSheetPolicy`
- `DrawingViewPreset`
- `GetViewDefinitionPresetResult`

Потом API:

- `IViewDefinitionApi`
- `TeklaViewDefinitionApi`

## Целевой сценарий использования

Потребитель должен уметь сказать примерно так:

- для `Assembly` нужны `Front + Top + 3D`;
- orientation брать `ByAssemblyAxis`;
- `HideSideParts = true`;
- `HideBackParts = false`;
- `Scale = 1:10`;
- `3DScale = 1:15`;
- `Shortening = 200`;
- sheet size auto, но только из разрешённого набора.

И уже потом другой слой решает:

- как именно эти виды создать;
- как передать их в `ViewLayout`;
- как применить runtime placement.

## Этапы

### Phase 1. Contracts and presets

Сделать:

- folder structure;
- модели definition-layer;
- базовые enums;
- первый preset object;
- roadmap и naming freeze.

Статус:

- folder создан;
- базовые contracts введены;
- первый preset API введён;
- стартовый assembly preset использует legacy-подобный baseline:
  - `AlongAxis`
  - `Front` enabled
  - `Top` disabled
  - `Bottom` disabled
  - `3D` disabled by default
  - `Scale = 1:10`
  - `3D scale = 1:15`
  - `Section scale = 1:10`
  - `Shortening = 200`
  - `HideSideParts = true`
  - `HideBackParts = false`
  - `CoordinateSystemSource = Auto`
  - `AxisRotationX/Y = Auto`

### Phase 2. Definition builders

Сделать:

- assembly preset builder;
- ga preset builder;
- mapping preset -> definition set;
- initial defaults for scales, shortening, visibility.

### Phase 3. Orientation and visibility rules

Сделать:

- orientation policy objects;
- visibility policy objects;
- definition-time validation;
- explicit separation between requested and resolved policy.

### Phase 4. Creation integration

Сделать:

- bridge from `ViewDefinitions` to drawing creation layer;
- apply preset during creation flow;
- handoff to `ViewLayout` after actual views exist.

### Phase 5. Config/persistence

Опционально позже:

- load/save presets;
- profile-based preset registry;
- user-defined preset set.

## Acceptance criteria

Слой считается полезно введённым, когда:

- `ViewLayout` и `ViewDefinitions` не пересекаются по ответственности;
- `Assembly` и `GA` могут использовать один и тот же contract family;
- visibility/orientation intent задаются без прямой привязки к runtime layout;
- preset object можно читать как drawing intent без знания внутренней layout-логики.

## Следующий практический шаг

После фиксации roadmap:

1. ввести базовые enums и contracts;
2. собрать первый `DrawingViewPreset` для assembly scope;
3. не трогать `ViewLayout` beyond explicit integration points.
