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
- **TeklaMcpServer/TeklaBridge/** — Bridge subprocess (net48). Entry point: [TeklaBridge/Program.cs](TeklaMcpServer/TeklaBridge/Program.cs)
- **svMCP/** — Unused placeholder library (netstandard2.0)

### MCP Tool Registration

`Program.cs` calls `.WithToolsFromAssembly()`, which auto-discovers all classes annotated with `[McpServerToolType]` and methods annotated with `[McpServerTool]`.

**To add a new tool:**
1. Add a `[McpServerTool]` static method in `TeklaMcpServer/Tools/` — call `RunBridge("command_name", ...args)`
2. Handle `"command_name"` in `TeklaBridge/Commands/ModelCommandHandlers.cs` or `DrawingCommandHandlers.cs`

### File Structure

```
src/
├── TeklaMcpServer/           # MCP server (net8.0-windows)
│   ├── Program.cs            # Entry point — MCP host config
│   ├── Tools/
│   │   ├── Shared/           # RunBridge() helper (ModelTools.Shared.cs)
│   │   ├── Connection/       # check_connection
│   │   ├── Model/            # Model selection tools
│   │   └── Drawing/          # Drawing tools (Basic + Advanced)
│   └── TeklaBridge/          # Bridge process (net48)
│       ├── Program.cs        # Entry point + IPC fix + Console capture
│       └── Commands/
│           ├── ModelCommandHandlers.cs
│           └── DrawingCommandHandlers.cs
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
| `get_selected_elements_properties` | Properties of selected elements: GUID, name, profile, material, class, weight |
| `get_selected_elements_total_weight` | Total weight of selected elements (kg) |
| `select_elements_by_class` | Select model elements by Tekla class number |

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
