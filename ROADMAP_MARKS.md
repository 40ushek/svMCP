# svMCP — Марки: архитектура и роадмап

## Типы Placing

### LeaderLinePlacing
- Метка свободно летает вокруг якорной точки, лидерная линия тянется
- `StartPoint` — якорь (точка на элементе в координатах вида)
- Движение: в любую сторону
- Overlap resolution: 2D AABB, push в сторону наименьшего перекрытия
- В `get_drawing_marks` теперь дополнительно читаются дочерние `LeaderLine` с:
  - `LeaderLineType` (`NormalLeaderLine`, `SupportLeaderLine`, `ExtensionLeaderLine`)
  - `StartPoint`, `EndPoint`
  - `ElbowPoints`

### BaseLinePlacing
- Метка прикреплена к базовой линии элемента
- `StartPoint` / `EndPoint` — вектор базовой линии в координатах вида (мм модели), но **ненадёжны** для части меток (`length = 0` или ~2 мм)
- Движение: **только вдоль вектора** оси детали
- Overlap resolution: см. текущую реализацию ниже
- Overlap detection: проецировать центры и полуразмеры обеих меток на единичный вектор, сравнивать 1D интервалы

---

## Координатная система

- `mark.GetAxisAlignedBoundingBox()` → мм модели (view-local)
- `view.Width / Height` → мм листа
- `view.Attributes.Scale` → масштаб вида (модель / лист)
- Весь layout работает в **мм листа**: делить model coords на scale при сборе, умножать обратно при записи в `InsertionPoint`

---

## Текущая реализация (`arrange_marks`)

**Реализовано:**
- Координатное несоответствие исправлено: bbox в мм модели смешивался с bounds в мм листа → метки улетали на ~scale×displacement
- В `get_drawing_marks` доступны:
  - `placingType`, `centerX/centerY`, `angle`, `rotationAngle`, `textAlignment`
  - `arrowHead` (`type`, `position`, `height`, `width`)
  - `leaderLines[]` с типом и геометрией
  - `axis` (`start/end`, `dx/dy`, `length`, `angleDeg`, `isReliable`) для диагностики
  - `objectAlignedBoundingBox` (`width`, `height`, `angleToAxis`, `center`, `corners`) — OBB метки

**Источник оси для BaseLinePlacing (обновлено):**
- Сначала пробуем `TryGetRelatedPartAxisInView`: читает реальную ось детали из модели
  - `Beam` → `StartPoint/EndPoint`
  - `Part` → `CoordinateSystem.AxisX`
  - Координаты читаются при активной `TransformationPlane(view.DisplayCoordinateSystem)` → ось в системе вида
- Если успешно: используется реальная ось детали + `ComputeAxisAlignedExtents` пересчитывает AABB из OBB с учётом угла
- Fallback: `BaseLinePlacing.StartPoint/EndPoint`, но `isReliable = false` если `length < 0.001`

**Overlap resolver для BaseLinePlacing (обновлено):**
- Оси нормализуются перед вычислением dot product
- **Параллельные оси** (`|dot| >= 0.95`): проецируем на усреднённый вектор, раздвигаем вдоль него
- **Непараллельные оси** (`|dot| < 0.95`): `TryResolveAlongIndependentAxes` — решает систему 2×2 через определитель:
  - обе подвижны: `moveA`, `moveB` из матричного уравнения, каждая скользит вдоль своей оси
  - одна фиксирована: вторая скользит вдоль своей оси на `ResolveSingleAxisMove`
- Применение в `ApplyPlacements`: после `mark.Modify()` марка перечитывается из Tekla (`TryReloadMarkState`), сдвиг считается только если фактическое смещение > 0.05 мм

**Известные проблемы:**
- Смешанные конфликты `BaseLinePlacing` ↔ `LeaderLinePlacing` пока не оптимальны:
  baseline-метка ограничена осью, leader-line свободна в 2D — специального резолвера нет
- `GetObjectAlignedBoundingBox()` используется для геометрии OBB, но точность зависит от Tekla API

---

## К реализации

### BaseLinePlacing — смешанные случаи и улучшение качества
1. Для конфликтов `BaseLinePlacing` ↔ `LeaderLinePlacing` добавить resolver, который предпочитает двигать leader-line mark, а baseline оставляет на оси
2. ~~Для непараллельных baseline-меток добавить более точный fallback~~ — **реализовано** через `TryResolveAlongIndependentAxes`
3. ~~Перевести baseline geometry с `StartPoint/EndPoint` на более надежную модель~~ — **реализовано** через `TryGetRelatedPartAxisInView`
4. ~~Использовать drawing debug overlay для отрисовки bbox/оси~~ — **реализован** `draw_debug_overlay` / `clear_debug_overlay`

### Конвертация BaseLinePlacing → LeaderLinePlacing
- Если два `BaseLinePlacing` не могут разойтись вдоль вектора (элемент короткий), конвертировать одну метку в `LeaderLinePlacing` с лидерной линией к середине элемента
- Даёт свободу движения в 2D

### Прочее
- Другие типы placing (если выплывут) — добавить сюда
- Возможно: `CanMove = false` для меток вне вида или помеченных пользователем
