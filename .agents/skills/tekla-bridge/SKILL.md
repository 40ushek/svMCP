---
name: tekla-bridge
description: Use when a Tekla MCP tool fails or answers with stale data, after changing TeklaMcpServer.Api or TeklaBridge code, or when a bridge command has no MCP tool. Connection check, active-drawing pitfalls, deploy of the bridge, direct bridge commands. Not needed for placing dimensions when the tools work.
---

# Tekla MCP and bridge

The MCP server (`TeklaMcpServer.exe`) calls a persistent `TeklaBridge.exe --loop`, which talks to
Tekla. Build, deploy and the two-process design are described in `CLAUDE.md`; this file is the
short operating checklist.

## Connection

- Call `check_connection`: it returns the model name and path. No answer means Tekla is not
  running or no model is open.
- Almost every drawing command works on the **active drawing**. `get_drawing_context` shows which
  one and what is selected. "View N not found in active drawing" means another drawing is open:
  `open_drawing`, then retry.
- A view's snapshot (`contextId`) is dropped when you ask about another view or switch drawing.
  Create all dimensions of a view before reading the next one; an "Unknown or expired contextId"
  error means read the view again.

## Tools missing or stale

- The client caches tool schemas until it is restarted. A new tool parameter appears only after
  restarting the MCP server.
- Stop `TeklaMcpServer` before building it (`dotnet build` fails with MSB3021 on a running server).
  A test build that must not disturb the session:
  `dotnet test src/TeklaMcpServer.Tests/TeklaMcpServer.Tests.csproj -c Release -p:BaseOutputPath=D:/repos/svMCP/.codex-build/<name>/`
  (forward slashes).

## Bridge stuck or answering old data

1. Stop the `TeklaBridge` process; the server starts a new one by itself.
2. After changing `TeklaMcpServer.Api` or `TeklaBridge`, build the bridge
   (`dotnet build src/TeklaBridge/TeklaBridge.csproj -c Release`; a Host build does **not** refresh
   the bridge's copy of the Api DLL) and copy **all three** of `TeklaBridge.exe`,
   `TeklaMcpServer.Api.dll` and `SolidContacts.Core.dll` from `src/TeklaBridge/bin/Release/net48/`
   to `C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`. Missing one is not an
   error at deploy time: the bridge answers from the old DLL or fails on the first contact call.
3. Compare hashes of the built and the deployed `TeklaMcpServer.Api.dll` before trusting a live check.

## Direct bridge commands

Some commands have no MCP tool. Run the extension-folder bridge, for example the skill progress log:

```
TeklaBridge.exe log_skill_event start dimension-drawings <runId>
TeklaBridge.exe log_skill_event task dimension-drawings <runId> "<next phase>"
TeklaBridge.exe log_skill_event finish dimension-drawings <runId> "<summary>"
```

## Errors

The last error is in `C:\temp\teklabridge_log.txt`; the IPC channel fix results are in
`C:\temp\tekla_channel.txt`.
