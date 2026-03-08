using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TeklaMcpServer.Tools;

internal sealed class PersistentBridge : IDisposable
{
    private static readonly JsonSerializerOptions ProtocolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _bridgePath;
    private readonly string _workingDirectory;
    private readonly string[] _startupArgs;
    private readonly TimeSpan _responseTimeout;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private StreamReader? _stderr;
    private Task? _stderrDrainTask;
    private int _nextId;

    internal PersistentBridge(
        string bridgePath,
        string workingDirectory,
        string[] startupArgs,
        TimeSpan responseTimeout)
    {
        _bridgePath = bridgePath;
        _workingDirectory = workingDirectory;
        _startupArgs = startupArgs;
        _responseTimeout = responseTimeout;
    }

    public string Send(string command, params string[] args)
    {
        _lock.Wait();
        try
        {
            EnsureStarted();

            var request = new BridgeRequest
            {
                Id = Interlocked.Increment(ref _nextId),
                Cmd = command,
                Args = args
            };

            _stdin!.WriteLine(JsonSerializer.Serialize(request, ProtocolJsonOptions));
            _stdin.Flush();

            var responseLine = ReadResponseLine();
            var response = JsonSerializer.Deserialize<BridgeResponse>(responseLine, ProtocolJsonOptions)
                ?? throw new InvalidDataException("Bridge returned an empty protocol response.");
            if (response.Id != request.Id)
                throw new InvalidDataException($"Bridge protocol error: response id {response.Id} did not match request id {request.Id}.");

            if (!response.Ok)
                throw new InvalidDataException("Bridge protocol error: " + (response.Error ?? "Unknown bridge error."));

            var result = response.Result ?? string.Empty;
            if (ShouldRestartAfterPayload(result))
                KillProcess();

            return result;
        }
        catch
        {
            KillProcess();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        KillProcess();
        _lock.Dispose();
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false })
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _bridgePath,
            WorkingDirectory = _workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in _startupArgs)
            startInfo.ArgumentList.Add(arg);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start TeklaBridge process.");
        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;
        _stdout = _process.StandardOutput;
        _stderr = _process.StandardError;
        _stderrDrainTask = Task.Run(() => DrainStderrAsync(_stderr));
    }

    private string ReadResponseLine()
    {
        var readTask = _stdout!.ReadLineAsync();
        if (!readTask.Wait(_responseTimeout))
        {
            throw new TimeoutException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Timed out waiting for TeklaBridge response after {0} ms.",
                    _responseTimeout.TotalMilliseconds));
        }

        return readTask.Result
            ?? throw new EndOfStreamException("TeklaBridge closed stdout before returning a response.");
    }

    private static async Task DrainStderrAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync().ConfigureAwait(false) != null)
            {
            }
        }
        catch
        {
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            _stderr?.Dispose();
            _process?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _stdin = null;
            _stdout = null;
            _stderr = null;
            _stderrDrainTask = null;
            _process = null;
        }
    }

    private static bool ShouldRestartAfterPayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return false;
            }

            var error = errorElement.GetString();
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.Contains("Not connected to Tekla Structures", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Failed to connect to an IPC Port", StringComparison.OrdinalIgnoreCase)
                || error.Contains("IPC", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed class BridgeRequest
    {
        public int Id { get; set; }
        public string Cmd { get; set; } = string.Empty;
        public string[] Args { get; set; } = [];
    }

    private sealed class BridgeResponse
    {
        public int Id { get; set; }
        public bool Ok { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
    }
}
