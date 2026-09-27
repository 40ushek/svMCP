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
        var isDimensionAction = IsDimensionAction(command);
        var actionScope = isDimensionAction
            ? DescribeDimensionAction(command, args.Skip(1).ToArray())
            : string.Empty;

        if (isDimensionAction)
            WriteDimensionAction("start", command, actionScope);

        if (!File.Exists(BridgePath))
        {
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=false reason=bridge_missing path={BridgePath}");
            if (isDimensionAction)
                WriteDimensionAction("error", command, $"{actionScope} reason=bridge_missing".Trim());
            return $"Error: TeklaBridge.exe not found at {BridgePath}";
        }

        try
        {
            var timeout = ResolveBridgeResponseTimeout(command);
            var result = Bridge.Execute(command, args.Skip(1).ToArray(), timeout);
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=true args={Math.Max(0, args.Length - 1)} timeoutMs={timeout.TotalMilliseconds} resultBytes={result.Length}");
            if (isDimensionAction)
                WriteDimensionAction("finish", command, $"{actionScope} elapsedMs={total.ElapsedMilliseconds}".Trim());
            return result;
        }
        catch (Exception ex)
        {
            PerfTrace.Write("mcp", command, total.ElapsedMilliseconds, $"ok=false errorType={ex.GetType().Name} message={ex.Message}");
            if (isDimensionAction)
                WriteDimensionAction("error", command, $"{actionScope} error={ex.GetType().Name}".Trim());
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
            "create_dimensions_batch" => TimeSpan.FromMinutes(3),
            _ => PersistentBridge.DefaultResponseTimeout
        };

    private static bool IsDimensionAction(string command)
        => command.Contains("dimension", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("place_control_diagonals", StringComparison.OrdinalIgnoreCase);

    private static void WriteDimensionAction(string state, string command, string scope)
        => PerfTrace.Write(
            "api-dimensions",
            $"ai_action_{state}",
            0,
            $"command={command} {scope}".Trim());

    private static string DescribeDimensionAction(string command, string[] args)
    {
        string Arg(int index) => args.Length > index ? args[index] : string.Empty;
        string View() => string.IsNullOrWhiteSpace(Arg(0)) ? "viewId=all" : $"viewId={Arg(0)}";

        return (command switch
        {
            "create_dimension" => $"viewId={Arg(0)} direction={Arg(2)} pointCount={CountDimensionPoints(Arg(1), Arg(9))}",
            "create_dimensions_batch" => $"viewId={Arg(0)} chainCount={CountJsonItems(Arg(2))}",
            "delete_dimension" => $"dimensionId={Arg(0)}",
            "move_dimension" or "move_angle_dimension" => $"dimensionId={Arg(0)} delta={Arg(1)}",
            "add_dimension_points" or "recreate_dimension" => $"dimensionId={Arg(0)} direction={Arg(2)}",
            "arrange_dimensions" => $"{View()} targetGap={Arg(1)}",
            "combine_dimensions" => $"{View()} previewOnly={Arg(2)}",
            "get_dimension_chain_coverage" => $"viewId={Arg(0)} dimensionId={Arg(1)}",
            "get_angle_dimension_debug" or "draw_angle_dimension_debug_geometry" =>
                $"{View()} dimensionId={(string.IsNullOrWhiteSpace(Arg(1)) ? "all" : Arg(1))}",
            _ => View()
        }).Trim();
    }

    private static int CountDimensionPoints(string points, string pointIds)
    {
        var pointIdCount = CountJsonItems(pointIds);
        if (pointIdCount > 0)
            return pointIdCount;

        var coordinateCount = CountJsonItems(points);
        return coordinateCount > 0 ? coordinateCount / 3 : 0;
    }

    private static int CountJsonItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        }
    }
}
