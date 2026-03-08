using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace TeklaMcpServer.Tools;

[McpServerToolType]
public static partial class ModelTools
{
    private static readonly string BridgePath = ResolveBridgePath();

    private static string ResolveBridgePath()
    {
        // For TS2025+: TeklaBridge must run from the Tekla extensions folder so that
        // Tekla-generated (or manually created) exe.config loads installed DLLs with
        // the correct channel name (FileVersion 2025.0.52577.0 instead of NuGet 2025.0.0.0).
        var teklaBase = @"C:\TeklaStructures";
        if (Directory.Exists(teklaBase))
        {
            // Find newest installed version >= 2025
            var extensionsBridge = Directory.GetDirectories(teklaBase)
                .Where(d => Version.TryParse(Path.GetFileName(d), out var v) && v.Major >= 2025)
                .OrderByDescending(d => Version.Parse(Path.GetFileName(d)))
                .Select(d => Path.Combine(d, "Environments", "common", "extensions", "svMCP", "TeklaBridge.exe"))
                .FirstOrDefault(f => File.Exists(f));
            if (extensionsBridge != null)
                return extensionsBridge;
        }

        return Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "bridge", "TeklaBridge.exe");
    }

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
        if (!string.IsNullOrEmpty(result))
            result = ExtractJsonPayload(result);

        if (string.IsNullOrEmpty(result))
            result = JsonSerializer.Serialize(new { error = "TeklaBridge produced no output", stderr = stderr.Trim() });
        return result;
    }

    private static string ExtractJsonPayload(string output)
    {
        // Strip any non-JSON prefix (BOM, leaked diagnostics). Keep full JSON payload, including multi-line.
        var trimmed = output.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (LooksLikeJson(trimmed) && IsValidJson(trimmed))
            return trimmed;

        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            var ch = trimmed[i];
            if (ch != '{' && ch != '[')
                continue;

            var candidate = trimmed.Substring(i).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (LooksLikeJson(candidate) && IsValidJson(candidate))
            {
                return candidate;
            }
        }

        return trimmed;
    }

    private static bool LooksLikeJson(string value)
    {
        return !string.IsNullOrEmpty(value) && (value[0] == '{' || value[0] == '[');
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
