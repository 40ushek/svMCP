using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionWriteProtocolTests
{
    [Fact]
    public void ReplacementIsCommittedAndVerifiedBeforeOriginalDeletion()
    {
        var calls = new List<string>();
        var result = DimensionWriteProtocol.Execute(
            () => { calls.Add("create"); return 42; }, () => { calls.Add("commit"); return true; },
            id => { Assert.Equal(42, id); calls.Add("verify"); return null; },
            () => { calls.Add("delete"); return true; },
            () => { calls.Add("absent"); return true; });
        Assert.Equal(new[] { "create", "commit", "verify", "delete", "commit", "absent", "verify" }, calls);
        Assert.True(result.Completed);
        Assert.True(result.OriginalDeletionVerified);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("verify")]
    public void FailureBeforeVerificationPreservesOriginalAndReportsReplacementId(string failure)
    {
        var deleted = false;
        var result = DimensionWriteProtocol.Execute(() => 42,
            () => { if (failure == "commit") throw new InvalidOperationException("commit failed"); return true; },
            _ => "wrong points", () => { deleted = true; return true; }, () => true);
        Assert.False(deleted);
        Assert.False(result.Completed);
        Assert.False(result.Verified);
        Assert.Equal(42, result.NewDimensionId);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void DeleteFalseIsNotSuccessOrRollback()
    {
        var result = DimensionWriteProtocol.Execute(() => 42, () => true, _ => null, () => false, () => false);
        Assert.False(result.Completed);
        Assert.True(result.Verified);
        Assert.True(result.OriginalDeleteAttempted);
        Assert.False(result.OriginalDeleteAccepted);
        Assert.False(result.OriginalDeletionVerified);
        Assert.Equal("deletingOriginal", result.Stage);
    }

    [Fact]
    public void DeletionCommitFailureRetainsHonestUncertainState()
    {
        var commits = 0;
        var result = DimensionWriteProtocol.Execute(() => 42,
            () => { if (++commits == 2) throw new Exception("lost connection"); return true; },
            _ => null, () => true, () => true);
        Assert.True(result.OriginalDeleteAccepted);
        Assert.False(result.OriginalDeletionVerified);
        Assert.False(result.Completed);
        Assert.Equal("committingDeletion", result.Stage);
    }

    [Fact]
    public void PostDeletionReflowFailureDoesNotClaimVerifiedSuccess()
    {
        var reads = 0;
        var result = DimensionWriteProtocol.Execute(() => 42, () => true,
            _ => ++reads == 1 ? null : "reflow changed offset", () => true, () => true);
        Assert.True(result.OriginalDeletionVerified);
        Assert.False(result.Verified);
        Assert.False(result.Completed);
    }

    [Fact]
    public void FailedCreateNeverCommitsOrDeletes()
    {
        var result = DimensionWriteProtocol.Execute(() => 0,
            () => throw new Exception("must not commit"), _ => throw new Exception("must not verify"),
            () => throw new Exception("must not delete"));
        Assert.Equal(0, result.NewDimensionId);
        Assert.False(result.OriginalDeleteAttempted);
        Assert.Contains("usable ID", result.Error);
    }

    [Fact]
    public void ReadbackMatchesPointsOneToOneWithoutInferringStartFromTheirOrder()
    {
        double[] expected = [0, 10, 0, 100, 20, 0];
        Assert.Null(DimensionWriteVerification.CheckPoints(expected, [(100, 20), (0, 10)], 1));
        Assert.NotNull(DimensionWriteVerification.CheckPoints(expected, [(0, 10), (100, 10)], 1));
        Assert.NotNull(DimensionWriteVerification.CheckPoints(expected, [(0, 10), (100, 20)], 2));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void RejectsUnsafeOffsetBeforeRuntime(double distance)
        => Assert.NotNull(DimensionWriteProtocol.Validate([0, 0, 0, 100, 0, 0], distance));

    [Fact]
    public void RejectsNonfiniteAndCoincidentPoints()
    {
        Assert.NotNull(DimensionWriteProtocol.Validate([0, 0, 0, double.NaN, 0, 0], 20));
        Assert.NotNull(DimensionWriteProtocol.Validate([0, 0, 0, 0, 0, 100], 20));
    }

    [Theory]
    [InlineData("0,0,0")]
    [InlineData("NaN,1,0")]
    [InlineData("Infinity,1,0")]
    public void RejectsInvalidDirection(string direction)
        => Assert.Null(DimensionCreatePlacementHelper.TryResolveDirection(direction));

    [Fact]
    public void DatumUsesConnectivityNotReturnedSegmentOrder()
    {
        Assert.Null(DimensionWriteVerification.CheckDatum(0, 0,
            [((10, 0), (20, 0)), ((0, 0), (10, 0))]));
        Assert.NotNull(DimensionWriteVerification.CheckDatum(20, 0,
            [((10, 0), (20, 0)), ((0, 0), (10, 0))]));
        Assert.NotNull(DimensionWriteVerification.CheckDatum(0, 0,
            [((0, 0), (10, 0)), ((0, 0), (20, 0))]));
        Assert.NotNull(DimensionWriteVerification.CheckDatum(0, 0,
            [((0, 0), (10, 0)), ((20, 0), (30, 0))]));
    }

    [Fact]
    public void FreshCreateFailingVerificationRemovesTheNewSetAndConfirmsIt()
    {
        var calls = new List<string>();
        var result = DimensionWriteProtocol.Execute(() => 42,
            () => { calls.Add("commit"); return true; }, _ => "wrong points", null, null,
            id => { calls.Add("deleteNew:" + id); return true; },
            id => { calls.Add("absent:" + id); return true; });
        Assert.Equal(new[] { "commit", "deleteNew:42", "commit", "absent:42" }, calls);
        Assert.True(result.NewDimensionRemoved);
        Assert.Null(result.CleanupError);
        Assert.False(result.Completed);
        Assert.Contains("the new dimension was removed", result.ErrorDetail);
    }

    [Fact]
    public void ReplacementFailingBeforeOriginalDeletionIsRemovedAndOriginalStays()
    {
        var originalDeleted = false;
        var result = DimensionWriteProtocol.Execute(() => 42, () => true, _ => "wrong style",
            () => { originalDeleted = true; return true; }, () => true,
            _ => true, _ => true);
        Assert.False(originalDeleted);
        Assert.True(result.NewDimensionRemoved);
        Assert.False(result.OriginalDeleteAttempted);
    }

    [Theory]
    [InlineData("deleteFalse")]
    [InlineData("commitFails")]
    [InlineData("stillPresent")]
    public void FailedCleanupIsReportedAndNeverClaimedAsRemoval(string failure)
    {
        var commits = 0;
        var result = DimensionWriteProtocol.Execute(() => 42,
            () => failure == "commitFails" && ++commits == 2 ? throw new Exception("lost connection") : true,
            _ => "wrong points", null, null,
            _ => failure != "deleteFalse", _ => failure != "stillPresent");
        Assert.False(result.NewDimensionRemoved);
        Assert.NotNull(result.CleanupError);
        Assert.Contains("may still be on the sheet", result.ErrorDetail);
        Assert.Equal(42, result.NewDimensionId);
    }

    [Fact]
    public void NothingIsUndoneOnceTheOriginalDeleteWasAttempted()
    {
        var deletedNew = false;
        var result = DimensionWriteProtocol.Execute(() => 42, () => true, _ => null,
            () => false, () => false, _ => { deletedNew = true; return true; }, _ => true);
        Assert.True(result.OriginalDeleteAttempted);
        Assert.False(deletedNew);
        Assert.False(result.NewDimensionRemoved);
    }

    [Fact]
    public void FailedCreateHasNothingToRemove()
    {
        var result = DimensionWriteProtocol.Execute(() => 0, () => true, _ => null, null, null,
            _ => throw new Exception("must not delete"), _ => true);
        Assert.False(result.NewDimensionRemoved);
        Assert.Null(result.CleanupError);
    }

    [Fact]
    public void CommitFalseStopsBeforeVerificationOrDeletion()
    {
        var result = DimensionWriteProtocol.Execute(() => 42, () => false,
            _ => throw new Exception("must not verify"), () => throw new Exception("must not delete"));
        Assert.False(result.OriginalDeleteAttempted);
        Assert.False(result.Completed);
        Assert.Equal(42, result.NewDimensionId);
        Assert.Contains("CommitChanges() returned false", result.Error);
    }
}
