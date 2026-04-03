# Drawing Dimensions

## Purpose

`Drawing/Dimensions` is the line-first dimension module for drawing runtime.

It is responsible for:

- reading existing `StraightDimensionSet` objects from the active drawing
- projecting Tekla runtime data into the internal dimension domain model
- grouping and reducing dimensions for analysis
- planning and applying spacing/offset adjustments
- creating, moving and deleting dimensions
- control-diagonal placement
- text/debug support for dimension inspection

The canonical legacy reference for domain semantics is `D:\repos\svMCP\dim`.
That project is a reference for vocabulary and heuristics, not for direct
architectural copying.

## Current Module Shape

Facade/API files kept at the root of this folder:

- `TeklaDrawingDimensionsApi.cs`
- `TeklaDrawingDimensionsApi.Query.cs`
- `TeklaDrawingDimensionsApi.Commands.cs`
- `TeklaDrawingDimensionsApi.Arrangement.cs`
- `IDrawingDimensionsApi.cs`

Shared public DTO/value files kept at the root:

- `DrawingDimensionInfo.cs`
- `DimensionGeometryKind.cs`
- `DimensionSourceKind.cs`
- `DimensionType.cs`
- `CreateDimensionResult.cs`
- `PlaceControlDiagonalsResult.cs`

Internal layers:

- `Grouping/`
  - `DimensionItem`
  - `DimensionGroup`
  - `DimensionGroupFactory`
  - `DimensionOperations`
  - grouping/reduction policies and debug DTOs
- `Arrangement/`
  - spacing analysis
  - arrangement planning
  - distance-adjustment translation
  - arrangement debug/apply result types
- `Placement/`
  - projection helpers
  - placement heuristics
  - text placement and fallback polygon helpers
  - create-dimension placement helpers
  - control-diagonal placement helpers
  - text value formatting and text attribute mapping helpers

## Processing Flow

The current internal flow is:

`Tekla runtime snapshot -> grouping/reduction -> arrangement planning -> placement math -> commands/debug`

In practical terms:

1. `Query` reads dimensions and view ownership from Tekla.
2. `Grouping` turns runtime data into `DimensionItem` / `DimensionGroup`.
3. `Arrangement` analyzes stacks/gaps and plans `Distance` changes.
4. `Placement` owns geometry/text placement helpers.
5. `Commands` and debug endpoints expose runtime actions.

## Public MCP Tools Today

Exposed through `TeklaMcpServer/Tools`:

- `get_drawing_dimensions`
- `move_dimension`
- `create_dimension`
- `delete_dimension`
- `place_control_diagonals`
- `draw_dimension_text_boxes`

These are the supported public tool-surface operations for dimensions.

## Internal / Bridge-Only Debug Surface

Available in `TeklaBridge`, but not currently surfaced as public MCP tools:

- `get_dimension_text_placement_debug`
- `get_dimension_source_debug`
- `get_dimension_groups_debug`
- `get_dimension_arrangement_debug`

Arrangement apply logic also exists internally in `TeklaDrawingDimensionsApi`,
but there is no public MCP `arrange_dimensions` tool at the moment.

## Document Split

Use this file as the operational description of the module:

- current structure
- current responsibilities
- what is public vs internal

Use [ROADMAP_DIMENSIONS.md](D:\repos\svMCP\src\TeklaMcpServer.Api\Drawing\Dimensions\ROADMAP_DIMENSIONS.md)
as the strategic document:

- what is still being aligned to `dim`
- what remains intentionally deferred
- what future functionality should be exposed later
