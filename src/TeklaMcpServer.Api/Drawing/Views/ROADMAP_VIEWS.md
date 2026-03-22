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
- planner больше не стартует от прямого `FrontView` lookup.
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
- `EndView` больше не обязан уходить в residual:
  если topology resolver классифицирует его как `SideNeighborRight`,
  он получает явный правый neighbor slot в main layout.

### Что ещё не закончено

- `BaseViewSelection` всё ещё имеет `FrontView`-centric fallback shortcut.
- явный projection graph в коде ещё не выделен.
- `ProjectionMethod` ещё не стал явным параметром.
- oversized sections пока не вынесены в отдельную degraded policy.
- local scale reduction для outlier section пока нет.
- repeated `fit_views_to_sheet` ещё не гарантированно идемпотентен на всех листах.
- detail placement уже anchor-aware, но policy всё ещё можно улучшать:
  при нехватке места нужна более явная и объяснимая деградация.

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

### Details

- `DetailView` не влияет на global scale selection
- `DetailView` не конкурирует за ранние проекционные слоты
- он размещается после основного base/section каркаса
- основная метрика — близость к `DetailMarkAnchor`
- fallback в residual placement допустим только как явная деградация

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

## Ближайшие шаги

### 1. Довести BaseView-centric topology

Нужно:

- выделить явный projection graph поверх уже существующего `NeighborSet`
- перестать держать role resolution неявно внутри resolver heuristics

Готово когда:

- standard neighbors и dependent relations выводятся из явной topology policy,
  а не только из текущего resolver

### 2. Отделить oversized sections от normal section policy

Нужно:

- не позволять одному outlier section диктовать весь `optimalScale`
- ввести отдельный degraded path для outlier sections

Готово когда:

- обычный каркас листа не ломается из-за одного проблемного разреза

### 3. Усилить detail/dependent placement policy

Нужно:

- сделать деградацию detail placement более явной и повторяемой
- сохранить максимальную близость к anchor при нехватке места
- не допускать нелогичных прыжков через лист без явной причины

Готово когда:

- detail и detail-like section ставятся максимально близко к своему anchor
- при нехватке места причина деградации видна в результате

### 4. Сделать projection method явной конфигурацией

Нужно:

- перестать держать проекционную конвенцию неявно в коде

Готово когда:

- стороны размещения standard neighbors и секций определяются не скрытым соглашением,
  а явной policy

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
