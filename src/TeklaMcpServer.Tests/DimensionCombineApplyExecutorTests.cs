using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionCombineApplyExecutorTests
{
    [Fact]
    public void Execute_Succeeds_WhenNoFaultIsInjected()
    {
        var deletedSources = new List<string>();
        var rollbackSteps = new List<string>();
        DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.None;

        try
        {
            var result = DimensionCombineApplyExecutor.Execute(
                createDimension: () => 9001,
                deleteSourceDimensions:
                [
                    () => deletedSources.Add("left"),
                    () => deletedSources.Add("right")
                ],
                commitCombine: () => deletedSources.Add("commit"),
                rollbackDeleteCreatedDimension: () => rollbackSteps.Add("rollback-delete"),
                commitRollback: () => rollbackSteps.Add("rollback-commit"));

            Assert.True(result.Success);
            Assert.Equal(9001, result.CreatedDimensionId);
            Assert.False(result.RollbackAttempted);
            Assert.False(result.RollbackSucceeded);
            Assert.Equal(["left", "right", "commit"], deletedSources);
            Assert.Empty(rollbackSteps);
        }
        finally
        {
            DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.None;
        }
    }

    [Fact]
    public void Execute_RollsBack_WhenFaultInjectedAfterCreateBeforeDelete()
    {
        var deletedSources = new List<string>();
        var rollbackSteps = new List<string>();
        DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.AfterCreateBeforeDelete;

        try
        {
            var result = DimensionCombineApplyExecutor.Execute(
                createDimension: () => 9001,
                deleteSourceDimensions:
                [
                    () => deletedSources.Add("left"),
                    () => deletedSources.Add("right")
                ],
                commitCombine: () => deletedSources.Add("commit"),
                rollbackDeleteCreatedDimension: () => rollbackSteps.Add("rollback-delete"),
                commitRollback: () => rollbackSteps.Add("rollback-commit"));

            Assert.False(result.Success);
            Assert.Equal("fault_injection:after_create_before_delete", result.Reason);
            Assert.True(result.RollbackAttempted);
            Assert.True(result.RollbackSucceeded);
            Assert.Equal(string.Empty, result.RollbackReason);
            Assert.Empty(deletedSources);
            Assert.Equal(["rollback-delete", "rollback-commit"], rollbackSteps);
        }
        finally
        {
            DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.None;
        }
    }

    [Fact]
    public void Execute_RollsBack_WhenFaultInjectedAfterFirstDeleteBeforeCommit()
    {
        var deletedSources = new List<string>();
        var rollbackSteps = new List<string>();
        DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.AfterFirstDeleteBeforeCommit;

        try
        {
            var result = DimensionCombineApplyExecutor.Execute(
                createDimension: () => 9001,
                deleteSourceDimensions:
                [
                    () => deletedSources.Add("left"),
                    () => deletedSources.Add("right")
                ],
                commitCombine: () => deletedSources.Add("commit"),
                rollbackDeleteCreatedDimension: () => rollbackSteps.Add("rollback-delete"),
                commitRollback: () => rollbackSteps.Add("rollback-commit"));

            Assert.False(result.Success);
            Assert.Equal("fault_injection:after_first_delete_before_commit", result.Reason);
            Assert.True(result.RollbackAttempted);
            Assert.True(result.RollbackSucceeded);
            Assert.Equal(["left"], deletedSources);
            Assert.Equal(["rollback-delete", "rollback-commit"], rollbackSteps);
        }
        finally
        {
            DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.None;
        }
    }

    [Fact]
    public void Execute_ReportsRollbackFailure_WhenRollbackThrows()
    {
        DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.AfterCreateBeforeDelete;

        try
        {
            var result = DimensionCombineApplyExecutor.Execute(
                createDimension: () => 9001,
                deleteSourceDimensions: Array.Empty<Action>(),
                commitCombine: static () => { },
                rollbackDeleteCreatedDimension: () => throw new InvalidOperationException("rollback_delete_failed"),
                commitRollback: static () => { });

            Assert.False(result.Success);
            Assert.Equal("fault_injection:after_create_before_delete", result.Reason);
            Assert.True(result.RollbackAttempted);
            Assert.False(result.RollbackSucceeded);
            Assert.Equal("rollback_delete_failed", result.RollbackReason);
        }
        finally
        {
            DimensionCombineApplyExecutor.TestOverrideMode = DimensionCombineFaultInjectionMode.None;
        }
    }
}
