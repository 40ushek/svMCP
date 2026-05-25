# Forum Sample Handoff

Workspace:

`D:\repos\svMCP\src`

Repository rules:

- Follow `AGENTS.md`.
- Do not revert unrelated/user changes.
- Keep changes minimal and localized.
- `TeklaMcpServer.Host` is a test/sandbox project.

Current goal:

Prepare standalone Tekla Open API sample code for a forum post. The sample should show how to place dimensions for `ContourPlate` without depending on the main MCP/API architecture.

Relevant Host files:

- `TeklaMcpServer.Host/ContourPlateAngleDimensionPlacer.cs`
- `TeklaMcpServer.Host/ContourPlateRadiusDimensionPlacer.cs`
- `TeklaMcpServer.Host/Program.cs`

Current state:

- `ContourPlateAngleDimensionPlacer` places `AngleDimension` objects at `ContourPlate` vertices.
- `ContourPlateRadiusDimensionPlacer` places `RadiusDimension` objects for contour plate arcs/roundings.
- `Program.cs` calls both placers sequentially for testing.
- `AngleDimensionMoveProbe` is not part of this forum sample.

Build check:

```powershell
dotnet build TeklaMcpServer.Host\TeklaMcpServer.Host.csproj -v minimal /p:TeklaExtensionsDir=C:\__no_deploy__\
```

Last known result:

- Build succeeded.
- Only existing Trimble/binding redirect warnings remained.

Runtime test:

- Requires Tekla Structures with an open model.
- Requires an active drawing with a `ContourPlate` visible in a drawing view.
- `viewId: null` means first drawing view.
- `attributesFile: "standard"` is used by default.

Tekla notes:

- `View.GetIdentifier()` requires `Tekla.Structures.DrawingInternal`.
- `AngleDimension` expects view-plane coordinates.
- The code switches the model work plane to the drawing view coordinate system before reading contour points.
- `GetContourPolycurve()` returns world-space coordinates and requires `Select()` before calling.

Known unrelated context:

- Moving `AngleAtVertex` angular dimensions by changing `Distance` was investigated separately and is not part of this sample.
- That limitation/bug is documented in the dimensions roadmap.
