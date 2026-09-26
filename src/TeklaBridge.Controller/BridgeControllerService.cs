using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SvMcp.Bridge;
using TeklaMcpServer.Tools;

namespace TeklaBridge.Controller;

internal sealed class BridgeControllerService : IDisposable
{
    private readonly object _gate = new();
    private readonly string _bridgePath;
    private readonly string _statePath;
    private readonly PersistentBridge _bridge;
    private volatile bool _paused;
    private volatile string? _lastError;
    private volatile string _teklaConnection = "Unknown";

    public BridgeControllerService()
    {
        var controllerDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var serverDirectory = Directory.GetParent(controllerDirectory)?.FullName ?? controllerDirectory;
        _bridgePath = BridgePathResolver.Resolve(serverDirectory);
        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "svMCP",
            "bridge-state.json");
        _paused = ReadPausedState();
        _bridge = new PersistentBridge(
            _bridgePath,
            Path.GetDirectoryName(_bridgePath) ?? serverDirectory,
            ["--loop"]);
    }

    public event EventHandler? StatusChanged;

    public BridgeControllerStatus GetStatus()
    {
        var running = _bridge.IsRunning;
        return new BridgeControllerStatus
        {
            State = _paused ? "Paused" : running ? "Running" : "Stopped",
            BridgeRunning = running,
            Paused = _paused,
            TeklaConnection = _teklaConnection,
            Error = _lastError
        };
    }

    public BridgeControllerResponse Handle(BridgeControllerRequest request)
    {
        try
        {
            switch (request.Operation)
            {
                case "execute":
                    if (string.IsNullOrWhiteSpace(request.Command))
                        throw new InvalidDataException("Bridge command is required.");
                    lock (_gate)
                    {
                        if (_paused)
                            throw new InvalidOperationException("TeklaBridge is stopped. Resume it from the system tray.");
                        var timeout = request.TimeoutMilliseconds > 0
                            ? TimeSpan.FromMilliseconds(request.TimeoutMilliseconds)
                            : PersistentBridge.DefaultResponseTimeout;
                        var result = _bridge.SendWithTimeout(request.Command, request.Args, timeout);
                        _lastError = null;
                        if (string.Equals(request.Command, "check_connection", StringComparison.OrdinalIgnoreCase))
                            _teklaConnection = ContainsError(result) ? "Disconnected" : "Connected";
                        else if (ContainsError(result))
                            _teklaConnection = "Unknown";
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                        return new BridgeControllerResponse { Id = request.Id, Ok = true, Result = result, Status = Snapshot() };
                    }
                case "status":
                    return new BridgeControllerResponse { Id = request.Id, Ok = true, Status = GetStatus() };
                case "stop":
                    Stop();
                    return new BridgeControllerResponse { Id = request.Id, Ok = true, Status = GetStatus() };
                case "resume":
                    Resume();
                    return new BridgeControllerResponse { Id = request.Id, Ok = true, Status = GetStatus() };
                case "restart":
                    Restart();
                    return new BridgeControllerResponse { Id = request.Id, Ok = true, Status = GetStatus() };
                default:
                    throw new InvalidDataException($"Unsupported controller operation '{request.Operation}'.");
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _lastError = ex.Message;
                if (string.Equals(request.Operation, "execute", StringComparison.OrdinalIgnoreCase))
                    _teklaConnection = "Unknown";
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return new BridgeControllerResponse { Id = request.Id, Ok = false, Error = ex.Message, Status = GetStatus() };
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _bridge.Stop();
            _paused = true;
            _teklaConnection = "Unknown";
            _lastError = null;
            SavePausedState();
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        lock (_gate)
        {
            _bridge.Start();
            _paused = false;
            _teklaConnection = "Unknown";
            _lastError = null;
            SavePausedState();
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Restart()
    {
        lock (_gate)
        {
            try
            {
                _bridge.Restart();
                _paused = false;
                _teklaConnection = "Unknown";
                _lastError = null;
                SavePausedState();
            }
            catch
            {
                _paused = true;
                _teklaConnection = "Unknown";
                SavePausedState();
                throw;
            }
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "svMCP", "logs");

    public void Dispose()
    {
        _bridge.Dispose();
    }

    private BridgeControllerStatus Snapshot()
    {
        var running = _bridge.IsRunning;
        return new BridgeControllerStatus
        {
            State = _paused ? "Paused" : running ? "Running" : "Stopped",
            BridgeRunning = running,
            Paused = _paused,
            TeklaConnection = _teklaConnection,
            Error = _lastError
        };
    }

    private static bool ContainsError(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out _);
        }
        catch
        {
            return false;
        }
    }

    private bool ReadPausedState()
    {
        try
        {
            return File.Exists(_statePath) &&
                JsonSerializer.Deserialize<BridgeControllerStatus>(File.ReadAllText(_statePath))?.Paused == true;
        }
        catch
        {
            return false;
        }
    }

    private void SavePausedState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(new BridgeControllerStatus { Paused = _paused }));
        }
        catch (Exception ex)
        {
            _lastError = "Could not save bridge state: " + ex.Message;
        }
    }
}

internal sealed class BridgePipeServer : IDisposable
{
    private readonly BridgeControllerService _service;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    public BridgePipeServer(BridgeControllerService service) => _service = service;

    public void Start() => _serverTask = Task.Run(RunAsync);

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    BridgeControllerProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                if (line == null)
                    continue;
                var request = JsonSerializer.Deserialize<BridgeControllerRequest>(line, BridgeControllerProtocol.JsonOptions);
                var response = request == null
                    ? new BridgeControllerResponse { Ok = false, Error = "Invalid controller request." }
                    : _service.Handle(request);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, BridgeControllerProtocol.JsonOptions)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, _cancellation.Token).ConfigureAwait(false);
            }
        }
    }
}
