# Project Drafting Rules

- Диагональные размеры: это размеры между крайними дальними точками сборки; используются для контроля геометрии.
- Для layout-таблиц канонический источник видимых границ: `Segment.Primitives[0/2]` из presentation model.
- Контракт: `Primitives[0]` = min-corner marker, `Primitives[2]` = max-corner marker.
- Не заменять marker-based path общей аккумуляцией примитивов без явного доказательства, что canvas markers недоступны для конкретного runtime/template.
