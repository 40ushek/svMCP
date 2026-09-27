using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SvMcp.Shared.Logging;

internal sealed class McpFileLoggerProvider : ILoggerProvider
{
    private readonly SvMcpLogRouter _logRouter = new();

    public ILogger CreateLogger(string categoryName) => new McpFileLogger(_logRouter, categoryName);

    public void Dispose()
    {
    }

    private sealed class McpFileLogger(SvMcpLogRouter logRouter, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var exceptionDetails = exception is null ? string.Empty : $" exception={exception}";
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Process.GetCurrentProcess().Id} layer=mcp-host level={logLevel} category={categoryName} eventId={eventId.Id} message={message}{exceptionDetails}");

            logRouter.Append("mcp-host", categoryName, line);
        }
    }
}
