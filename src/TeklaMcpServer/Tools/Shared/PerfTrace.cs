using System.Diagnostics;
using System.Globalization;
using SvMcp.Shared.Logging;

namespace TeklaMcpServer.Tools;

internal static class PerfTrace
{
    private static readonly SvMcpLogRouter LogRouter = new();

    public static void Write(string layer, string operation, long elapsedMs, string? details = null)
    {
        var line = BuildLine(layer, operation, elapsedMs, details);
        LogRouter.Append(layer, operation, line);
    }

    private static string BuildLine(string layer, string operation, long elapsedMs, string? details)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:O} pid={1} layer={2} op={3} elapsedMs={4} {5}",
            DateTimeOffset.Now,
            Process.GetCurrentProcess().Id,
            layer,
            operation,
            elapsedMs,
            details ?? string.Empty);
    }
}
