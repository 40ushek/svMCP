using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace TeklaMcpServer.Tests;

internal static class BridgeTestHelpers
{
    internal static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
                File.Exists(Path.Combine(current.FullName, "src", "svMCP.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    internal static string GetBuiltBridgePath()
    {
        var repoRoot = FindRepoRoot();
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "TeklaBridge", "bin", "Debug", "net48", "TeklaBridge.exe"),
            Path.Combine(repoRoot, "src", "TeklaMcpServer", "bin", "Debug", "net8.0-windows", "bridge", "TeklaBridge.exe"),
            Path.Combine(repoRoot, "src", "TeklaBridge", "bin", "Release", "net48", "TeklaBridge.exe"),
            Path.Combine(repoRoot, "src", "TeklaMcpServer", "bin", "Release", "net8.0-windows", "bridge", "TeklaBridge.exe")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
            throw new FileNotFoundException("Built TeklaBridge.exe not found. Build the solution before running tests.");

        return path;
    }

    internal static JsonDocument RunBridgeOneShotJson(string command, params string[] args)
    {
        using var process = StartBridgeProcess(command, args);
        var output = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertProcessExitedSuccessfully(process, stderr);
        return JsonDocument.Parse(output);
    }

    internal static BridgeLoopSession StartLoopSession()
    {
        var bridgePath = GetBuiltBridgePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = bridgePath,
            WorkingDirectory = Path.GetDirectoryName(bridgePath)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--loop");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start TeklaBridge --loop process.");
        return new BridgeLoopSession(process);
    }

    internal static void AssertJsonPayloadShape(JsonElement element, string successField)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        if (element.TryGetProperty("error", out _))
            return;

        Assert.True(
            element.TryGetProperty(successField, out _),
            $"Expected JSON object to contain either 'error' or '{successField}'.");
    }

    private static Process StartBridgeProcess(string command, string[] args)
    {
        var bridgePath = GetBuiltBridgePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = bridgePath,
            WorkingDirectory = Path.GetDirectoryName(bridgePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(command);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start TeklaBridge process.");
    }

    private static void AssertProcessExitedSuccessfully(Process process, string stderr)
    {
        Assert.True(
            process.ExitCode == 0 || process.ExitCode == 1,
            $"Unexpected bridge exit code {process.ExitCode}. stderr: {stderr}");
    }

    internal sealed class BridgeLoopSession : IDisposable
    {
        private readonly Process _process;

        internal BridgeLoopSession(Process process)
        {
            _process = process;
        }

        public string Send(string command, params string[] args)
        {
            var request = JsonSerializer.Serialize(new { id = 1, cmd = command, args });
            _process.StandardInput.WriteLine(request);
            _process.StandardInput.Flush();

            var readTask = _process.StandardOutput.ReadLineAsync();
            Assert.True(readTask.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for loop response.");
            var responseLine = readTask.Result;
            Assert.False(string.IsNullOrWhiteSpace(responseLine), "Loop response was empty.");
            return responseLine!;
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
