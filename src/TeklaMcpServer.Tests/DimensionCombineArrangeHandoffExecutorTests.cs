using System;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionCombineArrangeHandoffExecutorTests
{
    [Fact]
    public void Execute_SkipsDuringPreview()
    {
        var invoked = false;

        var result = DimensionCombineArrangeHandoffExecutor.Execute(
            previewOnly: true,
            applyHandoff: () =>
            {
                invoked = true;
                return new DimensionArrangeHandoffResult { Attempted = true, Succeeded = true };
            });

        Assert.False(invoked);
        Assert.False(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Equal("preview_only", result.Reason);
    }

    [Fact]
    public void Execute_ReturnsDelegateResult_WhenHandoffSucceeds()
    {
        var result = DimensionCombineArrangeHandoffExecutor.Execute(
            previewOnly: false,
            applyHandoff: () =>
            {
                var handoff = new DimensionArrangeHandoffResult
                {
                    Attempted = true,
                    Succeeded = true
                };
                handoff.AppliedDimensionIds.Add(42);
                return handoff;
            });

        Assert.True(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal([42], result.AppliedDimensionIds);
    }

    [Fact]
    public void Execute_ReturnsFailure_WhenHandoffThrows()
    {
        var result = DimensionCombineArrangeHandoffExecutor.Execute(
            previewOnly: false,
            applyHandoff: () => throw new InvalidOperationException("handoff_failed"));

        Assert.True(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Equal("handoff_failed", result.Reason);
    }

    [Fact]
    public void Execute_ReportsFaultInjectionAsFailure()
    {
        DimensionCombineArrangeHandoffExecutor.TestOverrideMode = DimensionCombineArrangeHandoffFaultInjectionMode.BeforeApply;

        try
        {
            var result = DimensionCombineArrangeHandoffExecutor.Execute(
                previewOnly: false,
                applyHandoff: () => new DimensionArrangeHandoffResult
                {
                    Attempted = true,
                    Succeeded = true
                });

            Assert.True(result.Attempted);
            Assert.False(result.Succeeded);
            Assert.Equal("fault_injection:before_apply", result.Reason);
        }
        finally
        {
            DimensionCombineArrangeHandoffExecutor.TestOverrideMode = DimensionCombineArrangeHandoffFaultInjectionMode.None;
        }
    }
}
