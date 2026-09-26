using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TeklaMcpServer.Tools;

internal sealed class PersistentBridge : IDisposable
{
    internal static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions ProtocolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _bridgePath;
    private readonly string _workingDirectory;
    private readonly string[] _startupArgs;
    private readonly TimeSpan _defaultResponseTimeout;
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
        TimeSpan responseTimeout = default)
    {
        _bridgePath = bridgePath;
        _workingDirectory = workingDirectory;
        _startupArgs = startupArgs;
        _defaultResponseTimeout = responseTimeout == default ? DefaultResponseTimeout : responseTimeout;
    }

    public string Send(string command, params string[] args)
        => SendCore(command, args, null);

    public string SendWithTimeout(string command, string[] args, TimeSpan responseTimeout)
        => SendCore(command, args, responseTimeout);

    internal bool IsRunning
    {
        get
        {
            var process = _process;
            if (process == null)
                return false;

            try { return !process.HasExited; }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    internal void Start()
    {
        _lock.Wait();
        try { EnsureStarted(); }
        finally { _lock.Release(); }
    }

    internal void Stop()
    {
        _lock.Wait();
        try { KillProcess(); }
        finally { _lock.Release(); }
    }

    internal void Restart()
    {
        _lock.Wait();
        try
        {
            KillProcess();
            EnsureStarted();
        }
        finally { _lock.Release(); }
    }

    private string SendCore(string command, string[] args, TimeSpan? responseTimeout)
    {
        var total = Stopwatch.StartNew();
        var effectiveResponseTimeout = responseTimeout ?? _defaultResponseTimeout;
        var wait = Stopwatch.StartNew();
        _lock.Wait();
        wait.Stop();
        try
        {
            EnsureStarted();

            var request = new BridgeRequest
            {
                Id = Interlocked.Increment(ref _nextId),
                Cmd = command,
                Args = args
            };

            var requestJson = JsonSerializer.Serialize(request, ProtocolJsonOptions);
            var write = Stopwatch.StartNew();
            _stdin!.WriteLine(requestJson);
            _stdin.Flush();
            write.Stop();

            var read = Stopwatch.StartNew();
            var responseLine = ReadResponseLine(effectiveResponseTimeout);
            read.Stop();

            var parse = Stopwatch.StartNew();
            var response = JsonSerializer.Deserialize<BridgeResponse>(responseLine, ProtocolJsonOptions)
                ?? throw new InvalidDataException("Bridge returned an empty protocol response.");
            parse.Stop();

            if (response.Id != request.Id)
                throw new InvalidDataException($"Bridge protocol error: response id {response.Id} did not match request id {request.Id}.");

            if (!response.Ok)
                throw new InvalidDataException("Bridge protocol error: " + (response.Error ?? "Unknown bridge error."));

            var result = response.Result ?? string.Empty;
            var restart = false;
            if (ShouldRestartAfterPayload(result))
            {
                restart = true;
                KillProcess();
            }

            PerfTrace.Write(
                "transport",
                command,
                total.ElapsedMilliseconds,
                $"ok=true waitMs={wait.ElapsedMilliseconds} writeMs={write.ElapsedMilliseconds} readMs={read.ElapsedMilliseconds} parseMs={parse.ElapsedMilliseconds} timeoutMs={effectiveResponseTimeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} requestBytes={requestJson.Length} responseBytes={responseLine.Length} restarted={restart}");

            return result;
        }
        catch (Exception ex)
        {
            PerfTrace.Write(
                "transport",
                command,
                total.ElapsedMilliseconds,
                $"ok=false waitMs={wait.ElapsedMilliseconds} timeoutMs={effectiveResponseTimeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} errorType={ex.GetType().Name} message={ex.Message}");
            try
            {
                KillProcess();
            }
            catch (Exception cleanupException)
            {
                PerfTrace.Write(
                    "transport",
                    command + "_cleanup",
                    total.ElapsedMilliseconds,
                    $"ok=false errorType={cleanupException.GetType().Name} message={cleanupException.Message}");
            }
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        Stop();
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

    private string ReadResponseLine(TimeSpan responseTimeout)
    {
        var readTask = _stdout!.ReadLineAsync();
        if (!readTask.Wait(responseTimeout))
        {
            throw new TimeoutException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Timed out waiting for TeklaBridge response after {0} ms.",
                    responseTimeout.TotalMilliseconds));
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
        var process = _process;
        if (process != null && !HasExited(process))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (HasExited(process))
            {
                // It exited between the HasExited check and Kill call.
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) && !HasExited(process))
                throw new TimeoutException("TeklaBridge did not exit within 10 seconds after the stop request.");
        }

        if (process != null && !HasExited(process))
            throw new TimeoutException("TeklaBridge is still running; its files may still be in use.");

        try
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            _stderr?.Dispose();
        }
        catch
        {
        }

        try
        {
            process?.Dispose();
        }
        finally
        {
            if (ReferenceEquals(_process, process))
                _process = null;
            _stdin = null;
            _stdout = null;
            _stderr = null;
            _stderrDrainTask = null;
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (ObjectDisposedException) { return true; }
        catch (InvalidOperationException) { return true; }
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
