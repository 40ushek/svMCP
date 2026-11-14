using System;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionMoveProtocol
{
    internal static MoveDimensionResult Execute(int id, double previous, double delta,
        Func<double, bool> modify, Action commit, Func<double?> readDistance)
    {
        var result = new MoveDimensionResult { DimensionId = id, NewDistance = previous };
        var expected = previous + delta;
        if (double.IsNaN(expected) || double.IsInfinity(expected))
            throw new ArgumentException("Requested dimension distance must be finite", nameof(delta));
        if (!modify(expected))
        {
            result.Reason = "Modify returned false; movement was not confirmed";
            return result;
        }
        commit();
        var actual = readDistance();
        if (!actual.HasValue || double.IsNaN(actual.Value) || double.IsInfinity(actual.Value))
        {
            result.Reason = "Dimension was not available on read-back; movement is unverified";
            return result;
        }
        result.NewDistance = actual.Value;
        result.Moved = Math.Abs(actual.Value - expected) <= 0.01;
        result.Reason = result.Moved ? string.Empty : "Read-back distance differs from the requested distance";
        return result;
    }
}
