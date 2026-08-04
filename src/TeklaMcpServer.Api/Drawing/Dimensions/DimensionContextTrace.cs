using System;
using System.Diagnostics;
using System.Globalization;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionContextTrace : IDisposable
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly string _command;
    private readonly string _scopeDetails;
    private bool _disposed;

    private DimensionContextTrace(string command, string scopeDetails)
    {
        _command = command;
        _scopeDetails = scopeDetails;
        Trace("start", string.Empty);
    }

    public string TraceId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public static DimensionContextTrace Start(int viewId)
        => new("get_dimension_contexts", $"viewId={viewId}");

    public static DimensionContextTrace StartArrange(
        int? viewId,
        double targetGap,
        bool allowInwardCorrectionFromPartsBounds)
        => new(
            "arrange_dimensions",
            $"viewId={(viewId.HasValue ? viewId.Value.ToString() : "all")} targetGap={targetGap.ToString(System.Globalization.CultureInfo.InvariantCulture)} allowInwardCorrectionFromPartsBounds={allowInwardCorrectionFromPartsBounds}");

    public IDisposable Stage(string stage, string details = "")
        => new StageScope(this, stage, details);

    public void Event(string operation, string details = "")
        => Trace(operation, details);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Trace("end", string.Empty);
    }

    private void Trace(string operation, string details)
    {
        PerfTrace.Write(
            "api-dimensions-detail",
            operation,
            _total.ElapsedMilliseconds,
            $"traceId={TraceId} command={_command} {_scopeDetails} {details}".Trim());
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
