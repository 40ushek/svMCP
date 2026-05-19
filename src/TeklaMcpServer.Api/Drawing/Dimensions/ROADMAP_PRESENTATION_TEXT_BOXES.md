# Dimension Presentation Text Boxes Roadmap

## Purpose

This roadmap covers how `Drawing/Dimensions` should obtain text boxes for
dimension annotations from Tekla drawing presentation primitives.

The goal is to move from one-box, type-specific heuristics toward a shared
text-box collection model that can represent what Tekla actually draws.

This roadmap is scoped to text geometry and collision inputs. It does not
replace the line-first dimension domain model described in
`ROADMAP_DIMENSIONS.md`.

## Problem Statement

Current text-box handling is split by dimension type:

- straight dimensions primarily use runtime child objects from `GetObjects()`
  and `GetObjectAlignedBoundingBox()`
- straight dimensions fall back to analytical text-box construction
- angle dimensions use presentation `TextPrimitive` as the primary source,
  with analytical fallback
- radius and other dimension types are not yet covered by the same model

This is not enough for reliable collision handling because one dimension can
draw more than one text object. Straight dimensions can have absolute text
labels, secondary labels, tags, or other drawn text that should each produce a
separate collision box.

## Target Model

Introduce one internal text-box model for all dimension types:

```text
DimensionTextBox
- DimensionId
- SegmentId?
- DimensionKind
- Text
- Polygon
- Bounds
- Source
- Confidence
```

`Source` should identify where the box came from:

```text
Presentation
RuntimeObjects
AnalyticalFallback
```

`Confidence` should identify why the box was accepted:

```text
PresentationTextPrimitive
ExactText
SingleCandidate
Fallback
```

The core rule is: a dimension may produce zero, one, or many text boxes.

## Target Architecture

Add a shared collector facade:

```text
DimensionTextBoxProvider
  GetTextBoxes(DrawingObject dimension, View view) -> IReadOnlyList<DimensionTextBox>
```

Internally the provider should use source-specific collectors:

```text
DimensionPresentationTextBoxCollector
DimensionRuntimeTextBoxCollector
DimensionAnalyticalTextBoxFallback
```

The provider should prefer presentation data, but keep the existing runtime and
analytical paths as fallbacks until presentation behavior is validated across
real drawings.

## Presentation Collector

`DimensionPresentationTextBoxCollector` should:

- call `GetObjectPresentation(objectId)`
- recursively traverse `Segment`, `PrimitiveGroup`, and nested primitives
- collect every `TextPrimitive`
- build an oriented text polygon from:
  - `TextPrimitive.Position`
  - `TextPrimitive.Angle`
  - `TextPrimitive.Height`
  - `TextPrimitive.Proportion`
  - owning view scale
- keep the original text value
- preserve the source object id used for presentation lookup

Important coordinate rule:

- presentation text primitive coordinates are treated as paper space
- multiply by the owning view scale to compare with existing view-space
  dimension geometry and debug overlays

## Migration Plan

### Phase 1: Shared Presentation Collector

Implement the presentation collector without changing production placement
logic.

Use it only from existing diagnostics/debug drawing first.

Expected output:

- all text primitive boxes for a dimension set
- all text primitive boxes for a dimension segment
- all text primitive boxes for an angle dimension
- debug labels with text, source object id, and source type

### Phase 2: Straight Dimensions Debug Comparison

Extend the existing `DrawDimensionTextBoxes` diagnostics path so straight
dimensions can draw and compare:

- presentation boxes
- runtime `GetObjects()` boxes
- analytical fallback boxes

This phase should answer:

- whether presentation on `StraightDimensionSet` is enough
- whether segment-level presentation is also needed
- how absolute text labels appear
- whether text primitive sizes match runtime text boxes

### Phase 3: Straight Dimensions Production Switch

After validation, make straight dimensions use:

1. presentation text boxes
2. runtime child-object boxes
3. analytical fallback

The production result should no longer assume one text box per segment.

Existing `TextBounds` can remain as a legacy summary if needed, but collision
logic should consume the full list.

### Phase 4: Angle Dimension Integration

Replace the angle-specific presentation traversal in
`DimensionAngleTextPolygonHelper` with the shared presentation collector.

Keep the angle-specific analytical fallback because angle dimensions need
custom fallback geometry.

### Phase 5: Radius and Arc Dimensions

Before writing analytical logic for radius or arc dimensions, inspect their
presentation primitives first.

The first deliverable should extend the existing debug drawing path to draw
their presentation `TextPrimitive` boxes and report primitive types.

Only add analytical fallback if presentation is missing or incomplete.

### Phase 6: Collision Inputs

Collision detection should consume:

```text
IReadOnlyList<DimensionTextBox>
```

not:

```text
DimensionSegmentInfo.TextBounds
```

This is required because one dimension can have multiple visible text boxes.

Movement policy must stay separate from text-box collection. A valid collision
box does not imply that Tekla Open API can move that dimension type.

## Open Questions

- Does `GetObjectPresentation(dimensionSetId)` include all segment text labels?
- Do absolute labels appear on the dimension set presentation, segment
  presentation, or both?
- Are `TextPrimitive.Height` and `TextPrimitive.Proportion` stable across fonts
  and dimension styles?
- Do text frames require extra padding beyond the primitive size?
- Which non-straight dimension types expose reliable text primitives?
- Should legacy `TextBounds` remain in public DTOs as a union/summary box, or
  should a new public `TextBoxes` list be added?

## Non-goals

- Do not replace line-first dimension grouping with text-box heuristics.
- Do not use text boxes as the primary dimension identity.
- Do not remove runtime child-object or analytical fallbacks until presentation
  is validated.
- Do not infer move support from text-box availability.

## Initial Recommendation

Start by extending the existing `DrawDimensionTextBoxes` debug path instead of
adding a separate command.

The debug drawing should draw every presentation text box and label it with:

- dimension id
- source object id
- text
- source kind

Only after this is validated on real drawings should production straight
dimension text-box logic switch to the presentation-first provider.
