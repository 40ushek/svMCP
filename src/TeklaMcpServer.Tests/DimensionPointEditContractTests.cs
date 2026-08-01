using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Contract tests for the two point-editing commands.
///
/// Only the argument contract is covered here. Everything past validation calls into the Tekla
/// runtime (DrawingHandler, StraightDimensionSetHandler) and cannot run without an open drawing,
/// so the behavioural side — horizontal / vertical / inclined rebuilds, merge success and failure,
/// point counts, failure after the replacement was created — is verified live against a real
/// drawing and recorded in the XML docs on the methods themselves.
/// </summary>
public class DimensionPointEditContractTests
{
    private static readonly double[] TwoValidPoints = new double[] { 0, 0, 0, 1000, 0, 0 };

    public static TheoryData<double[]?> RejectedPointArrays => new()
    {
        { null },                                   // nothing supplied
        { new double[0] },                          // empty
        { new double[] { 0, 0, 0 } },               // one point: a set needs at least two
        { new double[] { 0, 0, 0, 1000, 0 } },      // truncated: not a whole number of triples
        { new double[] { 0, 0, 0, 1000, 0, 0, 2000, 0 } }, // three points, last incomplete
    };

    [Theory]
    [MemberData(nameof(RejectedPointArrays))]
    public void AddDimensionPoints_RejectsMalformedPointArrays(double[]? points)
    {
        var result = new TeklaDrawingDimensionsApi().AddDimensionPoints(42, points!, "horizontal");

        Assert.False(result.Added);
        Assert.Equal(42, result.DimensionId);
        Assert.NotNull(result.Error);
        Assert.Contains("at least 2 points", result.Error);
    }

    [Theory]
    [MemberData(nameof(RejectedPointArrays))]
    public void RecreateDimension_RejectsMalformedPointArrays(double[]? points)
    {
        var result = new TeklaDrawingDimensionsApi().RecreateDimension(42, points!, "vertical");

        Assert.False(result.Recreated);
        Assert.Equal(42, result.OldDimensionId);
        Assert.Equal(0, result.NewDimensionId);
        Assert.NotNull(result.Error);
        Assert.Contains("at least 2 points", result.Error);
    }

    [Fact]
    public void RecreateDimension_ReportsNoNewIdWhenValidationFails()
    {
        // The old id must never be echoed as the new one: callers switch to NewDimensionId after
        // a rebuild, and a non-zero value here would send them to a set that was never created.
        var result = new TeklaDrawingDimensionsApi().RecreateDimension(42, new double[] { 0, 0, 0 }, "vertical");

        Assert.NotEqual(result.OldDimensionId, result.NewDimensionId);
        Assert.Equal(0, result.NewDimensionId);
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    [InlineData("0.6,-0.8,0")]
    public void BothCommands_AcceptEveryDirectionFormBeforeTouchingTekla(string direction)
    {
        // Validation must not depend on the direction: an inclined chain has to reach the Tekla
        // call on the same terms as a horizontal one. With a valid point array both methods get
        // past argument checking and fail only on the missing runtime — asserting that they throw
        // rather than return a validation error is what proves the direction was accepted. The
        // exception type is left open on purpose: outside Tekla the failure surfaces from the
        // remoting transport, not as DrawingNotOpenException.
        var api = new TeklaDrawingDimensionsApi();

        Assert.ThrowsAny<System.Exception>(() => api.AddDimensionPoints(42, TwoValidPoints, direction));
        Assert.ThrowsAny<System.Exception>(() => api.RecreateDimension(42, TwoValidPoints, direction));
    }

    [Fact]
    public void RecreateDimensionResult_DefaultsCarryNoAccidentalSuccess()
    {
        var result = new RecreateDimensionResult();

        Assert.False(result.Recreated);
        Assert.False(result.AttributesKept);
        Assert.Equal(0, result.NewDimensionId);
        Assert.Equal(0d, result.DistanceCorrection);
    }
}
