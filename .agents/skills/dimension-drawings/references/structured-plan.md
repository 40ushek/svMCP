# Shared view context and creation

The experimental preview/apply commands were removed. Use `create_dimension`;
do not request approval tokens or reconstruct their old plan format.

1. On a bridge exposing `get_view_dimension_context`, read
   `questions="points,edges,scale"` with required `sides` (or `all`).
   Request `parts` only when needed. Completeness, main-part identity and
   exclusions accompany the answer.
2. Each side lists real XY points with `supports`: owner, kind, hole flag
   when true and `partExtentAlongChain`. Merged points can have several owners:
   use evidence for the intended part. Ownerless GroupExtent anchors are valid.
3. Select measurements by plant rules; compact output does not choose points.
   Read existing dimensions to avoid duplicating chains.
4. Call `create_dimension` with selected XYZ points, direction and the SAME
   `excludePrefixes`/`excludeMaterials`. Omit `distance` for the default
   8 paper mm gap, or set `paperGapMm`. Explicit `distance` retains manual
   view units. Never pass both. Custom vectors require explicit distance.
5. Inspect write state and rendered-line result, then reread dimensions for
   neighbours and row/point checks. Missing or `not verified` observation does
   not prove the line was drawn at the calculated location. After an uncertain
   write, inspect actual dimensions before retrying.

A `placement` question accepts points/direction/gap and calculates without
writing; it is optional, not a mandatory preview. Creation uses the same calculator.

The persistent bridge retains snapshots per normalized filter scope for the
active drawing/view. Dimensions do not invalidate them. External geometry,
scale or view edits require `refresh=true`. Switching drawing or queried view,
or restarting the bridge, loses the snapshot; first use builds it. No automatic
external-edit detection is promised.

`get_structural_chain_positions` remains the compatibility/full-evidence route
(`verbose=true` for original supports), sharing the snapshot and refresh policy.
On an older deployed bridge use that route; source files do not prove deployment.
Both routes retain the accepted full-solid projection limitation on section/end views.
