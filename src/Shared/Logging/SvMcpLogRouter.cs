using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Threading;

namespace SvMcp.Shared.Logging;

internal sealed class SvMcpLogRouter
{
    private const long MaxLogBytes = 4L * 1024 * 1024;
    private const int RetainedDetailFiles = 32;
    private const int RetainedOtherFiles = 16;
    private static readonly TimeSpan RetainedAge = TimeSpan.FromDays(14);

    private static readonly Lazy<Logger> Operations =
        new(() => CreateLogger("operations", RetainedOtherFiles), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Logger> Dimensions =
        new(() => CreateLogger("dimensions", RetainedDetailFiles), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Logger> Views =
        new(() => CreateLogger("views", RetainedDetailFiles), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Logger> Marks =
        new(() => CreateLogger("marks", RetainedOtherFiles), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Logger> Geometry =
        new(() => CreateLogger("geometry", RetainedOtherFiles), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "svMCP", "Logs");

    public static void Initialize() => _ = Operations.Value;

    public void Append(string layer, string operation, string line)
    {
        try
        {
            SelectLogger(layer, operation).Write(LogEventLevel.Information, "{Line}", line);
        }
        catch
        {
            // Logging must not interrupt a Tekla operation or the MCP protocol.
        }
    }

    private static Logger CreateLogger(string stream, int retainedFiles)
    {
        Directory.CreateDirectory(LogDirectory);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(LogDirectory, stream + "-.log"),
                outputTemplate: "{Message:lj}{NewLine}",
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: MaxLogBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFiles,
                retainedFileTimeLimit: RetainedAge,
                shared: true)
            .CreateLogger();
    }

    private static Logger SelectLogger(string layer, string operation)
    {
        if (string.Equals(layer, "api-view", StringComparison.Ordinal))
            return Views.Value;
        if (layer.StartsWith("api-dimensions", StringComparison.Ordinal)
            || (string.Equals(layer, "api-geometry", StringComparison.Ordinal)
                && operation.StartsWith("dimension_", StringComparison.Ordinal)))
            return Dimensions.Value;
        if (string.Equals(layer, "api-mark", StringComparison.Ordinal)
            || string.Equals(layer, "bridge-mark", StringComparison.Ordinal))
            return Marks.Value;
        if (string.Equals(layer, "api-geometry", StringComparison.Ordinal))
            return Geometry.Value;
        return Operations.Value;
    }
}
