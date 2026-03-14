using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TeklaMcpServer.Api.Diagnostics;

internal static class PerfTrace
{
    private static readonly object Sync = new();
    private static readonly bool Enabled = IsEnabled();
    private static readonly string LogPath = ResolveLogPath();

    public static void Write(string layer, string operation, long elapsedMs, string? details = null)
    {
        if (!Enabled)
            return;

        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:O} pid={1} layer={2} op={3} elapsedMs={4} {5}",
            DateTimeOffset.Now,
            Process.GetCurrentProcess().Id,
            layer,
            operation,
            elapsedMs,
            details ?? string.Empty);

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? Path.GetTempPath());
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Ignore profiling IO failures.
        }
    }

    private static bool IsEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("SVMCP_PERF");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLogPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SVMCP_PERF_LOG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return Path.Combine(Path.GetTempPath(), "svmcp-perf.log");
    }
}
