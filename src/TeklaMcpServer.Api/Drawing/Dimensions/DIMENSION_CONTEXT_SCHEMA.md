# Dimension Context and Case Schema

Status: proposed persisted-case contract; not the current runtime JSON contract

This document defines what should be persisted when the system reads, creates,
or repairs drawing dimensions. It is deliberately separate from the runtime
DTOs (`DimensionContextInfo`, `DrawingContext`, and related types): runtime DTOs
may evolve with the implementation, while persisted cases need a versioned and
stable contract.

## Implementation boundary

This document is a target contract for persisted observations and cases. It is
not evidence that every field below already exists in `get_dimension_contexts`.

Implemented in the current runtime read model:

- dimension and segment diagnostics;
- measured points, geometry, lengths, and warnings;
- related-source evidence;
- `associationSource` with live-inference and snapshot-fallback values;
- point association status, selected candidate, distance, nearest geometry
  point, candidate count, and point warning.

Still proposed for persisted cases and not currently emitted by the runtime
read model:

- stable `sourceFingerprint` and dimension fingerprint;
- `anchorKind` and native associativity semantics;
- confidence as a first-class numeric field;
- the `invalid` and `unassociated` status values;
- the versioned observation/decision/action/verification storage layout.

The local `cases/dimension_cases` set was deleted on 2026-08-01 and the folder
is empty. It predated this schema — no anchors, no candidate points, no segment
relations, and lengths computed by a formula that has since been corrected — so
re-capturing in the current format was judged cheaper than migrating it.

Two of its lessons should survive into whatever replaces it. Record explicitly
whether a human passed over the chains, and what they changed: a drawing that
merely happened to be open is not reference material. And capture the `before`
state first and confirm it is on disk, because three of the six previous cases
had `before` states that existed nowhere else after the drawing was edited and
saved.

## Core decision

A dimension point is not just a coordinate. The canonical semantic record is:

```text
point -> association -> source object -> anchor kind
```

Coordinates, reference lines, and bounds remain necessary geometry evidence, but
they are not a substitute for the association. A future LLM workflow should
select source objects and anchors first and let the executor resolve current
geometry from them.

## Agreed v1 scope

Agreed 2026-08-01. This is the minimum an observation must carry to be usable,
and the boundary of what is being built now. A separate model, retrieval and
stable fingerprints are deliberately out of scope until the context itself
agrees with the drawing.

### Drawing identity

- drawing type — **gates everything else.** The thinning rules apply to assembly
  drawings only; on a single-part drawing they would remove exactly the
  dimensions that drawing exists to show, and a GA drawing has a different
  subject again. An observation without this field cannot be interpreted;
- assembly mark and prefix (`RE`, `EW`) — read once per drawing, not per part:
  every part returns the same value and each read costs a `Select()`. This is
  how a comparable example is retrieved;
- units and Tekla version — without them an observation stops being readable
  once either changes.

### View context

- view id, type, scale;
- view bounds;
- parts with model ids;
- part geometry in the view coordinate system;
- extreme points of each part: top, bottom, left, right;
- part type, profile, material, **`partPrefix` and `materialType`**.

`partPrefix` and `materialType` are not decoration: candidate filtering runs on
them — `T` timber is structural, `M` fittings and `R` insulation are not.
Material as a string is project-specific naming; the prefix and the numeric type
are not.

Extreme points are the bounding box, and for an inclined part the box is not the
part: a raked top plate measures 383 mm tall by its box while the member itself
is 45 mm. Anything reasoning about faces must use the axis or the solid, not the
extremes.

### Dimension context

- every dimension set and segment, carrying every field of the agreed v1
  contract. Not everything Tekla holds: the printed run is absent, as are the
  full dimension attributes;
- points, direction, lengths, offset;
- related objects per point;
- dimension type and role;
- occupied zones and neighbouring dimensions.

Two fields need naming discipline rather than a single word:

**Lengths.** Three different quantities exist and have already been confused for
each other. `LengthList` is the cumulative span projected onto the dimension
axis, index-aligned with the point list. `RealLengthList` is the straight-line
distance between points. The absolute run actually printed on the sheet is
**not present in the context at all** — see the known gap in
`ROADMAP_DIMENSIONS.md`. An observation must not imply it has the printed values.

**Role.** `external` / `internal` / `control` is the intent, but the current
classifier does not deliver it: everything except the control diagonal comes back
`External`, so an overall dimension and an internal chain are indistinguishable.
Until that is fixed the field must be recorded as unreliable, or consumers will
build on it.

**Occupied zones** stays OPTIONAL until its semantics are pinned down — occupied
relative to what, and in which space. A required field with an undefined meaning
gets filled anyway, each producer in its own way, and the disagreement is
invisible afterwards. Omit it rather than guess.

### Coordinates

- all working coordinates are in the view coordinate system;
- sheet coordinates are for layout only;
- one saved observation covers both the view context and the dimensions;
- a reduced context may be derived for a model, but the raw data is kept.

### Storage note

Part geometry does not change between dimension edits — verified byte-for-byte
across four states of one view. A before/after pair therefore carries the same
tens of kilobytes twice. An observation should be allowed to reference a shared
parts payload rather than embedding its own copy.

A shared payload must carry a **version or content hash**, and the referencing
observation must record it.

Compare observations by `viewContext`, `dimensionContext` and `partsPayloadHash`
— **not by the whole header.** The drawing identity carries issue status and
dates (`isIssuedButModified`, `modificationDate`, `issuingDate`) that change from
handling the drawing at all, so two observations of untouched dimensions still
differ in the header. Diffing it whole reports noise as change.

Hash the **stored bytes**, not the object: SHA-256 over the parts payload exactly
as it is serialized and written. That removes the canonicalization question
entirely — no property ordering, whitespace or number formatting rule has to be
agreed, because the artifact being hashed is the one being referenced. A payload
re-serialized with different settings is a different artifact and correctly gets
a different hash. Sharing is only sound while the geometry really is
identical; the model can be edited between two reads, and a reference without a
hash would then silently pair dimensions with parts they were never read
against. The hash makes that mismatch detectable instead of invisible.

## Two persisted payloads

### Observation

An observation records what was read from Tekla at a particular moment:

- drawing identity, Tekla version, units, and schema version;
- view geometry, scale and view relationships. **View geometry is in; sheet
  placement is not** — where a view sits on the sheet changes with layout while
  dimensions live in view coordinates, so including it would make two
  observations of an unchanged drawing differ;
- existing dimension sets and segments;
- measured points, reference/lead lines, text bounds, and warnings;
- source objects and per-point associations.

The observation must preserve provenance. Every association and geometry fact
must say whether it came from a native Tekla value, an inferred mapping, or an
unknown/unavailable source. Every coordinate-bearing payload must also record
its coordinate space and how it was obtained. At minimum, use values such as:

```json
{
  "coordinateSpace": "view",
  "coordinateSystemSource": "ViewCoordinateSystem",
  "projection": "model_work_plane_before_geometry_read"
}
```

Geometry read through the existing part-geometry path is already in the
owning view coordinate system. A later `view -> sheet` projection is a
presentation conversion and must not be mixed into point-to-source matching.

### Case

A case records an intentional operation and its outcome:

```json
{
  "schemaVersion": 1,
  "request": {
    "intent": "add_missing_dimensions",
    "targetViewId": 42,
    "constraints": ["outside_view", "horizontal_chain"]
  },
  "before": { "observationRef": "observation-before.json" },
  "decision": {
    "selectedSources": ["model:123", "model:456"],
    "direction": "horizontal",
    "distance": 40.0
  },
  "actions": [],
  "after": { "observationRef": "observation-after.json" },
  "diff": {},
  "verification": {
    "success": true,
    "warnings": [],
    "humanAccepted": false
  }
}
```

`decision` is what the model proposed. `actions` are what the executor
actually sent to Tekla. They must not be collapsed into one field.

## Dimension point association

Each persisted dimension point should contain at least:

```json
{
  "order": 2,
  "position": { "x": 120.0, "y": 450.0 },
  "association": {
    "status": "matched",
    "source": "inferred",
    "selected": {
      "modelId": 12345,
      "drawingObjectId": 678,
      "objectType": "Beam",
      "anchorKind": "endpoint"
    },
    "candidates": [],
    "confidence": 0.98,
    "warnings": []
  }
}
```

Recommended association statuses:

- `matched`
- `ambiguous`
- `no_candidates`
- `no_geometry`
- `invalid`
- `unassociated`

`source` must distinguish `native`, `inferred`, and `unknown`. `native` is only
valid when Tekla exposes the selected associativity rule itself. A list of
related objects plus a geometric match is `inferred`, even when the inference
is strong.

Object IDs are useful for the current model but are not sufficient as long-term
identity. Persist a stable source fingerprint as well, based on object kind,
model identity where available, and the relevant geometry/role. Dimension IDs
are transient and must not be the primary case key.

## Dimension-level record

The dimension record should contain:

- transient `dimensionId` and `segmentIds` for diagnostics;
- stable dimension fingerprint;
- `viewId`, view type, scale, dimension type, and source kind;
- coordinate space and coordinate-system provenance for every point/geometry
  collection;
- point list and per-point associations;
- reference line, lead lines, distance, bounds, and text geometry;
- geometry and association warnings;
- the source references used to reconstruct the dimension.

The current `LengthList` name is retained for compatibility. In persisted
contracts it should be described as projected cumulative spans aligned with the
point list, not as the absolute printed run shown on the sheet. It must not be
used to diagnose the drawing or to claim that the displayed dimension values
are wrong. A future schema may expose clearer names such as
`projectedCumulativeSpans` and `pointToPointSpans`.

The current `get_dimension_contexts` runtime payload exposes this evidence as
`associationSource`, `pointAssociations[].point`,
`pointAssociations[].nearestGeometryPoint`, and
`pointAssociations[].candidateCount`. Its live source is explicitly marked as
`live_related_objects_geometry_inference`: it uses related objects from the
dimension set/segments and a geometry match, not Tekla's selected associativity
rule. Snapshot-only reads are marked as `snapshot_source_references`.

## Case storage layout

The first storage format should remain inspectable and diffable:

```text
case/
  meta.json
  observation-before.json
  decision.json
  actions.json
  observation-after.json
  diff.json
  verification.json
```

Raw Tekla dumps may be kept beside the normalized observation for debugging,
but the normalized observation is the contract used for retrieval and model
input. Large repeated geometry should not be copied into every derived index.

## Retrieval and learning rules

- Retrieve examples by intent, view type, dimension type, source/anchor kinds,
  and failure mode, not by raw coordinates alone.
- Prefer human-accepted cases for few-shot context.
- Keep rejected and failed cases only when the failure reason is explicit.
- Treat warnings and ambiguity as input to the decision, not as incidental logs.
- Verify the post-operation associations and geometry before marking a case
  successful.

The first consumer should be retrieval-augmented planning. Fine-tuning should
wait until there is a substantial set of accepted cases with reliable
before/after verification.

## References

- [Tekla: Display and change dimension point associativity](https://support.tekla.com/doc/tekla-structures/2024/dra_change_dimension_point_associativity)
- [Tekla Open API: StraightDimension properties](https://developer.tekla.com/doc/tekla-structures/2025/straight-dimension-properties-50116)
- [Tekla Open API: StraightDimensionSet properties](https://developer.tekla.com/doc/tekla-structures/2026/straight-dimension-set-properties-69049)
