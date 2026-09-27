# Logging Roadmap

**Status:** planned; Serilog has not been added yet
**Updated:** 2026-09-27

## Goal

Replace the hand-written production file writer with Serilog, use a per-user log
directory, retain logs by age and size, and keep the existing performance trace
events useful for investigating MCP and Tekla operation timings.

## Current state

- `Shared/Logging/BoundedFileLogWriter.cs` writes `C:\temp\svmcp-perf.log` and
  `C:\temp\svmcp-view-layout.log`. Each file is capped at 4 MiB by clearing the
  whole file when it fills; there is no dated rotation or age-based retention.
- Performance events are emitted from `TeklaMcpServer.Api`, `TeklaMcpServer`,
  `TeklaBridge`, and `TeklaBridge.Controller`. Each has a `PerfTrace` facade;
  `TeklaBridge` references the router compiled into `TeklaMcpServer.Api`, while
  the server and controller compile linked copies of its source. The bridge
  and API facades run in the same bridge process.
- `TeklaBridge.Controller/BridgeControllerService.cs` already computes
  `%LOCALAPPDATA%\svMCP\logs`, but the tray menu still opens `C:\temp`.
- `TeklaBridge/Program.cs` writes startup/channel diagnostics directly to
  `C:\temp\teklabridge_log.txt` and `C:\temp\tekla_channel.txt`.
- `TeklaMcpServer.Host` has probe-specific temporary log files. These are
  diagnostic artifacts, not part of the normal application trace stream.
- Tekla 2025 loads `TeklaBridge.exe` from
  `C:\TeklaStructures\2025.0\Environments\common\extensions\svMCP\`.
  Its manually maintained `TeklaBridge.exe.config` contains Tekla binding and
  codebase settings and must be preserved during deployment.

## Target design

- Use the `Serilog` and `Serilog.Sinks.File` packages. Pin package versions and
  avoid adding configuration, async, or Microsoft logging adapter packages
  unless implementation demonstrates a need for them.
- Keep `PerfTrace.Write(layer, operation, elapsedMs, details)` as the call-site
  contract. Preserve the current timestamp, PID, layer, operation, duration,
  and details in the text output during the migration.
- Keep two logical streams: performance/bridge activity and detailed view
  layout. Create one Serilog sink per stream per process through the shared
  router. The bridge and API facades must use the same router instance in the
  bridge process; do not create a sink for each `PerfTrace` facade.
- Store runtime logs under `%LOCALAPPDATA%\svMCP\Logs`, outside the Tekla
  installation directory. Use two shared file sets, `svmcp-perf-.log` and
  `svmcp-view-layout-.log`, with daily and size-based rolling. Configure each
  file sink with `shared: true` because the MCP server, controller, and bridge
  may write concurrently. Use the same sink version, file path, and rolling
  settings in every process.
- Set `fileSizeLimitBytes` to 4 MiB, `rollOnFileSizeLimit` to `true`,
  `rollingInterval` to `Day`, `retainedFileTimeLimit` to 14 days, and
  `retainedFileCountLimit` to 32 for each stream. This gives a nominal file
  budget of 128 MiB per stream, 256 MiB total; the count cap can shorten the
  effective history when detailed traces are frequent. Verify the bound and
  useful history with real trace volume before release.
- Initialize the bridge logger early enough to capture startup failures. Move
  the existing startup/channel diagnostics into the managed log stream where
  possible, retaining a minimal fallback for failures that happen before logger
  initialization.
- Update the tray `Open log` action to open the managed log directory or the
  latest performance log, and make the view-layout log easy to reach.
- Keep Host probe logs separate unless a probe becomes a supported runtime
  feature.

## Implementation phases

### 1. Add the shared Serilog facade

- Add the pinned Serilog packages to the projects that compile production trace
  code: `TeklaMcpServer.Api`, `TeklaMcpServer`, and `TeklaBridge.Controller`.
- Replace the internals of the shared logging router with a static, per-process
  Serilog configuration while retaining its two-stream routing behavior.
- Keep `TeklaBridge`'s existing API project reference: its `PerfTrace` facade
  and the API facade must use the same router from that assembly. Do not link a
  second copy of the router into `TeklaBridge` or add a separate bridge logger.
- Keep `PerfTrace` event call sites and their data unchanged.

### 2. Move paths and retention

- Resolve `%LOCALAPPDATA%\svMCP\Logs` at runtime and create the directory when
  the first event is written. Use the same path provider for the controller's
  `LogDirectory` property and the tray menu.
- Configure both shared file sinks with the rolling and retention values above.
  Do not use process-specific filenames unless a directory-wide cleanup policy
  is also added; per-process sink retention would leave old process files behind.
- Keep the performance and view-layout streams separate, and preserve PID and
  timestamp in each entry to correlate the bridge pipeline.

### 3. Migrate startup diagnostics and tray access

- Initialize logging at bridge startup and route the current connection/channel
  diagnostics through it where initialization permits.
- Update the tray `Open log` menu to use the shared log-directory/path provider
  instead of hard-coded `C:\temp` paths.
- Preserve diagnostic probe outputs in `TeklaMcpServer.Host` as separate
  temporary artifacts.

### 4. Update deployment and documentation

- Ensure the build output contains the Serilog runtime assemblies next to the
  executables that need them.
- Update the manual Tekla 2025 deployment instructions to include the required
  Serilog DLLs in the extensions folder alongside `TeklaBridge.exe` and its
  API dependencies.
- Preserve the existing `TeklaBridge.exe.config`; do not overwrite its Tekla
  `<bindingRedirect>` or `<codeBase>` entries.
- Document the new log location, file naming, rotation, retention, and tray
  access in the project documentation.

### 5. Verify the migration

- Build every affected target (`net48` bridge/API and `net8.0-windows` server/
  controller) and inspect the deployed dependency set.
- Confirm Tekla 2025 still connects using the extensions-folder bridge and its
  existing config file.
- Generate entries from each process and confirm the two streams preserve the
  current trace fields and correlate by PID/time.
- Exercise simultaneous writes from all three processes while a shared file
  reaches its size limit and during daily rollover. Restart writers during
  rollover, then check for missing, duplicated, or interleaved records and
  failures that interrupt Tekla operations.
- Verify age cleanup and the 32-file cap for each stream, including when more
  than one process is active. Confirm the retained size stays near the 256 MiB
  budget and that the remaining detailed trace history is useful. Check cleanup
  after new writes/rollover; age retention is not a background timer.
- Confirm the tray menu opens the new log location and startup diagnostics are
  available after a failed bridge launch.

## Acceptance criteria

- No production performance or view-layout trace writes to `C:\temp` remain.
- Logs are stored in `%LOCALAPPDATA%\svMCP\Logs`, roll by date and size, and
  clean up rolled files beyond 32 files or 14 days per stream when the sink
  processes new events, with a nominal 256 MiB total file budget.
- Concurrent process writes, including during rollover and cleanup, do not
  corrupt records or prevent Tekla connection.
- Existing `PerfTrace` call sites and the timing fields they emit are preserved.
- Tekla 2025 deployment retains its working `TeklaBridge.exe.config` and ships
  every required Serilog assembly beside the bridge executable.
- The tray `Open log` action reaches the new logs.

## Risks and constraints

- `TeklaBridge` targets .NET Framework 4.8 and references Tekla assemblies. Keep
  Serilog in the bridge's separate executable process; do not install it into
  Tekla's own process or Tekla installation folders.
- The bridge deployment is partly manual. Missing a Serilog runtime DLL can
  make bridge startup fail; overwriting `TeklaBridge.exe.config` can break the
  Tekla IPC channel. Deployment instructions and verification must cover both.
- The nominal 256 MiB budget covers the two managed streams; one oversized
  record can exceed a per-file limit, and separate Host probe or startup
  fallback artifacts are outside this budget.
