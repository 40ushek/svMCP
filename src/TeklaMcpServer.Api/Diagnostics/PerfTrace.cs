using System.Diagnostics;
using System.Globalization;
using SvMcp.Shared.Logging;

namespace TeklaMcpServer.Api.Diagnostics;

internal static class PerfTrace
{
    private static readonly SvMcpLogRouter LogRouter = new();

    internal static bool IsDetailedTraceActive => false;

    public static void Write(string layer, string operation, long elapsedMs, string? details = null)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.ff} pid={1} layer={2} op={3} elapsedMs={4} {5}",
            DateTimeOffset.Now,
            Process.GetCurrentProcess().Id,
            layer,
            operation,
            elapsedMs,
            details ?? string.Empty);

        LogRouter.Append(layer, operation, line);
    }
}
