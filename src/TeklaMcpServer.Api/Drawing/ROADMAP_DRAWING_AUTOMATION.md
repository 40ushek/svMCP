# Roadmap Drawing Automation

## Goal

Add a dedicated drawing automation path for repeated workflows over drawings.

The first target is the slow manual sequence:

- open drawing by GUID
- arrange views
- arrange marks
- save/close drawing
- repeat for many drawings

The automation path should make these workflows fast and stable by combining:

- command-specific bridge timeouts;
- background drawing open for automation;
- warm bridge-local drawing cache;
- optional future queue commands, if still needed later.

Important current baseline: `PersistentBridge` already exists. `TeklaBridge.exe`
is not intentionally one-shot for MCP tools. The main cold-start trigger is a
timeout or fatal IPC error that causes `PersistentBridge` to kill the process.

## Problem

Mass drawing workflows are currently expensive and fragile when driven as many
separate MCP calls, especially when one long-running command exceeds the bridge
response timeout.

Example:

- 19 drawings
- 2 to 4 operations per drawing
- each operation crosses MCP -> server -> bridge -> Tekla

The expensive parts are:

- repeated transport and JSON roundtrips;
- repeated drawing open/close lifecycle;
- repeated risk of bridge timeout killing the bridge process;
- repeated `GetDrawings()` scans when the bridge process is restarted;
- visible `SetActiveDrawing(..., showDrawing: true)` redraw cost.

The in-memory drawing GUID cache is useful only while the bridge process stays
alive. If a long operation times out and `PersistentBridge` kills the process,
the cache is lost.

The simplest first fix is not a batch command. It is command-specific timeout hardening
for known heavy commands such as:

- `arrange_marks_force`
- `fit_views_to_sheet`
- future automation commands

Do not design a batch command yet. First make the current bridge and drawing-open
paths stable and fast.

Another major speed factor is background drawing mode. Plantech's batch plugin
uses `SetActiveDrawing(drawing, showDrawing: false)` by default through its
`Run in background` option. That avoids Drawing Editor redraw/flicker and makes
automation significantly faster.

Current MCP surface uses separate tools instead of an optional `showDrawing`
parameter:

```text
open_drawing            -> visible/manual open
open_drawing_background -> background automation open
```

This avoids Claude Code / MCP client issues with optional boolean tool
parameters while keeping the bridge/API contract unchanged internally.

## Non-Goals

- Do not create a disk cache for live Tekla `Drawing` objects.
- Do not let users run arbitrary bridge commands from automation input.
- Do not duplicate layout or mark algorithms inside the automation layer.
- Do not replace existing single-drawing MCP tools.

## Architecture

Keep the existing command layers:

```text
MCP tool
  -> TeklaBridge command
     -> TeklaMcpServer.Api services
        -> existing drawing/view/mark APIs
```

Automation is an orchestration and transport policy layer only.

It should call the same APIs used by existing commands:

- `TeklaDrawingQueryApi.OpenDrawing(...)`
- view arrangement API
- mark layout API
- `TeklaDrawingQueryApi.CloseActiveDrawing()`

The existing single-drawing commands remain available for manual work and
debugging.

## First Step: Timeout Hardening

Before adding any new queue tool, make the bridge timeout policy command-aware.

Current behavior:

- `PersistentBridge.Send(...)` waits one fixed `_responseTimeout`.
- on timeout it throws;
- the catch path kills the bridge process;
- the next command starts cold and loses process-local caches.

Required behavior:

- keep the default timeout for normal interactive commands;
- use a larger timeout for known heavy commands;
- make timeout selection visible in `PerfTrace`;
- keep killing/restarting the bridge on real transport corruption, but avoid
  treating expected long operations as transport failure.

Initial timeout candidates:

```text
default:              30 seconds
arrange_marks_force:  180-300 seconds
fit_views_to_sheet:   120-180 seconds
future queue command: 300-900 seconds, only if added later
```

Implementation options:

- add a `ResolveTimeout(command)` method in `ModelTools` or `PersistentBridge`;
- or pass timeout override to `Bridge.Send(command, args, timeout)`;
- or introduce command metadata for timeout and other transport behavior.

This is the lowest-risk first slice because it changes transport policy only.

## Background Drawing Mode

Automation paths should default to background drawing open:

```text
SetActiveDrawing(drawing, showDrawing: false)
```

This mirrors the working Plantech pattern:

- UI/manual mode can still choose visible opening;
- automation should avoid visible Drawing Editor redraw;
- this reduces open latency and avoids flicker while processing many drawings.

Keep the existing interactive `open_drawing` behavior visible. The safer MCP
surface is:

```text
manual/interactive open: open_drawing
automation open:         open_drawing_background
```

For existing multi-step MCP workflows, callers should use
`open_drawing_background` before automated layout/mark operations. The MCP
tool does not expose `showDrawing` as an optional parameter.

## Timeout Policy

Automation commands need a timeout policy that matches expected runtime.

Current `PersistentBridge` uses one fixed timeout. That is risky because a single
large drawing can exceed 30 seconds and kill the bridge process, losing warm
state and the drawing cache.

Planned options:

- add per-command timeout metadata in MCP server;
- or make known long-running commands use a larger timeout;
- or add progress/heartbeat protocol later.

First practical step: increase timeout for known heavy automation commands
without adding a new command.

## Diagnostics

Use existing `PerfTrace` layers:

- `mcp`
- `transport`
- `bridge-loop`
- `bridge-exec`
- `api-view`
- `api-mark`

Automation should add or extend summary events:

- selected command timeout
- bridge restart reason
- drawing open mode (`showDrawing=true/false`)
- drawing cache hit/miss/rebuild

Useful fields:

- GUID/mark/name when available
- operation status
- open/operation/close elapsed times
- bridge process restart indicator from transport layer

## Cache Rules

Allowed:

- bridge-process-local GUID -> `Drawing` cache;
- invalidating the cache after create/delete/update operations;
- rebuilding the cache once per bridge process or after invalidation.

Not allowed:

- disk cache of live `Drawing` objects;
- trusting persisted GUID metadata as an openable runtime handle;
- hiding stale-cache failures.

If a GUID is missing from cache, automated open may invalidate and rebuild once
before reporting the drawing as missing.

## Phases

### Phase 1: Bridge Timeout Policy

- Done: add per-command timeout support in `PersistentBridge`.
- Done: keep default timeout for small commands.
- Done: give known heavy drawing commands longer timeouts.
- Done: log selected timeout in `PerfTrace`.
- Do not change drawing algorithms.

### Phase 2: Background Drawing Open

- Done: preserve visible/manual `open_drawing`.
- Done: add separate `open_drawing_background` tool for automation.
- Do not expose optional `showDrawing` on the MCP tool surface.
- Measure open time with visible and background modes.

### Phase 3: Cache and Lifecycle Hardening

- Rebuild cache once on missing GUID during open.
- Ensure drawing cache invalidation happens after create/delete/update.
- Confirm active drawing is closed in success and failure paths.

### Phase 4: Queue Command, If Still Needed

- Consider a future queue/batch command only after phases 1-3 are measured.
- Keep any future operation list whitelisted.
- Do not expose arbitrary bridge commands.

### Phase 5: Progress Protocol

- Consider bridge heartbeat/progress events for long automation runs.
- Keep final JSON result as the source of truth.

## Open Questions

- Which commands need extended timeout in the first implementation?
- What timeout values are acceptable for `arrange_marks_force` and
  `fit_views_to_sheet`?
- Should other bool-heavy MCP tools also be split into explicit mode tools if
  Claude Code shows the same optional boolean issue?
- Where should drawing cache hit/miss/rebuild be traced?
- After phases 1-3, is a queue command still needed?

## Recommended First Slice

Do not implement batch first.

Implement timeout hardening first:

```text
normal commands        -> existing short timeout
heavy drawing commands -> longer timeout
future queue commands  -> longest timeout
```

This keeps the existing command model and prevents long-but-valid drawing
operations from killing the persistent bridge.

In parallel, make automated drawing opens use `open_drawing_background`
wherever the caller does not need the Drawing Editor UI.

Do not add a generic command runner yet.

## Practical Next Steps

### Step 1: Confirm the Current Failure Mode

Use `PerfTrace` to confirm whether the bridge is being killed by timeout:

- enable `SVMCP_PERF`;
- run the slow drawing sequence;
- check `transport` events for timeout/error;
- check whether `restarted=true` appears after heavy commands;
- compare process ids in MCP/bridge trace if available.

Expected finding:

- first `open_drawing` after bridge start is slow because it builds the drawing
  cache;
- subsequent `open_drawing` calls are faster while the bridge survives;
- after `arrange_marks_force` timeout, bridge restarts and cache is cold again.

### Step 2: Define Command Timeout Policy

Document the command timeout table before coding it.

Initial policy:

```text
default interactive commands: 30 seconds
fit_views_to_sheet:          180 seconds
arrange_views_only:          180 seconds
arrange_marks_force:         300 seconds
future queue commands:       900 seconds, only if added later
```

The exact numbers can be tuned after trace data. The important rule is that
known heavy commands must not use the same 30 second transport timeout as small
query commands.

### Step 3: Add Transport-Level Design Notes

The intended code change later is:

- keep `PersistentBridge` persistent;
- keep one bridge process per MCP server lifetime;
- make response timeout command-aware;
- log the selected timeout in `PerfTrace`;
- keep restarting on fatal IPC/connection errors.

No drawing algorithm should change in this step.

### Step 4: Re-Test Without New Commands

After timeout hardening:

- open several drawings by GUID;
- run heavy mark/layout commands;
- verify the bridge does not restart just because a command takes longer than
  30 seconds;
- verify the drawing GUID cache stays warm across calls.

If this is enough for the 19 drawing workflow, stop here.

### Step 5: Decide Whether a Queue Command Is Still Needed

Add a queue/batch command only if one of these remains true:

- the user still spends too much time orchestrating many calls;
- MCP roundtrip overhead is visible in traces;
- a single structured summary for all drawings is required;
- the workflow needs one cancellation/progress boundary for the whole queue.

Queue/batch should be treated as workflow ergonomics and bulk orchestration, not
as the primary fix for the existing bridge-cache problem.
