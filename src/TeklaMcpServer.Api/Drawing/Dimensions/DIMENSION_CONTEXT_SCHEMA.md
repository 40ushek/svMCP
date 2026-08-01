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

The local `cases/dimension_cases` corpus predates this schema and is ignored by
the repository. Treat it as generation 0: raw evidence for discussion and
manual review, not as schema-valid training or retrieval data. Its
`dimensionId` values are transient diagnostics only. A migration must add a
schema version, stable fingerprints, and verification before the corpus can be
used as contract data.

## Core decision

A dimension point is not just a coordinate. The canonical semantic record is:

```text
point -> association -> source object -> anchor kind
```

Coordinates, reference lines, and bounds remain necessary geometry evidence, but
they are not a substitute for the association. A future LLM workflow should
select source objects and anchors first and let the executor resolve current
geometry from them.

## Two persisted payloads

### Observation

An observation records what was read from Tekla at a particular moment:

- drawing identity, Tekla version, units, and schema version;
- sheet and view geometry, scale, origin, and view relationships;
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
