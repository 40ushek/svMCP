# Historical View Layout Roadmap

Status: implemented / superseded as the active roadmap.

Current active roadmap:

- `ROADMAP_DRAWING_LAYOUT.md`

This file is kept as a compact history of the implemented `fit_views_to_sheet`
algorithm and its invariants. New drawing-layout architecture work should go
into `ROADMAP_DRAWING_LAYOUT.md`.

## Implemented Behavior

- `fit_views_to_sheet` is the supported view-layout path.
- `DrawingViewArrangementSelector` prefers `BaseProjectedDrawingArrangeStrategy`.
- `BaseViewSelection` is explicit.
- Standard projected neighbors are modeled through:
  - `NeighborSet`
  - `StandardNeighborResolver`
  - `NeighborRole`
- View semantics are split into:
  - `BaseProjected`
  - `Section`
  - `Detail`
  - `Other`
- Standard neighbors are resolved relative to the selected `BaseView`.
- Resolver order for standard neighbors is:
  - `ViewType` override
  - coordinate systems
  - current sheet position
- `SectionPlacementSide` is explicit:
  - `Left`
  - `Right`
  - `Top`
  - `Bottom`
  - `Unknown`
- Section projection alignment follows placement side:
  - `Left/Right -> Y`
  - `Top/Bottom -> X`
- Detail views do not drive global scale selection.
- Detail and detail-like section placement is anchor-driven and happens after
  the main base/projected/section skeleton.
- Standard scale candidates use the current standard scale row:
  `1:1, 1:2, 1:5, 1:10, 1:15, 1:20, 1:25, 1:30, 1:40, 1:50, 1:60, 1:70, 1:75, 1:80, 1:100, 1:125, 1:150, 1:175, 1:200, 1:250, 1:300`.
- Candidate-fit reads actual frame sizes after Tekla scale probes.
- Reserved table areas are respected during fit and projection validation.
- Projection alignment is skipped for mixed-scale non-detail sets when scale
  spread exceeds the configured tolerance.
- Keep-scale paths use actual frame offsets for projection collision checks.
- `ViewPlacementValidator` is the shared out-of-bounds / reserved-overlap /
  view-overlap validator.

## Fixed Contracts

- Parser/bridge default for `fit_views_to_sheet`:
  - `ApplyMode = FinalOnly`
  - `gap = 4 mm`
- API-level default in `FitViewsToSheet(...)` remains `DebugPreview`.
  This is a known layer distinction.
- `MaxRects` and shelf packing are fallback mechanisms, not the semantic core.
- `Origin` is an apply/runtime mechanism. Frame/bbox geometry is canonical for
  layout checks.

## Remaining Work

Active follow-up work moved to `ROADMAP_DRAWING_LAYOUT.md`.

The next work is context migration, not a new layout-policy rewrite:

- introduce `DrawingLayoutWorkspace`
- introduce lightweight `DrawingLayoutViewItem` entries
- move scattered lookup dictionaries into the planning context
- make projection alignment consume lightweight projection signals
- keep full `DrawingViewContext` reserved for dimensions/marks

## Validation Targets

- real assembly drawings with standard projected neighbors
- GA drawings with grid-axis projection alignment
- drawings with top/bottom and left/right section views
- drawings with detail and detail-like section views
- repeated `fit_views_to_sheet` on the same drawing
- reserved table/title-block avoidance
