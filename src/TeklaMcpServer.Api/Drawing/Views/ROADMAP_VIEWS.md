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
  Внутренние blocked/reserved areas внутри окна тоже учитываются с одним `gap`,
  без прежней двойной инфляции.
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

### Что осталось вне roadmap

Основной roadmap по `Views` закрыт. Ниже не незавершённые стадии,
а остаточные follow-up темы вне текущего плана:

- низкоприоритетный perf-долг:
  `MaxRects`-packer для поиска `baseRect` создаётся заново для каждого
  candidate window
- возможная дополнительная policy-полировка degraded placement,
  если реальные Tekla-прогоны покажут слабые композиционные кейсы
- возможный пересмотр preserve-scale агрегата (`Max()` vs `Min()`)
  в `ShouldSkipProjectionAlignment`, если это подтвердится на live-сценариях
- локальные debug-hooks, например
  `SVMCP_FIT_DEBUG_STOP_ON_SECTION_REJECT`, остаются только как
  инструменты диагностики и не считаются частью production contract

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
  завершён.
  `strategy`, `EstimateFit`, apply и projection post-pass уже сведены
  к одному geometry/validator source of truth; reject reasons и parity traces
  унифицированы.
- `Stage 3: behavioral fixes`:
  завершён.
  Закрыты `BaseView` spacing / main-skeleton hardening, `Top/Bottom`
  bounded-band decision, oversized standard sections и repeated-fit
  stabilization для standard neighbors/sections.
- `Stage 4: policy polish`:
  завершён.
  Topology/projection policy выделена как явный internal слой,
  detail/dependent placement использует единый anchor-driven decision shape,
  `ProjectionMethod` зафиксирован как internal policy model.

Итог:

- roadmap по `Views` завершён по коду
- дальнейшая работа идёт уже как live-validation, regression fixing
  и low-priority polish, а не как незакрытые roadmap-стадии

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
  - `UniformAllNonDetail` использует все non-detail views,
    кроме oversized standard sections
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
- `Top/Bottom` секции используют bounded horizontal shift внутри
  preferred/degraded band:
  сначала centered candidate, затем допустимые смещения по `X` внутри того же band
- `EstimateFit` и apply для `Top/Bottom` sections используют один и тот же
  probe/result contract и одинаковые reject reasons

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

## Следующий режим работы

Roadmap-этапы больше не активны. Дальше работа по `Views` идёт в таком порядке:

### 1. Live-validation в Tekla

- прогон реальных чертежей с standard neighbors
- прогон листов с `Top/Bottom` sections
- прогон листов с detail/dependent views и `SectionMark` anchor
- повторный `fit_views_to_sheet` на тех же листах для проверки стабильности

### 2. Regression fixing

- любые найденные отклонения фиксируются как точечные runtime/regression bugs,
  а не как новые roadmap-стадии
- приоритет:
  collision/parity regressions -> semantic placement regressions ->
  composition polish

### 3. Low-priority polish

- perf-улучшения без смены policy
- возможная локальная корректировка preserve-scale semantics
- trace/diagnostics cleanup по результатам живых прогонов

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

На текущий момент именно этот раздел является главным источником
дальнейшей валидации: roadmap по реализации уже закрыт, дальше важна
проверка на реальных drawings.

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
