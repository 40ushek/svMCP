using System.IO;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The retired detector used to hold this as a unit test. It is now a placement-policy
/// boundary: a contact may justify a selected coordinate, but cannot require one.
/// Keep the rule in the tracked reference consumed by the dimensioning skill.
/// </summary>
public sealed class DimensioningSkillPolicyTests
{
    [Fact]
    public void AContactWithoutASelectedPositionDoesNotDemandADimension()
    {
        var rulesPath = Path.Combine(
            BridgeTestHelpers.FindRepoRoot(),
            ".agents", "skills", "dimension-drawings", "references", "plant-rules.md");

        var rules = File.ReadAllText(rulesPath);

        Assert.Contains("A contact that has no selected chain position is ordinary", rules);
        Assert.Contains("do not create either a required coordinate or a defect", rules);
    }
}
