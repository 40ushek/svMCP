# Roadmap Refactor Large Files

## Goal

Reduce the largest drawing-related source files into smaller, focused files
without changing runtime behavior.

This roadmap is for mechanical refactoring only. It should make future layout,
dimension, and bridge-command changes easier to review and safer to modify.

## Current Hotspots

Largest files found during the first audit:

```text
2556  TeklaMcpServer.Api/Drawing/ViewLayout/BaseProjectedDrawingArrangeStrategy.cs
2046  TeklaMcpServer.Api/Drawing/ViewLayout/TeklaDrawingViewApi.Layout.cs
1933  TeklaMcpServer.Api/Drawing/Dimensions/TeklaDrawingDimensionsApi.Commands.cs
1383  TeklaBridge/Commands/DrawingCommandHandler.Dimensions.cs
1266  TeklaMcpServer.Api/Algorithms/Marks/ForceDirectedMarkPlacer.cs
1192  TeklaMcpServer.Api/Filtering/Common/FilterHelper.cs
1066  TeklaMcpServer.Api/Drawing/ViewLayout/ProjectedGroupLayoutPlanner.cs
1039  TeklaMcpServer.Api/Drawing/Dimensions/TeklaDrawingDimensionsApi.cs
```

## Rules

- Do not change public MCP tool names or bridge command names.
- Do not change algorithm behavior while moving code.
- Prefer existing `partial` class patterns already used in the project.
- Keep one mechanical split per commit.
- Do not mix formatting cleanup with code moves unless required by the move.
- Preserve comments and diagnostics unless they become misleading after the move.
- Run build after each step.

## Recommended Order

Before each phase, inspect the target file and group methods by actual
responsibility. File names below are examples, not a fixed contract.

### Phase 1: TeklaDrawingViewApi Layout Split

Status: done.

Target:

```text
TeklaMcpServer.Api/Drawing/ViewLayout/TeklaDrawingViewApi.Layout.cs
```

Reason:

- large active file;
- already part of a `partial` API class;
- lower risk than splitting the core arrange strategy first.

Possible split:

```text
TeklaDrawingViewApi.Layout.cs           core public layout entry points
TeklaDrawingViewApi.Layout.<Group>.cs   one file per real method group
```

Do not invent file groups before reading the file. Possible groups may be
apply/commit, candidate tracing, plan/candidate orchestration, or diagnostics,
but the final split should follow the actual method clusters.

Completed split:

```text
TeklaDrawingViewApi.Layout.cs              825 lines, orchestration/core helpers
TeklaDrawingViewApi.Layout.Diagnostics.cs  650 lines, trace and diagnostics helpers
TeklaDrawingViewApi.Layout.Scale.cs        333 lines, scale selection/probing helpers
TeklaDrawingViewApi.Layout.Details.cs      273 lines, detail view helpers
```

Current decision:

- consider Phase 1 closed;
- do not split `TeklaDrawingViewApi.Layout.cs` further in this pass;
- keep remaining layout orchestration together unless a later functional change
  reveals a clearer boundary.

### Phase 2: TeklaDrawingDimensionsApi Commands Split

Status: done.

Starting size: 1933 lines.

Target:

```text
TeklaMcpServer.Api/Drawing/Dimensions/TeklaDrawingDimensionsApi.Commands.cs
```

Completed split:

```text
TeklaDrawingDimensionsApi.Commands.cs          173 lines, simple move/create/delete commands
TeklaDrawingDimensionsApi.Commands.Debug.cs   1015 lines, debug/read/overlay commands
TeklaDrawingDimensionsApi.Commands.Combine.cs 345 lines, combine command and apply helpers
TeklaDrawingDimensionsApi.Commands.Place.cs   718 lines, control/contour placement commands
```

Completed command groups:

```text
TeklaDrawingDimensionsApi.Commands.cs
  MoveDimension
  MoveAngleDimension
  CreateDimension
  DeleteDimension

TeklaDrawingDimensionsApi.Commands.Debug.cs
  GetDimensionSourceDebug
  ReadDimensionSourceDebugInfosCore
  GetDimensionTextPlacementDebug
  ReadDimensionTextPlacementDebugInfosCore
  DrawDimensionTextBoxes
  GetAngleDimensionDebug
  DrawAngleDimensionDebugGeometry
  BuildDimensionSourceDebugFingerprint
  BuildDimensionTextPlacementDebugFingerprint
  TryCreatePresentationConnection
  EnumeratePresentationTextPrimitives
  TryGetShortDimension

TeklaDrawingDimensionsApi.Commands.Combine.cs
  CombineDimensions
  CreateCombineCandidateResult
  ResolveArrangeHandoffResult
  TryApplyCombineCandidate
  FindDimensionSetsById
  CreateCombinePointList
  TryGetCombineAttributes
  TryResolveCombineOffsetVector

TeklaDrawingDimensionsApi.Commands.Place.cs
  PlaceControlDiagonals
  PlaceContourAngleDimensions
  PlaceContourRadiusDimensions
  CreateAngleDimensionDebugInfo
  ResolveAngleBisector
  AddAngleRadiusCandidate
  CreateDebugPoint
  CreateDebugVector
  SafeDouble
  SafeToString
  RoundDebug
  FlattenZ
  ContourSegment
  GetPolycurveSegments
  HasBooleans
```

Completed pre-move checks:

- verify `TryCreatePresentationConnection`,
  `EnumeratePresentationTextPrimitives`, and `TryGetShortDimension` are still
  called only by the debug/read/overlay group before moving them to
  `Commands.Debug.cs`;
- verify `CreateDebugPoint`, `CreateDebugVector`, `SafeDouble`,
  `SafeToString`, `RoundDebug`, and `FlattenZ` are still local to the
  contour/angle debug placement tail before moving them to `Commands.Place.cs`;
- verify `ContourSegment`, `GetPolycurveSegments`, and `HasBooleans` are still
  local to contour/control placement before moving them to `Commands.Place.cs`;
- if a helper has a non-debug or cross-group caller, leave it in
  `TeklaDrawingDimensionsApi.Commands.cs` for that split and document the
  dependency.

Completed order:

1. Move the debug/read/overlay group first. It is large, cohesive, and lower
   risk than mutating dimension commands.
2. Move `CombineDimensions` and its private helpers.
3. Move contour/control placement commands and their tail helpers.
4. Leave the simple move/create/delete commands in
   `TeklaDrawingDimensionsApi.Commands.cs`.

Boundary decision:

```text
TeklaDrawingDimensionsApi.Commands.cs
TeklaDrawingDimensionsApi.cs
```

`TeklaDrawingDimensionsApi.cs` is a shared helper surface used by command,
query, arrangement, text-placement, and geometry paths. Do not split it during
the first Phase 2 pass. If it becomes a later hotspot, split it separately by
actual shared responsibility, for example geometry, text placement, and view
resolution.

Keep method bodies unchanged. Only move related command methods and their
private helpers together.

### Phase 3: Bridge Dimension Handler Split

Status: planned.

Starting size from initial hotspot audit: 1383 lines.

Target:

```text
TeklaBridge/Commands/DrawingCommandHandler.Dimensions.cs
```

Planned split:

```text
DrawingCommandHandler.Dimensions.cs
  TryHandleDimensionCommands
  HandleGetDrawingDimensions
  HandleGetDimensionContexts
  WriteGetDimensionsResult

DrawingCommandHandler.Dimensions.Debug.cs
  HandleDrawDimensionTextBoxes
  HandleDrawAngleDimensionDebugGeometry
  HandleGetAngleDimensionDebug
  HandleGetDimensionTextPlacementDebug
  HandleGetDimensionSourceDebug
  HandleGetDimensionGroupsDebug
  HandleGetDimensionOrchestrationDebug
  HandleGetDimensionActionPlan
  HandleGetDimensionArrangementDebug
  SerializeRepresentativePackets
  SerializeCombineCandidates
  SerializeCombinePreview
  SerializePoint
  SerializeActionPlanSteps
  SerializeActionPlanToolArguments
  SerializeOrchestrationPackets
  WriteDimensionArrangementDebugResult
  SerializeDirection
  SerializeMembers
  SerializeGroup
  SerializeReductionItems
  GetContextPropertyValue
  GetLayoutPolicyPropertyValue
  SerializeDebugLine
  SerializeDebugBounds
  SerializeDebugVector
  SerializeGeometryBand

DrawingCommandHandler.Dimensions.Apply.cs
  HandleArrangeDimensions
  HandleCombineDimensions
  HandleMoveDimension
  HandleMoveAngleDimension
  HandleCreateDimension
  HandleDeleteDimension
  HandlePlaceControlDiagonals
  HandlePlaceContourRadiusDimensions
  HandlePlaceContourAngleDimensions
  WriteMoveDimensionResult
  WriteCreateDimensionResult
  WriteDeleteDimensionResult
  WriteArrangeDimensionsResult
  WriteCombineDimensionsResult
```

Avoid changing the bridge command protocol in this phase.

Pre-move checks:

- keep `TryHandleDimensionCommands` in the main file so bridge routing remains
  easy to review;
- verify every moved `Handle*` method is still called only by
  `TryHandleDimensionCommands`;
- verify every moved `Serialize*`, `Get*PropertyValue`, and `Write*` helper is
  called only by the same group before moving it;
- verify `SerializeGeometryBand` is still called only by debug serializers
  before moving it to `DrawingCommandHandler.Dimensions.Debug.cs`;
- if a serializer is shared across debug and apply paths, leave it in the main
  file for that split and document the dependency.

Recommended order:

1. Move the debug/read/plan handlers and their serializers first.
2. Move apply/mutation handlers and their result writers.
3. Leave routing and basic read handlers in
   `DrawingCommandHandler.Dimensions.cs`.

Use the Phase 2 API groups as a guide. The bridge file should remain
protocol-only: routing, argument parsing, and result serialization.

### Phase 4: Core Layout Strategy Split

Target:

```text
TeklaMcpServer.Api/Drawing/ViewLayout/BaseProjectedDrawingArrangeStrategy.cs
```

This is the highest-risk file because it contains core layout behavior.
Handle only after phases 1-3 are complete.

Possible split:

```text
BaseProjectedDrawingArrangeStrategy.cs             constructor/public entry
BaseProjectedDrawingArrangeStrategy.<Group>.cs      one file per real method group
```

Before this phase, define a manual before/after check on 2-3 representative
drawings and capture the expected layout behavior. Add focused tests only if a
small, realistic test seam already exists.

## Deferred Hotspots

These files are large but not first in the refactor order:

```text
TeklaMcpServer.Api/Algorithms/Marks/ForceDirectedMarkPlacer.cs
TeklaMcpServer.Api/Filtering/Common/FilterHelper.cs
TeklaMcpServer.Api/Drawing/ViewLayout/ProjectedGroupLayoutPlanner.cs
TeklaMcpServer.Api/Drawing/Dimensions/TeklaDrawingDimensionsApi.cs
```

Reason:

- they are either algorithm-heavy or shared helper surfaces;
- a mechanical split is still possible, but the first pass should target files
  that already use clear `partial` patterns and are frequently touched by
  drawing automation work;
- include one of these earlier only if it becomes an active change hotspot.

## Verification

After every phase:

```text
git diff --check
dotnet build src/TeklaMcpServer.Host/TeklaMcpServer.Host.csproj -c Release
```

Existing warnings are acceptable if no new errors are introduced.

For high-risk layout moves, run the agreed manual drawing check before and
after the split.

## Done Criteria

- Each split is committed separately.
- Build passes with 0 errors after every commit.
- Public command/tool names stay unchanged.
- No behavior changes are intentionally introduced.
- Large files are smaller and grouped by responsibility.

## Non-Goals

- No algorithm rewrite.
- No new frameworks.
- No dependency changes.
- No public API redesign.
- No broad formatting-only churn.
