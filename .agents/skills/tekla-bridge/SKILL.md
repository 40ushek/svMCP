---
name: tekla-bridge
description: Use when a Tekla MCP tool fails or answers with stale data, after changing TeklaMcpServer.Api or TeklaBridge code, or when a bridge command has no MCP tool. Connection check, active-drawing pitfalls, deploy of the bridge, direct bridge commands. Not needed for placing dimensions when the tools work.
---

# Tekla MCP and bridge

Since `c085d61` (`TeklaBridge.Controller`, see `src/TeklaBridge.Controller/` and
`src/TeklaBridge/ROADMAP_TRAY_CONTROL.md`), the MCP server (`TeklaMcpServer.exe`) does **not** own
`TeklaBridge.exe` directly. A separate tray app, `TeklaBridge.Controller.exe`
(`TeklaMcpServer/bin/<Config>/net8.0-windows/controller/`), is the sole owner of the persistent
`TeklaBridge.exe --loop` process; the MCP server talks to it over a local named pipe
(`svMcpTeklaBridgeController`). The MCP server auto-starts the controller on first use if the pipe
is unreachable. Build, deploy and the two-process design are described in `CLAUDE.md`; this file is
the short operating checklist.

**Paused is sticky.** Once the bridge is stopped (tray "Stop Bridge", or a `stop` pipe request),
the controller persists `Paused` to `%LOCALAPPDATA%\svMCP\bridge-state.json` and **refuses every
`execute` until an explicit `resume`/`restart`** — unlike the old direct-owned bridge, a paused
controller does **not** silently start a fresh one on the next tool call. If a tool call fails with
"TeklaBridge is stopped. Resume it from the system tray.", send `resume` (see below) or use the
tray menu; do not assume it will recover on its own.

**Never `Stop-Process`/`taskkill` `TeklaBridge.exe` directly.** That leaves the controller's own
bookkeeping (`_paused`, `IsRunning`) out of sync with reality, and can produce confusing
`Win32Exception`/"Access to the path ... is denied" errors on the next auto-start attempt (seen
live in this project) if the kill races with a file copy or another launch. Always go through the
controller's `stop`/`resume`/`restart` pipe operations instead.

### Scripting the controller (status / stop / resume / restart)

No CLI flag exists; script the named pipe directly. Use the Bash tool, calling `powershell.exe`
(do not use the PowerShell tool for this — quoting a JSON payload through it is unreliable in this
project; a plain `powershell.exe -Command` from Bash is not):

```bash
powershell.exe -NoProfile -Command '
function Send-BridgeControllerRequest([string]$Operation) {
    $req = ([ordered]@{ Id = 1; Operation = $Operation; Command = $null; Args = @(); TimeoutMilliseconds = 5000 } | ConvertTo-Json -Compress)
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "svMcpTeklaBridgeController", [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(3000)
    $writer = New-Object System.IO.StreamWriter($pipe); $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($pipe)
    $writer.WriteLine($req)
    $line = $reader.ReadLine()
    $pipe.Dispose()
    return $line
}
Send-BridgeControllerRequest -Operation "status"
'
```

Replace `"status"` with `"stop"`, `"resume"` or `"restart"`. The response is one JSON line, e.g.
`{"ok":true,"status":{"state":"Running","bridgeRunning":true,"paused":false,"teklaConnection":"Connected"}}`.
`Connect(3000)` timing out means the controller itself is not running (no MCP call has auto-started
it yet, or it was fully exited via the tray "Exit" item — see below).

Redeploying the **controller assembly itself** (rare — only its own project changed) needs a full
exit first: there is no pipe operation for that, only `Stop-Process -Name "TeklaBridge.Controller"`
or the tray "Exit" menu item (which itself stops the bridge cleanly first). The MCP server
auto-starts a fresh copy on the next call. This is the one case where killing a process directly is
correct — it is the controller's own process, not the bridge it owns.

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

1. Send `stop` to the controller over the pipe (not `Stop-Process` on `TeklaBridge.exe`, see
   above) and confirm with `status` that `bridgeRunning` is `false` before touching any file in the
   extensions folder — the child process must have exited or the copy in the next step is locked.
2. After changing `TeklaMcpServer.Api` or `TeklaBridge`, build the bridge
   (`dotnet build src/TeklaBridge/TeklaBridge.csproj -c Release`; a Host build does **not** refresh
   the bridge's copy of the Api DLL) and copy **all three** of `TeklaBridge.exe`,
   `TeklaMcpServer.Api.dll` and `SolidContacts.Core.dll` from `src/TeklaBridge/bin/Release/net48/`
   to `C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`. Missing one is not an
   error at deploy time: the bridge answers from the old DLL or fails on the first contact call.
3. Compare hashes of the built and the deployed `TeklaMcpServer.Api.dll` before trusting a live check.
4. Send `resume` to the controller and confirm `status` shows `bridgeRunning: true`.
   After an explicit `stop`, an MCP call cannot resume the paused bridge.

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
