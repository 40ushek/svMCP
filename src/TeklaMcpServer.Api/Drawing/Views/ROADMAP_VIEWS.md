# Роадмап расстановки видов

## Цель

Расставлять виды так, как это делает опытный чертёжник:

- по проекционной логике, а не по чистой упаковке
- с максимально крупным допустимым масштабом
- с устойчивым и объяснимым degraded behavior

`MaxRects` и shelf-packing допустимы только как fallback для остатка,
а не как ядро layout-логики.

## Ключевые принципы

- Сначала семантика и ограничения, потом упаковка.
- `BaseView` должен быть явной сущностью planner'а.
- `SectionView` размещается по направлению взгляда, а не по одному только bbox.
- `DetailView` и detail-like derived views не должны ломать base/section каркас.
- Для geometry/debug/collision checks каноничен реальный bbox вида.
- `Origin` допустим только как runtime-механика применения позиции.
- Post-alignment и centering уточняют хороший план, а не спасают плохой.

## Текущее состояние

### Уже сделано

- `DrawingViewArrangementSelector` предпочитает `BaseProjectedDrawingArrangeStrategy`.
- `BaseViewSelection` выделен в отдельный этап.
- entry points и основной planner больше не используют прямой `FrontView` lookup
  как основной source of truth для topology.
- введена единая topology-модель standard neighbors:
  - `NeighborSet`
  - `StandardNeighborResolver`
  - `NeighborRole`
- semantic split видов есть в коде:
  - `BaseProjected`
  - `Section`
  - `Detail`
  - `Other`
- planner и projection alignment теперь используют один и тот же source of truth
  для standard projected neighbors.
- standard neighbors определяются относительно выбранного `BaseView`,
  а не через прямые raw lookups `TopView/BottomView/BackView` в entry points.
- для standard projected views действует intentional stabilizing override:
  `ViewType` (`TopView` / `BottomView` / `BackView` / `EndView`) имеет приоритет
  над coordinate-system heuristic.
  Coordinate systems и текущая sheet-position используются как fallback,
  когда `ViewType` не даёт явной роли.
- `SectionPlacementSide` реализован как явная семантика:
  - `Left`
  - `Right`
  - `Top`
  - `Bottom`
  - `Unknown`
- planner размещает `Left/Right/Top/Bottom` секции отдельными stack-path'ами.
- projection alignment для секций привязан к `SectionPlacementSide`:
  - `Left/Right -> Y`
  - `Top/Bottom -> X`
- `DetailView` исключён из global scale selection.
- основной каркас теперь раскладывается без участия detail views.
- detail views расставляются отдельным post-pass после main layout.
- для detail placement учитывается parent relation через `DetailMark`.
- для `DetailView` anchor читается с родительского вида:
  - `LabelPoint`
  - `BoundaryPoint`
  - `CenterPoint`
- candidate search для detail placement уже anchor-driven:
  позиции от anchor входят в candidate set, а не только в scoring.
- добавлен naming-based classifier для detail-like views:
  - `Detail*`
  - `Det*`
  - `D*`, если имя длиннее одного символа
  - имена, начинающиеся с lowercase
  - специальное исключение: ровно `D` не считается detail-like
- detail-like `SectionView` тоже уходят в detail semantic class для layout.
- для detail-like `SectionView` после main layout используется связь через `SectionMark`.
- для detail-like `SectionView` anchor теперь берётся из реальной геометрии:
  midpoint `SectionMark.LeftPoint/RightPoint`, спроецированный в sheet coordinates.
- planner и final post-pass используют одну и ту же anchor-идею для detail-like sections.
- scale search защищён от заведомо безумных scale candidates:
  если вид больше usable area листа, кандидат сразу отбрасывается.
- стартовый список scale candidates теперь считается от реальной frame geometry
  scale-driver видов, а не от одного общего текущего scale листа.
- стартовый candidate scale теперь выбирается по midpoint между соседними
  standard scales:
  lower half интервала стартует с меньшего масштаба, upper half — с большего.
  Это уменьшает число лишних probe-прогонов относительно чистого `floor`,
  но не возвращает прежнюю жёсткость `ceil`.
- candidate-fit в текущей реализации идёт по реальному probe-path:
  candidate scale реально применяется к видам, затем planner читает
  фактические frame sizes/bbox, а не использует линейную аппроксимацию
  `oldFrame * sourceScale / targetScale`.
  При этом parser/bridge default без явного mode token трактуется как `FinalOnly`,
  но сам `applyMode` пока ещё не переключает отдельную layout-ветку внутри
  `TeklaDrawingViewApi.Layout`; сейчас это в основном transport/trace/result contract.
- в planner реализован `zone budgeting` вокруг `BaseView`:
  budgets для `Top/Bottom/Left/Right` считаются и используются
  для выбора допустимого окна `BaseView`.
- `baseRect` внутри budget-window теперь ищется через `MaxRects`,
  а не через старый `3x3` centered-first поиск.
- `TryFindBaseViewRectInWindow` учитывает `context.Gap` на уровне budget-window:
  бюджеты включают gap, поэтому `BaseView` больше не встаёт вплотную к _внешним_
  границам budget-window.
  При этом gap между `baseRect` и blocked-областями _внутри_ окна не применяется:
  `baseWidth/baseHeight` передаются без `+context.Gap`, blocked-прямоугольники
  не раздуваются — см. «Что ещё не закончено».
- `EndView` больше не обязан уходить в residual:
  если topology resolver классифицирует его как `SideNeighborRight`,
  он получает явный правый neighbor slot в main layout.
- default behavior у `fit_views_to_sheet` на parser/bridge layer теперь intentional:
  вызов без явного mode token трактуется как `FinalOnly`.
  При этом API-level default в сигнатуре пока остаётся `DebugPreview`,
  то есть это осознанное различие между parser contract и API contract.
- default `gap` для `fit_views_to_sheet` на parser/tool layer теперь `4 мм`.
- если standard section не встала в normal stack и уходит в residual fallback,
  `EstimateFit` теперь дополнительно делает hard-check итоговой residual geometry:
  overlap с другими planned views, overlap с reserved areas и выход за лист
  считаются hard fail для candidate scale.
  То есть fallback для standard sections по-прежнему разрешён,
  но плохой fallback больше не проходит как `fits=1`.
- `relaxed` main layout больше не обязан выбрасывать весь partial plan,
  если один standard neighbor ломает `main skeleton`:
  конфликтующий `Top/Bottom/Left/Right` now defer'ится локально,
  а уже хорошо вставшие anchor views сохраняются.
  После этого в fallback-path уходит только unresolved neighbor, а не весь набор видов.
- для такого локального defer добавлен явный trace:
  `main_skeleton_relaxed_resolved`
  с количеством deferrals и списком ролей, которые были сняты с main skeleton.
- в relaxed path для `TopView` перед уходом в residual planner делает отдельную
  попытку sheet-top placement, а не только centered-above-base slot.
- projection alignment теперь пропускается для mixed-scale набора non-detail видов:
  если spread по scale больше `5%`, post-pass не пытается сохранять
  жёсткую проекционную связь.
- keep-scale (`PreserveExistingScales`) post-pass теперь читает реальные
  `frame offsets` из фактической sheet geometry, а не деградирует к
  origin-centered bbox.
  Это закрывает конкретный live-баг, где `SectionView` могла после valid layout
  уехать внутрь `FrontView` из-за неверного collision check в projection-pass.
- внутри `BaseProjectedDrawingArrangeStrategy` почти завершён safe internal refactor
  вокруг `main skeleton` placement:
  - выделены `MainSkeletonPlacementState`, `MainSkeletonNeighborSpec`,
    `ViewPlacementSearchArea`
  - `strict`, `relaxed` и `diagnostic` paths сведены к более симметричным
    orchestration helper'ам
  - success / reject / diagnose flow для optional standard neighbors
    больше не размазан по четырём отдельным веткам `Top/Bottom/Left/Right`
- для optional main-skeleton placement уже есть локальный geometry/search mini-layer:
  - `FindCenteredRelativeRectInSearchArea(...)`
  - `FindRelativeRectInSearchArea(...)`
  - `FindTopViewAtSheetTopInSearchArea(...)`
  - `TryValidateMainSkeletonNeighborRect(...)`
  - `DiagnoseRelativePlacementFailureInSearchArea(...)`
- `Stage 1` local geometry/search/validation layer внутри
  `BaseProjectedDrawingArrangeStrategy` по сути закрыт:
  - `ViewPlacementSearchArea` стал общим internal contract для
    `main skeleton`, section stacks, relative placement, sheet-top fallback,
    base-window selection и detail placement
  - raw `freeMin/freeMax` overload'ы в основном сведены к wrapper'ам и legacy
    test/call-site surfaces
  - соседние `strict`, `relaxed`, `diagnostic` и section-adjacent checks
    больше не держат отдельные локальные реализации одного и того же
    search/validation flow
- test-facing legacy surface для `TryDeferMainSkeletonNeighbor(...)`
  сохранён как совместимый wrapper, чтобы внутренний refactor не ломал
  существующие unit-тесты

### Что ещё не закончено

- `BaseViewSelection` всё ещё имеет `FrontView`-centric fallback shortcut.
- явный projection graph в коде ещё не выделен.
- `ProjectionMethod` ещё не стал явным параметром.
- oversized sections пока не вынесены в отдельную degraded policy.
- в `UniformAllNonDetail` section views по-прежнему входят в scale-driver set,
  поэтому один outlier section всё ещё может тянуть `optimalScale` вниз.
- local scale reduction для outlier section пока нет.
- repeated `fit_views_to_sheet` ещё не гарантированно идемпотентен на всех листах.
- после перехода `FinalOnly` на real-probe path исчез отдельный слой ошибок
  из-за линейного пересчёта bbox, но на части листов итог всё ещё плавает
  между несколькими локальными layout/state вариантами.
- detail placement уже anchor-aware, но policy всё ещё можно улучшать:
  при нехватке места нужна более явная и объяснимая деградация.
- `Top/Bottom` section placement ещё не гарантирует сохранение сильной
  проекционной связи с base view при конфликте по `X`.
- `zone budgeting` и `MaxRects` убрали жёсткую center-привязку `BaseView`,
  но ложные reject в `EstimateFit` для `Top/Bottom` section всё ещё встречаются:
  planner может отвергнуть рабочий `1:20` layout по `reason=no-valid-x`,
  хотя фактическая расстановка на листе при том же масштабе оказывается валидной.
- repeated apply всё ещё может ломать projection post-pass:
  после повторного `fit_views_to_sheet` возможны массовые
  `projection-skip:view-overlap`.
- geometry/collision pipeline всё ещё архитектурно раздвоен:
  main layout и projection post-pass проверяют placement похожими,
  но не идентичными путями.
  Из-за этого одна и та же пара видов может получить разные ответы на вопрос
  `overlap / no-overlap` в разных фазах `fit_views_to_sheet`.
  Последний live-регресс конкретно для keep-scale collision checks уже закрыт,
  но архитектурный долг остаётся: решение `можно / нельзя двигать view`
  по-прежнему не сведено к одному validator/source of truth.
- на части листов основной main layout всё ещё допускает слишком плотную схему
  ещё до projection:
  `Front/Top/Section` оказываются почти вплотную, а post-pass уже не может
  восстановить проекционную связь без overlap.
- если `strict/relaxed` не могут удержать `TopView` в standard neighbor path,
  она всё ещё может попасть в `MaxRects` как unresolved residual view.
  После локального defer хорошие anchor views уже не теряются, но policy для самого
  `TopView` остаётся слабой: residual slot может быть геометрически валиден,
  но композиционно плох для верхнего вида.
- `MaxRects`-packer для поиска `baseRect` сейчас создаётся заново для каждого
  candidate window. Это не bug, но остаётся низкоприоритетным perf-долгом.
- standard section, не вставшая в preferred stack, уже не должна проходить
  через residual fallback с hard overlap, но остаётся открытым вопрос
  более мягкой деградации:
  когда fallback геометрически валиден, но проекционно выглядит слабо,
  planner пока ещё не умеет это оценивать отдельной soft-метрикой.
- зазор между `baseRect` и blocked-областями внутри `TryFindBaseViewRectInWindow`
  не задаётся: `baseWidth/baseHeight` передаются без `+gap`, blocked-прямоугольники
  не раздуваются. `BaseView` может вставать вплотную к зарезервированным областям
  и к уже расставленным видам без запаса для projection post-pass.
- `optimalScale` в preserve-scale путях (`PreserveExistingScales`,
  `UniformMainWithSectionExceptions`) вычисляется как `Max()` всех масштабов:
  `ShouldSkipProjectionAlignment` пропустит alignment, если самый мелкий
  вид листа >= 100, даже когда другие виды (1:20, 1:50) реально нуждаются
  в alignment. Текущий код и комментарий в `TeklaDrawingViewApi.Layout`
  всё ещё защищают `Max()`, но roadmap фиксирует целевой переход на `Min()`
  как семантически правильный агрегат для этого decision.
- бюджет стека секций вычисляется как суммарная высота/ширина, но не проверяется
  на каждую секцию отдельно: одна oversized-секция способна занять весь бюджет
  без диагностики для остальных.
- `EstimateFit` и projection post-pass ещё не переведены на тот же
  geometry/search contract, который теперь уже стабилизирован внутри
  `BaseProjectedDrawingArrangeStrategy`.
- user-facing aliases для `fit_views_to_sheet` унифицированы:
  parser знает `preserveexistingscales` / `preservemixedscales` / `keepscale`
  (все три ведут в `PreserveExistingScales`). Закрыто.
- debug env var `SVMCP_FIT_DEBUG_STOP_ON_SECTION_REJECT` активирует hard stop
  при reject horizontal section (бросает `InvalidOperationException`).
  Это временный локальный debug-hook, а не часть нормального runtime contract.

### Согласованный статус этапов

- `Stage 0: safe internal refactor`:
  завершён.
  Основной structural cleanup внутри `BaseProjectedDrawingArrangeStrategy`
  уже сделан без смены layout-policy.
- `Stage 1: local geometry/search/validation layer inside strategy`:
  завершён.
  Локальный mini-layer уже покрывает основные placement/search/validation paths
  внутри strategy и считается устойчивой внутренней границей.
- `Stage 2: shared geometry/collision pipeline across layout phases`:
  следующий активный этап.
  Здесь нужно выйти за пределы одного блока и свести `strategy`,
  `EstimateFit`, apply и projection post-pass к одному source of truth.
- `Stage 3: behavioral fixes`:
  ещё не начат.
  Сюда относятся `EstimateFit vs apply`, `Top/Bottom` sections,
  oversized policy и идемпотентность repeated `fit_views_to_sheet`.
- `Stage 4: policy polish`:
  после стабилизации geometry/collision pipeline.
  Сюда входят detail/dependent placement policy и явная конфигурация
  projection method.

## Зафиксированные текущие контракты

- parser/bridge default для `fit_views_to_sheet`:
  - `ApplyMode = FinalOnly`
  - `gap = 4 мм`
- API-level default в сигнатуре `FitViewsToSheet(...)` пока остаётся
  `DrawingLayoutApplyMode.DebugPreview`; это осознанное различие слоёв,
  а не опечатка документации.
- current resolver order для standard projected neighbors:
  `ViewType override -> coordinate systems -> current position`
- current scale-driver behavior:
  - `UniformAllNonDetail` использует все non-detail views
  - `UniformMainWithSectionExceptions` и `PreserveExistingScales`
    не делают unified rescale и валидируют текущие масштабы как есть
- current projection-skip decision:
  `ShouldSkipProjectionAlignment` использует агрегат `optimalScale`,
  который в preserve-scale путях сейчас считается через `Max()`.
  Дополнительно alignment пропускается для mixed-scale non-detail набора,
  если spread по scale больше `5%`;
  это documented current behavior, но не финальная целевая семантика.
- временный debug env var:
  `SVMCP_FIT_DEBUG_STOP_ON_SECTION_REJECT`
  включает hard stop на reject horizontal section и должен использоваться
  только для локальной диагностики.

## Семантическая модель

- `BaseView` — опорный вид листа.
- `BaseProjected` — базовый вид и его стандартные ортогональные соседи.
- `SectionView` — направленный производный вид, завязанный на направление взгляда.
- `DetailView` — увеличенный локальный фрагмент родительского вида.
- `detail-like SectionView` — локальный derived view, который по layout-политике
  ведёт себя ближе к detail family, но семантически остаётся section-derived.

## Правила размещения

### Base / projected

- сначала выбирается `BaseView`
- затем через `NeighborSet` выбираются стандартные соседи:
  - `TopNeighbor`
  - `BottomNeighbor`
  - `SideNeighborLeft`
  - `SideNeighborRight`
- для standard neighbors сначала учитывается явный `ViewType`,
  затем coordinate systems, затем текущая позиция на листе
- затем направленные секции
- упаковка остатка возможна только после этого
- `ResidualProjected` получает только те projected views, которые не были выбраны
  как standard neighbors.

### Sections

- `Left` секции тяготеют к правой стороне листовой схемы только по логике взгляда:
  взгляд влево означает размещение вида справа от объекта
- `Right` секции аналогично размещаются слева
- `Top`/`Bottom` секции живут в вертикальной зоне `BaseView`
- `Left`/`Right` секции живут в боковой зоне `BaseView`
- grouping и alignment обязаны следовать `SectionPlacementSide`
- текущий horizontal stack для `Top/Bottom` sections сначала пробует
  проекционный центр base view, затем только грубые крайние позиции.
  Это уже даёт explainable diagnostics, но пока слишком сужает поиск по `X`.
- текущий `EstimateFit` для `Top/Bottom` sections всё ещё может считать
  rect хуже, чем реально получается в apply path на том же листе.

### Details

- `DetailView` не влияет на global scale selection
- `DetailView` не конкурирует за ранние проекционные слоты
- он размещается после основного base/section каркаса
- основная метрика — близость к `DetailMarkAnchor`
- fallback в residual placement допустим только как явная деградация
- для standard sections residual fallback допустим только если итоговая геометрия
  остаётся overlap-free и не конфликтует с reserved areas;
  потеря идеальной projection relation сама по себе не является hard fail.

### Detail-like sections

- naming-based classifier переводит часть `SectionView` в detail semantic class
- такие виды не должны участвовать в main section skeleton
- после основных видов они должны размещаться рядом с реальным местом сечения
- anchor для них берётся по `SectionMark.LeftPoint/RightPoint`

## Технические инварианты

- для planner/debug используется bbox-first модель
- `Origin` не используется для вывода “вид внутри/вне листа”
- fit diagnostics и итоговая расстановка должны опираться на одну и ту же модель
- любой fallback должен быть объясним:
  - почему preferred placement не влез
  - куда произошла деградация
  - какой вид остался residual
- если `relaxed` снял standard neighbor с main skeleton и продолжил partial plan,
  лог должен явно показывать:
  - какая роль была defer'нута
  - что anchor views были сохранены
  - что в residual fallback ушёл только unresolved neighbor
- scale selection должен быть устойчив к повторному запуску на уже расставленном листе
- выбор кандидатов масштаба не должен зависеть от случайно смешанных текущих
  scale видов на входном листе
- для `Top/Bottom` sections лог должен явно показывать:
  - preferred centered rect
  - blockers по `X`
  - причину fallback / skip
- для сравнения `estimate` vs `apply` лог должен позволять увидеть,
  почему рабочий layout был отвергнут раньше времени

## Ближайшие шаги

Порядок ниже уже исходит из того, что `Stage 0` и `Stage 1` закрыты.

### 1. Выделить единый geometry/collision pipeline для всех фаз layout

Нужно:

- перестать держать две разные реализации placement-geometry для одного и того же `View`
- выделить единый helper/service, который является source of truth для:
  - реального `bbox` вида
  - `frame offset` относительно `Origin`
  - `candidate rect at origin`
  - `center / width / height`
  - candidate state после `dx/dy`
- выделить единый validator для placement-проверок:
  - usable area
  - overlap с `reservedAreas`
  - overlap с другими видами
  - единый `reason / blockers` contract
- перевести на этот pipeline:
  - main layout
  - `EstimateFit`
  - projection post-pass
  - live diagnostics/debug traces

Кандидаты на выделение:

- `ViewPlacementGeometryService`
- `ViewPlacementValidator`

Готово когда:

- layout и projection post-pass больше не считают один и тот же view разной геометрией
- collision decision для одной и той же candidate position одинаков
  в `EstimateFit`, apply и post-pass
- кейс `SectionView` внутри `FrontView` не может повториться из-за divergence
  между layout-path и projection-path
- весь код, который принимает решение `можно ли двигать view`,
  использует один validator, а не локальную копию overlap logic

Первый безопасный вход в этот этап:

- сначала выделить общий internal geometry/validator helper без смены policy
- сначала подключить его в `BaseProjectedDrawingArrangeStrategy`
- только затем тянуть тот же contract в `EstimateFit`
  и projection post-pass

### 2. Свести `EstimateFit` и apply path

Нужно:

- убрать расхождение, при котором `EstimateFit` принимает или отвергает
  layout по одной геометрии, а после apply фактические bbox дают другой результат
- отдельно довести parity для `Top/Bottom` sections, где уже наблюдался
  ложный reject по `no-valid-x`
- логировать и сравнивать одни и те же rect в estimate/apply path
- не считать `1:20` невалидным, если на том же листе этот layout реально помещается

Готово когда:

- `fits=0` не возникает для layout, который потом реально встаёт на лист
- `fits=1` не возникает для layout, который после первой расстановки уже даёт
  overlap/почти-overlap
- `Top/Bottom` section decision совпадает между estimate и apply

### 3. Довести main-layout spacing вокруг BaseView

Нужно:

- использовать один и тот же source of truth для budget window
  в strict/relaxed/diagnostics path
- перестать принимать слишком плотный `Front/Top/Right` каркас как валидный fit
  до projection
- добавить явный spacing reserve для standard neighbors там, где projection
  потом обязан сделать корректирующий move

Готово когда:

- `BaseView` размещается с учётом реальной потребности в месте сверху/снизу/слева/справа
- `TopView` и `Top/Bottom` sections не деградируют только из-за узкого
  centered-slot around `BaseView`
- выбор `baseRect` объясняется через budgets и свободный слот внутри budget-window
- основной каркас после первой расстановки уже имеет безопасные зазоры,
  а не упирается в `projection-skip:view-overlap`
- если standard section не может быть поставлена в свой normal stack,
  следующий допустимый путь деградации всё равно обязан оставаться
  overlap-free; иначе candidate scale должен быть отвергнут, и внешний
  scale loop должен взять следующий, более крупный масштаб

### 4. Довести BaseView-centric topology и явную projection policy

Нужно:

- выделить явный projection graph поверх уже существующего `NeighborSet`
- вынести current resolver order в явную policy:
  `ViewType override -> coordinate systems -> current position`
- перестать держать проекционную конвенцию неявно в коде

Готово когда:

- standard neighbors и dependent relations выводятся из явной topology policy,
  а не только из текущего resolver
- стороны размещения standard neighbors и секций определяются не скрытым соглашением,
  а явной policy

### 5. Отделить oversized sections от normal section policy

Нужно:

- не позволять одному outlier section диктовать весь `optimalScale`
- ввести отдельный degraded path для outlier sections
- отделить oversized geometry fail от normal section planning fail

Готово когда:

- обычный каркас листа не ломается из-за одного проблемного разреза
- oversized section получает объяснимую degraded policy, а не ломает
  normal path для всех остальных видов

### 6. Усилить detail/dependent placement policy

Нужно:

- сделать деградацию detail placement более явной и повторяемой
- сохранить максимальную близость к anchor при нехватке места
- не допускать нелогичных прыжков через лист без явной причины

Готово когда:

- detail и detail-like section ставятся максимально близко к своему anchor
- при нехватке места причина деградации видна в результате

## Валидация

Нужны три уровня проверки.

### Юнит-тесты

- `BaseViewSelection`
- `StandardNeighborResolver`
- `SectionPlacementSide`
- `NeighborRole -> alignment axis`
- naming-based detail-like classification
- anchor-driven detail placement

### Геометрические тесты

- нет overlap между planned views
- нет overlap с reserved areas
- detail/section placement устойчив при повторном запуске
- bbox planner'а совпадает с фактической рамкой вида

### Live-проверка в Tekla

- `DetailMark.GetRelatedObjects()` стабильно связывает detail view
- `SectionMark.LeftPoint/RightPoint` доступны и дают корректный anchor
- фактический runtime после `Modify()/CommitChanges()` сохраняет планируемую топологию

## Критерии приёмки

Система считается рабочей, когда:

- `BaseView` читается как визуальный якорь листа
- standard neighbors и направленные секции стоят логично
- `DetailView` не ломают масштаб и topology основного каркаса
- detail и detail-like section живут рядом со своим реальным anchor
- выбранный масштаб — наибольший, при котором схема влезает
- двойной запуск `fit_views_to_sheet` не приводит к хаосу

## References

Ниже не спецификация проекта, а внешние опорные материалы:

- Mackinlay J. (1986), *Automating the Design of Graphical Presentations of Relational Information*  
  https://doi.org/10.1145/22949.22950
- Graf W. / LayLab (DFKI, 1992–1993)  
  https://www.dfki.de/web/forschung/projekte-publikationen/publikation/6030  
  https://www.dfki.de/web/forschung/projekte-publikationen/publikation/6102
- Christensen J. et al. (1995), *Cartographic Label Placement by Simulated Annealing*  
  https://doi.org/10.1559/152304097782439259
- Zoraster S. (1990), map-label placement / optimization reference  
  http://nrs.harvard.edu/urn-3:HUL.InstRepos:2051370
- Para et al. (ICCV 2021), *Generative Layout Modeling Using Constraint Graphs*  
  https://openaccess.thecvf.com/content/ICCV2021/html/Para_Generative_Layout_Modeling_Using_Constraint_Graphs_ICCV_2021_paper.html
- Dupty et al. (CVPR 2024), *Constrained Layout Generation with Factor Graphs*  
  https://openaccess.thecvf.com/content/CVPR2024/html/Dupty_Constrained_Layout_Generation_with_Factor_Graphs_CVPR_2024_paper.html
