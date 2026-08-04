using System;
using System.Diagnostics;
using System.Globalization;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContextTrace : IDisposable
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly int _viewId;
    private bool _disposed;

    private DimensionContextTrace(int viewId)
    {
        _viewId = viewId;
        Trace("start", $"viewId={viewId}");
    }

    public string TraceId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public static DimensionContextTrace Start(int viewId) => new(viewId);

    public IDisposable Stage(string stage, string details = "")
        => new StageScope(this, stage, details);

    public void Event(string operation, string details = "")
        => Trace(operation, details);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Trace("end", $"viewId={_viewId}");
    }

    private void Trace(string operation, string details)
    {
        PerfTrace.Write(
            "api-dimensions-detail",
            operation,
            _total.ElapsedMilliseconds,
            $"traceId={TraceId} viewId={_viewId} {details}".Trim());
    }

    private sealed class StageScope : IDisposable
    {
        private readonly DimensionContextTrace _owner;
        private readonly string _stage;
        private readonly string _details;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private bool _disposed;

        public StageScope(DimensionContextTrace owner, string stage, string details)
        {
            _owner = owner;
            _stage = stage;
            _details = details;
            _owner.Trace($"{stage}_start", details);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner.Trace(
                $"{_stage}_end",
                $"elapsedMs={_timer.ElapsedMilliseconds} {_details}".Trim());
        }
    }
}
