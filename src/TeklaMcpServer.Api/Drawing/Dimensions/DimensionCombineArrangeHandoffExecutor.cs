using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionCombineArrangeHandoffFaultInjectionMode
{
    None = 0,
    BeforeApply
}

internal sealed class DimensionArrangeHandoffResult
{
    public bool Attempted { get; set; }
    public bool Succeeded { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<int> AppliedDimensionIds { get; } = [];
}

internal static class DimensionCombineArrangeHandoffExecutor
{
    internal const string FaultInjectionEnvironmentVariable = "SVMCP_DIMENSION_COMBINE_HANDOFF_FAULT";

    private static DimensionCombineArrangeHandoffFaultInjectionMode? _testOverrideMode;

    internal static DimensionCombineArrangeHandoffFaultInjectionMode TestOverrideMode
    {
        get => _testOverrideMode ?? DimensionCombineArrangeHandoffFaultInjectionMode.None;
        set => _testOverrideMode = value == DimensionCombineArrangeHandoffFaultInjectionMode.None ? null : value;
    }

    public static DimensionArrangeHandoffResult Execute(
        bool previewOnly,
        Func<DimensionArrangeHandoffResult> applyHandoff)
    {
        if (previewOnly)
        {
            return new DimensionArrangeHandoffResult
            {
                Attempted = false,
                Succeeded = false,
                Reason = "preview_only"
            };
        }

        try
        {
            ThrowIfInjected(DimensionCombineArrangeHandoffFaultInjectionMode.BeforeApply);
            return applyHandoff();
        }
        catch (Exception ex)
        {
            return new DimensionArrangeHandoffResult
            {
                Attempted = true,
                Succeeded = false,
                Reason = ex.Message
            };
        }
    }

    internal static DimensionCombineArrangeHandoffFaultInjectionMode ResolveFaultInjectionMode()
    {
        if (_testOverrideMode.HasValue)
            return _testOverrideMode.Value;

        var raw = Environment.GetEnvironmentVariable(FaultInjectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return DimensionCombineArrangeHandoffFaultInjectionMode.None;

        return raw.Trim().ToLowerInvariant() switch
        {
            "before_apply" => DimensionCombineArrangeHandoffFaultInjectionMode.BeforeApply,
            _ => DimensionCombineArrangeHandoffFaultInjectionMode.None
        };
    }

    private static void ThrowIfInjected(DimensionCombineArrangeHandoffFaultInjectionMode mode)
    {
        if (ResolveFaultInjectionMode() != mode)
            return;

        throw new InvalidOperationException($"fault_injection:{ToFaultModeToken(mode)}");
    }

    private static string ToFaultModeToken(DimensionCombineArrangeHandoffFaultInjectionMode mode)
    {
        return mode switch
        {
            DimensionCombineArrangeHandoffFaultInjectionMode.BeforeApply => "before_apply",
            _ => "none"
        };
    }
}
