using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using SvMcp.Bridge;

namespace TeklaMcpServer.Tools;

[McpServerToolType]
public static partial class ModelTools
{
    private static readonly string BridgePath = BridgePathResolver.Resolve(AppContext.BaseDirectory);
    private static readonly BridgeControllerClient Bridge = new();

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
            var result = Bridge.Execute(command, args.Skip(1).ToArray(), timeout);
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
            _ => PersistentBridge.DefaultResponseTimeout
        };
}
