using System.Diagnostics;
using System.Globalization;
using SvMcp.Shared.Logging;

namespace TeklaMcpServer.Api.Diagnostics;

internal static class PerfTrace
{
    private static readonly SvMcpLogRouter LogRouter = new();
    [ThreadStatic] private static ViewLayoutTraceContext? _viewLayoutTrace;

    internal static bool IsViewLayoutDetailedTraceActive => true;

    internal static bool IsDetailedTraceActive => false;

    internal static IDisposable BeginViewLayoutRun(string operation, string details)
    {
        var previous = _viewLayoutTrace;
        var context = new ViewLayoutTraceContext(Guid.NewGuid().ToString("N").Substring(0, 8));
        _viewLayoutTrace = context;
        Write("api-view", "layout_run_start", 0, $"command={operation} {details}");
        return new ViewLayoutTraceScope(previous, operation);
    }

    internal static void CompleteViewLayoutRun(string details)
    {
        if (_viewLayoutTrace is { } context)
            context.CompletionDetails = details;
    }

    public static void Write(string layer, string operation, long elapsedMs, string? details = null)
    {
        if (string.Equals(layer, "api-view", StringComparison.Ordinal)
            && _viewLayoutTrace is { } context)
        {
            if (!string.Equals(operation, "layout_run_end", StringComparison.Ordinal))
            {
                context.LastOperation = operation;
                context.LastDetails = details;
            }

            details = $"run={context.RunId} seq={++context.Sequence} {details}";
        }

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

    private sealed class ViewLayoutTraceContext
    {
        public ViewLayoutTraceContext(string runId)
        {
            RunId = runId;
        }

        public string RunId { get; }

        public int Sequence { get; set; }

        public string? CompletionDetails { get; set; }

        public string? LastOperation { get; set; }

        public string? LastDetails { get; set; }
    }

    private sealed class ViewLayoutTraceScope : IDisposable
    {
        private readonly ViewLayoutTraceContext? _previous;
        private readonly string _operation;
        private bool _disposed;

        public ViewLayoutTraceScope(ViewLayoutTraceContext? previous, string operation)
        {
            _previous = previous;
            _operation = operation;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            var context = _viewLayoutTrace;
            var result = context?.CompletionDetails;
            var incompleteDetails = string.IsNullOrWhiteSpace(result)
                ? $"lastOp={context?.LastOperation ?? "none"} lastDetails={context?.LastDetails ?? "none"}"
                : result;
            Write(
                "api-view",
                "layout_run_end",
                0,
                $"command={_operation} result={(string.IsNullOrWhiteSpace(result) ? "incomplete" : "success")} {incompleteDetails}");
            _viewLayoutTrace = _previous;
            _disposed = true;
        }
    }
}
