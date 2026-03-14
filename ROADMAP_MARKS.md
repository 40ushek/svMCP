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
- `StartPoint` / `EndPoint` — вектор базовой линии в координатах вида (мм модели)
- Движение: **только вдоль вектора** StartPoint→EndPoint
- Overlap resolution: проекция bbox на вектор, push вдоль вектора
- Overlap detection: проецировать центры и полуразмеры обеих меток на единичный вектор,
  сравнивать 1D интервалы

---

## Координатная система

- `mark.GetAxisAlignedBoundingBox()` → мм модели (view-local)
- `view.Width / Height` → мм листа
- `view.Attributes.Scale` → масштаб вида (модель / лист)
- Весь layout работает в **мм листа**: делить model coords на scale при сборе, умножать обратно при записи в `InsertionPoint`

---

## Текущая реализация (`arrange_marks`)

**Исправлено:**
- Координатное несоответствие: bbox в мм модели смешивался с bounds в мм листа → метки улетали на ~scale×displacement
- Фикс: все координаты меток делятся на `view.Attributes.Scale` перед layout, умножаются обратно при `InsertionPoint`
- В `get_drawing_marks` доступны:
  - `placingType`
  - `arrowHead` (`type`, `position`, `height`, `width`)
  - `leaderLines[]` с типом и геометрией
  - `centerX/centerY`
  - `angle`, `rotationAngle`, `textAlignment`
  - `axis` (`start/end`, `dx/dy`, `length`, `angleDeg`) для диагностики
- Для `BaseLinePlacing`:
  - читается ось `StartPoint -> EndPoint`
  - candidate generation идет только вдоль этой оси
  - overlap resolver для параллельных baseline-меток раздвигает их вдоль оси
  - применение placement к `InsertionPoint` проецируется на ось, чтобы метка не уходила поперек текста

**Известные проблемы:**
- Диагностика показала, что `BaseLinePlacing.StartPoint/EndPoint` для части меток ненадежны:
  встречаются `length = 0` и служебные оси длиной ~2 мм, которые не соответствуют реальной длине/геометрии текста
- Для baseline-меток `GetAxisAlignedBoundingBox()` и `BaseLinePlacing.StartPoint/EndPoint` пока нельзя считать надежной моделью визуального footprint текста
- Для построения более точной collision geometry, вероятно, придется опираться на `angle/rotationAngle` и отдельный debug overlay, а не только на `bbox + baseline points`
- Смешанные конфликты `BaseLinePlacing` ↔ `LeaderLinePlacing` все еще могут решаться неидеально:
  baseline-метка ограничена своей осью, а leader-line метка свободна в 2D
- Для непараллельных baseline-меток fallback всё еще ближе к обычному 2D overlap resolution

---

## К реализации

### BaseLinePlacing — смешанные случаи и улучшение качества
1. Для конфликтов `BaseLinePlacing` ↔ `LeaderLinePlacing` добавить resolver, который предпочитает двигать leader-line mark, а baseline оставляет на оси
2. Для непараллельных baseline-меток добавить более точный fallback вместо общего 2D push
3. Перевести baseline geometry с `StartPoint/EndPoint` на более надежную модель, основанную на `angle/rotationAngle` и визуальном overlay
4. Использовать общий drawing debug overlay для отрисовки bbox, оси, anchor и displacement в режиме разработки

### Конвертация BaseLinePlacing → LeaderLinePlacing
- Если два `BaseLinePlacing` не могут разойтись вдоль вектора (элемент короткий), конвертировать одну метку в `LeaderLinePlacing` с лидерной линией к середине элемента
- Даёт свободу движения в 2D

### Прочее
- Другие типы placing (если выплывут) — добавить сюда
- Возможно: `CanMove = false` для меток вне вида или помеченных пользователем
