# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**svMCP** is a Windows-only MCP (Model Context Protocol) server that bridges Claude and other MCP clients to **Tekla Structures 2021** (a structural BIM design application). It exposes Tekla model operations as MCP tools over stdio transport.

## Build & Run

```bash
# Build the solution
dotnet build src/svMCP.sln

# Build only the server project
dotnet build src/TeklaMcpServer/TeklaMcpServer.csproj

# Run the MCP server
dotnet run --project src/TeklaMcpServer/TeklaMcpServer.csproj
```

- Requires .NET 8 SDK and Tekla Structures 2021 installed locally (Windows only)
- The server communicates via **stdio** — it is meant to be launched by an MCP client (e.g., Claude Desktop), not run interactively
- No test projects currently exist

## Architecture

### Projects

- **TeklaMcpServer/** — The active console application (net8.0-windows). Entry point in [Program.cs](TeklaMcpServer/Program.cs).
- **svMCP/** — Unused class library placeholder (netstandard2.0). Currently contains only empty stub code.

### MCP Tool Registration

`Program.cs` configures a `Host` with the MCP server using stdio transport and calls `.WithToolsFromAssembly()`, which auto-discovers all classes annotated with `[McpServerToolType]` and methods annotated with `[McpServerTool]`.

To add a new tool: annotate a static method in any class with `[McpServerTool]` inside a `[McpServerToolType]` class. No registration boilerplate needed.

### Core Implementation: ModelTools.cs

[ModelTools.cs](TeklaMcpServer/ModelTools.cs) is the single file where all Tekla tools are implemented. Current tools:

| Tool | Description |
|------|-------------|
| `CheckConnection` | Verifies connection to a running Tekla Structures instance; returns model name/path |
| `GetSelectedElementsProperties` | Returns JSON array of properties (GUID, name, profile, material, class, finish, weight) for currently selected elements |
| `SelectElementsByClass` | Selects model elements by Tekla class number; returns count |
| `GetSelectedElementsTotalWeight` | Sums the weight (kg) of selected parts |

### Tekla API Patterns

- Use `new Model()` and `.GetConnectionStatus()` to check connectivity
- `UI.ModelObjectSelector` is used to get/set the current selection
- Report properties (e.g., `WEIGHT`) are retrieved via `modelObject.GetReportProperty(name, ref value)`
- All tools return `string` — serialize structured data as JSON

### Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 0.9.0-preview.2 | MCP server SDK |
| `Microsoft.Extensions.Hosting` | 8.0.0 | DI / hosting |
| `Tekla.Structures` | 2021.0.0 | Core Tekla API |
| `Tekla.Structures.Model` | 2021.0.0 | Model manipulation |
| `Tekla.Structures.Drawing` | 2021.0.0 | Drawing API |
