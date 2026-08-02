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
    private static readonly PersistentBridge Bridge = new(
        BridgePath,
        Path.GetDirectoryName(BridgePath) ?? AppContext.BaseDirectory,
        ["--loop"]);

    static ModelTools()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Bridge.Dispose();
    }

    private static string ResolveBridgePath()
    {
        // For TS2025+: TeklaBridge must run from the Tekla extensions folder so that
        // Tekla-generated (or manually created) exe.config loads installed DLLs with
        // the correct channel name (FileVersion 2025.0.52577.0 instead of NuGet 2025.0.0.0).
        var teklaBase = @"C:\TeklaStructures";
        if (Directory.Exists(teklaBase))
        {
            var extensionsBridge = Directory.GetDirectories(teklaBase)
                .Where(d => Version.TryParse(Path.GetFileName(d), out var v) && v.Major >= 2025)
                .OrderByDescending(d => Version.Parse(Path.GetFileName(d)))
                .Select(d => Path.Combine(d, "Environments", "common", "extensions", "svMCP", "TeklaBridge.exe"))
                .FirstOrDefault(File.Exists);
            if (extensionsBridge != null)
                return extensionsBridge;
        }

        return Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "bridge",
            "TeklaBridge.exe");
    }

    private static string RunBridge(params string[] args)
    {
        var total = Stopwatch.StartNew();
        if (args.Length == 0)
        {
            PerfTrace.Write("mcp", "run_bridge", total.ElapsedMilliseconds, "ok=false reason=no_command");
            return JsonSerializer.Serialize(new { error = "No bridge command specified" });
        }

        var command = args[0];

        if (!File.Exists(BridgePath))
        {
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=false reason=bridge_missing path={BridgePath}");
            return $"Error: TeklaBridge.exe not found at {BridgePath}";
        }

        try
        {
            var timeout = ResolveBridgeResponseTimeout(command);
            var result = Bridge.SendWithTimeout(command, args.Skip(1).ToArray(), timeout);
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=true args={Math.Max(0, args.Length - 1)} timeoutMs={timeout.TotalMilliseconds} resultBytes={result.Length}");
            return result;
        }
        catch (Exception ex)
        {
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=false errorType={ex.GetType().Name} message={ex.Message}");
            return JsonSerializer.Serialize(new
            {
                error = FormatBridgeError(ex)
            });
        }
    }

    private static string FormatBridgeError(Exception ex)
    {
        if (ex is InvalidDataException)
            return ex.Message;

        const string prefix = "TeklaBridge transport error: ";
        return ex.Message.StartsWith(prefix, StringComparison.Ordinal)
            ? ex.Message
            : prefix + ex.Message;
    }

    private static TimeSpan ResolveBridgeResponseTimeout(string command)
        => command switch
        {
            "arrange_marks_force" => TimeSpan.FromMinutes(5),
            "fit_views_to_sheet" => TimeSpan.FromMinutes(3),
            "arrange_views_only" => TimeSpan.FromMinutes(3),
            "open_drawing" => TimeSpan.FromMinutes(2),
            // Reads candidate points for every distinct part in the view, one bridge-side Tekla
            // call per part, sequentially. On a large view that alone can pass the 30 s default
            // before the detector even runs — see get_part_candidate_points_in_view's own note on
            // avoiding the MCP timeout by calling the bridge directly.
            "get_dimension_defects" => TimeSpan.FromMinutes(3),
            _ => PersistentBridge.DefaultResponseTimeout
        };
}
