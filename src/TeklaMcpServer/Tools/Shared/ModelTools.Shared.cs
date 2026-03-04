using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

[McpServerToolType]
public static partial class ModelTools
{
    private static readonly string BridgePath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "bridge", "TeklaBridge.exe");

    private static string RunBridge(params string[] args)
    {
        if (!File.Exists(BridgePath))
            return $"Error: TeklaBridge.exe not found at {BridgePath}";

        var startInfo = new ProcessStartInfo
        {
            FileName = BridgePath,
            WorkingDirectory = Path.GetDirectoryName(BridgePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var proc = new Process
        {
            StartInfo = startInfo
        };

        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        var result = output.Trim();
        // Strip any non-JSON prefix (BOM, Tekla diagnostics leaked before Console capture).
        // Find the LAST line starting with '{' or '[' — the actual JSON is always last.
        var lines = result.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].TrimStart('\r');
            if (line.Length > 0 && (line[0] == '{' || line[0] == '['))
            {
                result = line;
                break;
            }
        }
        if (string.IsNullOrEmpty(result))
            result = JsonSerializer.Serialize(new { error = "TeklaBridge produced no output", stderr = stderr.Trim() });
        return result;
    }
}
