# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**svMCP** is a Windows-only MCP (Model Context Protocol) server that bridges Claude and other MCP clients to **Tekla Structures 2021** (a structural BIM design application). It exposes Tekla model and drawing operations as MCP tools over stdio transport.

## Build & Run

```bash
# Build everything (TeklaMcpServer + TeklaBridge)
dotnet build src/TeklaMcpServer/TeklaMcpServer.csproj -c Release

# Bridge-only rebuild (no need to close Claude Desktop)
dotnet build src/TeklaMcpServer/TeklaBridge/TeklaBridge.csproj -c Release
```

- Requires .NET 8 SDK, .NET Framework 4.8, and Tekla Structures 2021 (Windows only)
- The server communicates via **stdio** — launched by an MCP client (e.g., Claude Desktop), not run interactively
- **TeklaMcpServer.exe is locked by Claude Desktop** while it's open — always close Claude Desktop before rebuilding TeklaMcpServer. TeklaBridge.exe can be rebuilt without closing Claude Desktop.

## Architecture

### Two-process design

```
Claude Desktop
    │  stdio (JSON-RPC / MCP)
    ▼
TeklaMcpServer.exe  (net8.0-windows)
    │  Process.Start → stdout pipe
    ▼
TeklaBridge.exe  (net48)
    │  .NET Remoting IPC
    ▼
Tekla Structures 2021
```

**Why two processes?** MCP SDK requires .NET 8+; Tekla Structures 2021 Open API requires .NET Framework 4.8. Two different CLRs cannot run in the same process.

TeklaBridge accepts a command as the first CLI argument, calls Tekla API, and returns JSON to stdout.

### Projects

- **TeklaMcpServer/** — MCP server (net8.0-windows). Entry point: [Program.cs](TeklaMcpServer/Program.cs)
- **TeklaBridge/** — Bridge subprocess (net48). Entry point: [TeklaBridge/Program.cs](TeklaBridge/Program.cs)
- **TeklaMcpServer.Api/** — All Tekla API logic (net48): interfaces, DTOs, and implementations. The single place for all Tekla interaction code.
- **svMCP/** — Unused placeholder library (netstandard2.0)

### Responsibility split

| Layer | Project | Role |
|---|---|---|
| MCP Tools | `TeklaMcpServer/Tools/` | **Thin wrappers only** — call `RunBridge()`, parse JSON, return string |
| Bridge dispatcher | `TeklaBridge/Commands/` | Route command to the right `TeklaMcpServer.Api` class, serialize result |
| All Tekla logic | `TeklaMcpServer.Api/` | Interfaces, DTOs, and all Tekla API implementations |

New Tekla logic goes entirely into `TeklaMcpServer.Api/`. TeklaBridge stays as a thin command dispatcher. MCP tools stay as thin wrappers.

### MCP Tool Registration

`Program.cs` calls `.WithToolsFromAssembly()`, which auto-discovers all classes annotated with `[McpServerToolType]` and methods annotated with `[McpServerTool]`.

**To add a new tool:**
1. Define interface + DTOs + implementation in `TeklaMcpServer.Api/`
2. Wire command in `TeklaBridge/Commands/ModelCommandHandlers.cs` or `DrawingCommandHandlers.cs`
3. Add thin `[McpServerTool]` wrapper in `TeklaMcpServer/Tools/` calling `RunBridge("command_name", ...args)`

### File Structure

```
src/
├── TeklaMcpServer.Api/       # ALL Tekla API code (net48) — interfaces, DTOs, implementations
│   ├── Connection/           # ITeklaConnectionApi, ConnectionInfo
│   ├── Selection/            # IModelSelectionApi, ModelObjectInfo, TeklaModelSelectionApi
│   │                         # ISelectionCacheManager, SelectionCacheManager
│   │                         # SelectionResult, ToolInputSelectionHandler
│   ├── Drawing/              # IDrawingQueryApi, DrawingInfo
│   └── Filtering/
│       ├── Common/           # FilterExpressionParser, FilterTokenizer, FilterAstBuilder, FilterHelper
│       │                     # FilterNode, GroupNode, SimpleExpressionNode, Token, TokenType, exceptions
│       ├── Drawing/          # DrawingObjectsFilterHelper
│       └── Model/            # IModelFilteringApi, ModelObjectFilter, FilteredModelObjectsResult
│                             # TeklaModelFilteringApi
├── TeklaMcpServer/           # MCP server (net8.0-windows)
│   ├── Program.cs            # Entry point — MCP host config
│   └── Tools/                # Thin MCP tool wrappers
│       ├── Shared/           # RunBridge() helper
│       ├── Connection/       # check_connection
│       ├── Model/            # Model tools
│       └── Drawing/          # Drawing tools
├── TeklaBridge/              # Bridge process (net48) — command dispatcher only
│   ├── Program.cs            # Entry point + IPC fix + Console capture
│   └── Commands/
│       ├── ModelCommandHandlers.cs
│       └── DrawingCommandHandlers.cs
└── svMCP/                    # Unused stub
```

## Available Tools

### Connection

| Tool | Description |
|------|-------------|
| `check_connection` | Verify connection to Tekla Structures; return model name/path |

### Model

| Tool | Description |
|------|-------------|
| `get_selected_elements_properties` | Properties of selected elements: Part, BoltGroup, Weld, RebarGroup — all types |
| `get_selected_elements_total_weight` | Total weight of selected elements (kg) |
| `select_elements_by_class` | Select model elements by Tekla class number |
| `filter_model_objects_by_type` | Filter / select model objects by type (beam, plate, bolt, assembly…) |

### Drawing

| Tool | Description |
|------|-------------|
| `list_drawings` | List all drawings in the model |
| `find_drawings` | Search by name / mark (case-insensitive contains) |
| `find_drawings_by_properties` | Search by multiple JSON filters (name, mark, type, status) |
| `export_drawings_to_pdf` | Export drawings to PDF by GUID |
| `create_general_arrangement_drawing` | Create GA drawing from a saved model view via macro |
| `get_drawing_context` | Active drawing and currently selected objects |
| `select_drawing_objects` | Select drawing objects by model object IDs |
| `filter_drawing_objects` | Filter drawing objects by type (Mark, Part, DimensionBase…) |
| `set_mark_content` | Modify mark content and font settings |

## Critical Tekla API Patterns

### IPC Channel Fix (must run before any Tekla API call)

When TeklaBridge stdout is a pipe (redirected by MCP server), Tekla computes IPC channel names without the `-Console` suffix, causing `RemotingException: Failed to connect to an IPC Port`. The fix scans all static string fields across all Tekla assemblies and corrects the channel names:

```csharp
// Touch public assemblies so they load
_ = typeof(Tekla.Structures.Model.Model);
_ = typeof(Tekla.Structures.Drawing.DrawingHandler);

// Force-load Internal assemblies — NOT auto-loaded
var dir = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location) ?? "";
foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll"))
    try { Assembly.LoadFrom(dll); } catch { }

// Fix all broken channel names (pattern: "Tekla.Structures.*-:*")
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
    if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
    Type[] types; try { types = asm.GetTypes(); } catch { continue; }
    foreach (var t in types)
        foreach (var f in t.GetFields(flags)) {
            if (f.FieldType != typeof(string)) continue;
            try {
                var val = f.GetValue(null)?.ToString() ?? "";
                if (val.StartsWith("Tekla.Structures.") && val.Contains("-:"))
                    f.SetValue(null, val.Replace("-:", "-Console:"));
            } catch { }
        }
}
```

This must execute **before** `new Model()` or `new DrawingHandler()`. Affects three channels:
- `Tekla.Structures.Model-Console:2021.0.0.0`
- `Tekla.Structures.Drawing-Console:2021.0.0.0`
- `Tekla.Structures.TeklaStructures-Console:2021.0.0.0`

### Console.Out Capture

Tekla writes internal diagnostics to `Console.Out` during API calls — this would corrupt the JSON output. Capture before any Tekla API call:

```csharp
var realOut = Console.Out;
var teklaLog = new StringWriter();
Console.SetOut(teklaLog); // Tekla writes here

// All JSON output goes via realOut
realOut.WriteLine(JsonSerializer.Serialize(result));
```

### Common API Patterns

```csharp
// Connection
var model = new Model();
bool connected = model.GetConnectionStatus();

// Model selection
var enumerator = new ModelObjectSelector().GetSelectedObjects(); // ModelObjectEnumerator

// Report properties
double weight = 0;
modelObject.GetReportProperty("WEIGHT", ref weight);

// Drawing list
var drawings = new DrawingHandler().GetDrawings(); // DrawingEnumerator
```

## Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 0.9.0-preview.2 | MCP server SDK |
| `Microsoft.Extensions.Hosting` | 8.0.0 | DI / hosting |
| `Tekla.Structures` | 2021.0.0 | Core Tekla API |
| `Tekla.Structures.Model` | 2021.0.0 | Model manipulation |
| `Tekla.Structures.Drawing` | 2021.0.0 | Drawing API |

## Diagnostics

| File | Content |
|------|---------|
| `C:\temp\teklabridge_log.txt` | Last error details (JSON) |
| `C:\temp\tekla_channel.txt` | IPC channel fix results (count + details) |
