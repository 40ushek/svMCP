using System;
using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionCombineFaultInjectionMode
{
    None = 0,
    AfterCreateBeforeDelete,
    AfterFirstDeleteBeforeCommit
}

internal sealed class DimensionCombineApplyResult
{
    public bool Success { get; set; }
    public int? CreatedDimensionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool RollbackAttempted { get; set; }
    public bool RollbackSucceeded { get; set; }
    public string RollbackReason { get; set; } = string.Empty;
}

internal static class DimensionCombineApplyExecutor
{
    internal const string FaultInjectionEnvironmentVariable = "SVMCP_DIMENSION_COMBINE_FAULT";

    private static DimensionCombineFaultInjectionMode? _testOverrideMode;

    internal static DimensionCombineFaultInjectionMode TestOverrideMode
    {
        get => _testOverrideMode ?? DimensionCombineFaultInjectionMode.None;
        set => _testOverrideMode = value == DimensionCombineFaultInjectionMode.None ? null : value;
    }

    public static DimensionCombineApplyResult Execute(
        Func<int?> createDimension,
        IReadOnlyList<Action> deleteSourceDimensions,
        Action commitCombine,
        Action rollbackDeleteCreatedDimension,
        Action commitRollback)
    {
        var result = new DimensionCombineApplyResult();
        int? createdDimensionId = null;

        try
        {
            createdDimensionId = createDimension();
            if (!createdDimensionId.HasValue)
            {
                result.Reason = "CreateDimensionSet returned null";
                return result;
            }

            ThrowIfInjected(DimensionCombineFaultInjectionMode.AfterCreateBeforeDelete);

            for (var i = 0; i < deleteSourceDimensions.Count; i++)
            {
                deleteSourceDimensions[i]();
                if (i == 0)
                    ThrowIfInjected(DimensionCombineFaultInjectionMode.AfterFirstDeleteBeforeCommit);
            }

            commitCombine();
            result.Success = true;
            result.CreatedDimensionId = createdDimensionId;
            return result;
        }
        catch (Exception ex)
        {
            result.Reason = ex.Message;
            if (!createdDimensionId.HasValue)
                return result;

            result.RollbackAttempted = true;
            try
            {
                rollbackDeleteCreatedDimension();
                commitRollback();
                result.RollbackSucceeded = true;
            }
            catch (Exception rollbackEx)
            {
                result.RollbackReason = rollbackEx.Message;
            }

            return result;
        }
    }

    internal static DimensionCombineFaultInjectionMode ResolveFaultInjectionMode()
    {
        if (_testOverrideMode.HasValue)
            return _testOverrideMode.Value;

        var raw = Environment.GetEnvironmentVariable(FaultInjectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return DimensionCombineFaultInjectionMode.None;

        return raw.Trim().ToLowerInvariant() switch
        {
            "after_create_before_delete" => DimensionCombineFaultInjectionMode.AfterCreateBeforeDelete,
            "after_first_delete_before_commit" => DimensionCombineFaultInjectionMode.AfterFirstDeleteBeforeCommit,
            _ => DimensionCombineFaultInjectionMode.None
        };
    }

    private static void ThrowIfInjected(DimensionCombineFaultInjectionMode mode)
    {
        if (ResolveFaultInjectionMode() != mode)
            return;

        throw new InvalidOperationException($"fault_injection:{ToFaultModeToken(mode)}");
    }

    private static string ToFaultModeToken(DimensionCombineFaultInjectionMode mode)
    {
        return mode switch
        {
            DimensionCombineFaultInjectionMode.AfterCreateBeforeDelete => "after_create_before_delete",
            DimensionCombineFaultInjectionMode.AfterFirstDeleteBeforeCommit => "after_first_delete_before_commit",
            _ => "none"
        };
    }
}
