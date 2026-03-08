using ModelContextProtocol.Server;
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
        ["--loop"],
        TimeSpan.FromSeconds(30));

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
        if (args.Length == 0)
            return JsonSerializer.Serialize(new { error = "No bridge command specified" });

        if (!File.Exists(BridgePath))
            return $"Error: TeklaBridge.exe not found at {BridgePath}";

        try
        {
            return Bridge.Send(args[0], args.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
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
}
