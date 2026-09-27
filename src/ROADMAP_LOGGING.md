# Logging Roadmap

**Status:** implemented and deployed to Tekla 2025 extensions; live Tekla connection and daily-rollover acceptance pending

**Updated:** 2026-09-27

## Goal

Replace the hand-written production file writer with Serilog, use a per-user log
directory, retain logs by age and size, and keep the existing performance trace
events useful for investigating MCP and Tekla operation timings.

## Previous state

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
- Keep five streams with readable names: `operations`, `dimensions`, `views`,
  `marks`, and `geometry`. General MCP/bridge timings and startup diagnostics
  go to `operations`; detailed API events go to their domain stream. Route by
  stable `layer` names, with `api-geometry` operations beginning `dimension_`
  going to `dimensions`. The bridge and API facades share one static sink set
  in the bridge process; do not create a sink for each `PerfTrace` facade.
- In `dimensions`, write a short start and finish/error entry for each
  dimension-related MCP action initiated by the AI. Include the command and
  only a compact scope summary such as view ID, dimension ID, point count, or
  chain count. Do not log prompts, reasoning, or raw coordinate arrays.
- Store runtime logs under `%LOCALAPPDATA%\svMCP\Logs`, outside the Tekla
  installation directory. Use `operations-.log`, `dimensions-.log`,
  `views-.log`, `marks-.log`, and `geometry-.log` file sets, with daily and
  size-based rolling. Serilog appends the date to each filename. Configure each
  file sink with `shared: true` because the MCP server, controller, and bridge
  may write concurrently. Use the same sink version, file path, and rolling
  settings in every process.
- Set `fileSizeLimitBytes` to 4 MiB, `rollOnFileSizeLimit` to `true`,
  `rollingInterval` to `Day`, `retainedFileTimeLimit` to 14 days, and
  `retainedFileCountLimit` to 32 for `dimensions`/`views` and 16 for the other
  streams. This gives a nominal file budget of 448 MiB total; the count cap
  can shorten the effective history when detailed traces are frequent. Verify
  the bound and useful history with real trace volume before release.
- Initialize the bridge logger early enough to capture startup failures. Move
  the existing startup/channel diagnostics into the managed log stream where
  possible, retaining a minimal fallback for failures that happen before logger
  initialization.
- Update the tray `Open log` action to open the managed log directory so all
  domain streams are easy to reach.
- Keep Host probe logs separate unless a probe becomes a supported runtime
  feature.

## Implementation phases

### 1. Add the shared Serilog facade

- Add the pinned Serilog packages to the projects that compile production trace
  code: `TeklaMcpServer.Api`, `TeklaMcpServer`, and `TeklaBridge.Controller`.
- Replace the internals of the shared logging router with a static, per-process
  Serilog configuration and route events to the five streams above.
- Keep `TeklaBridge`'s existing API project reference: its `PerfTrace` facade
  and the API facade must use the same router from that assembly. Do not link a
  second copy of the router into `TeklaBridge` or add a separate bridge logger.
- Keep `PerfTrace` event call sites and their data unchanged.

### 2. Move paths and retention

- Resolve `%LOCALAPPDATA%\svMCP\Logs` at runtime and create the directory when
  the first event is written. Use the same path provider for the controller's
  `LogDirectory` property and the tray menu.
- Configure all five shared file sinks with the rolling and retention values above.
  Do not use process-specific filenames unless a directory-wide cleanup policy
  is also added; per-process sink retention would leave old process files behind.
- Preserve PID and timestamp in each entry to correlate the bridge pipeline
  across `operations` and the domain streams.

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
- Update the manual Tekla 2025 deployment instructions to include the Serilog
  assemblies and their runtime dependencies in the extensions folder alongside
  `TeklaBridge.exe` and its API dependencies.
- Preserve the existing `TeklaBridge.exe.config`; merge the Serilog 4.3.0
  binding redirect into it without removing Tekla `<bindingRedirect>` or
  `<codeBase>` entries.
- Document the new log location, file naming, rotation, retention, and tray
  access in the project documentation.

### 5. Verify the migration

- Build every affected target (`net48` bridge/API and `net8.0-windows` server/
  controller) and inspect the deployed dependency set.
- Confirm Tekla 2025 still connects using the extensions-folder bridge and its
  existing config file.
- Generate entries from each process and confirm that all five streams preserve
  the current trace fields and correlate by PID/time.
- Exercise simultaneous writes from all three processes while a shared file
  reaches its size limit and during daily rollover. Restart writers during
  rollover, then check for missing, duplicated, or interleaved records and
  failures that interrupt Tekla operations.
- Verify age cleanup and the 32/16-file caps, including when more than one
  process is active. Confirm the retained size stays near the 448 MiB
  budget and that the remaining detailed trace history is useful. Check cleanup
  after new writes/rollover; age retention is not a background timer.
- Confirm the tray menu opens the new log location and startup diagnostics are
  available after a failed bridge launch.

## Acceptance criteria

- No production trace writes to `C:\temp` remain.
- Logs are stored in `%LOCALAPPDATA%\svMCP\Logs`, roll by date and size, and
  clean up rolled files beyond their 32/16-file caps or 14 days when the sink
  processes new events, with a nominal 448 MiB total file budget.
- Concurrent process writes, including during rollover and cleanup, do not
  corrupt records or prevent Tekla connection.
- Existing `PerfTrace` call sites and the timing fields they emit are preserved.
- Dimension-related AI actions leave a concise start and finish/error trail in
  `dimensions` without logging prompts, reasoning, or raw geometry.
- Tekla 2025 deployment retains its working `TeklaBridge.exe.config` and ships
  every required Serilog assembly beside the bridge executable.
- The tray `Open log` action reaches the new logs.

## Verification on 2026-09-27

- Built the net48 API/bridge and net8 server/controller targets. The bridge,
  server, and controller outputs contain `Serilog.dll` and
  `Serilog.Sinks.File.dll`.
- Started the built bridge without a command and observed its channel-fix
  event in `operations-YYYYMMDD.log`.
- A logging-only local check wrote to all five streams. Three concurrent
  processes produced 1,200 complete records; concurrent size-based rollover,
  file-count retention, and removal of an aged rolled file also passed.
- Deployed the logging-only bridge/API build and required runtime DLLs to the
  Tekla 2025 extensions folder. Preserved its existing Tekla config entries and
  added the Serilog 4.3.0 binding redirect.
- The deployed bridge wrote startup and `check_connection` events to the new
  operations log. The connection check could not complete because Tekla
  Structures was not running; tray interaction and actual calendar-day
  rollover remain to be verified in the live environment.
- The MCP server's concise dimension-action logging was built from a clean
  logging-only snapshot and deployed to its Release directory. The prior MCP
  process was stopped to release the locked assembly; the client will load the
  new version on its next launch. The updated tray controller was also deployed
  to the server's `controller` directory. Runtime log output and tray interaction
  remain to be confirmed.

## Risks and constraints

- `TeklaBridge` targets .NET Framework 4.8 and references Tekla assemblies. Keep
  Serilog in the bridge's separate executable process; do not install it into
  Tekla's own process or Tekla installation folders.
- The bridge deployment is partly manual. Missing a Serilog runtime DLL or its
  binding redirect can make bridge startup fail; overwriting
  `TeklaBridge.exe.config` can break the Tekla IPC channel. Deployment
  instructions and verification must cover both.
- The nominal 448 MiB budget covers the five managed streams; one oversized
  record can exceed a per-file limit, and separate Host probe or startup
  fallback artifacts are outside this budget.
