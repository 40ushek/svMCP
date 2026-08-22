using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// One interpretation of whether a part takes part in the structural geometry, in one
/// place. It ships with no rules: prefixes and material names are each plant's own
/// convention in its own language, and the prefix table that used to live here answered
/// one timber model while turning every part of a steel column into a blocker.
/// </summary>
public sealed class PartRoleClassifierTests
{
    private static readonly PartRoleClassifier Classifier = new();

    [Fact]
    public void WithNoExclusionsEveryPartTakesPart()
    {
        // The whole point of the redesign. "T" is a timber frame member, "P" is every part
        // of a steel column, "W" is a window - none of them means anything here.
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties("T").Role);
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties("P").Role);
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties("W").Role);
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties(null).Role);
    }

    [Fact]
    public void TheDefaultIsAnEmptyExclusionSet()
    {
        Assert.Empty(new PartRoleClassifier().Exclusions);
        Assert.Empty(PartRoleClassifier.NoExclusions);
    }

    [Fact]
    public void APrefixExclusionTakesOutExactlyThatPrefix()
    {
        var classifier = new PartRoleClassifier([PartExclusionRule.ByPrefix("R")]);

        Assert.Equal(PartRole.Excluded, classifier.ClassifyProperties("R").Role);
        Assert.Equal(PartRole.Included, classifier.ClassifyProperties("RS").Role);
        Assert.Equal(PartRole.Included, classifier.ClassifyProperties("T").Role);
    }

    [Fact]
    public void PrefixMatchingIgnoresCase()
    {
        var classifier = new PartRoleClassifier([PartExclusionRule.ByPrefix("R")]);

        Assert.Equal(PartRole.Excluded, classifier.ClassifyProperties("r").Role);
    }

    [Fact]
    public void AMaterialExclusionMatchesAsASubstring()
    {
        // Material names are written by the plant, in its language, with grades and
        // suffixes attached: "Mineralwolle 040", "C24-KVH". Whole-string matching would
        // make the caller guess the exact spelling of every variant.
        var classifier = new PartRoleClassifier([PartExclusionRule.ByMaterial("wolle")]);

        Assert.Equal(PartRole.Excluded, classifier.ClassifyProperties("R", material: "Mineralwolle 040").Role);
        Assert.Equal(PartRole.Included, classifier.ClassifyProperties("T", material: "C24").Role);
    }

    [Fact]
    public void TheExclusionThatMatchedIsNamed()
    {
        var classifier = new PartRoleClassifier([PartExclusionRule.ByMaterial("WINDOW")]);

        Assert.Equal("exclude-material:WINDOW", classifier.ClassifyProperties("W", material: "WINDOW").RuleId);
        Assert.Equal("included", classifier.ClassifyProperties("T").RuleId);
    }

    [Fact]
    public void MaterialTypeNeverDecidesAnything()
    {
        // The whole reason this class exists. Insulation reports MATERIAL_TYPE 5, the same
        // as timber, which is how it came to inflate the structural extent; consulted even
        // as a last resort it would quietly become the deciding signal again.
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties("R", materialType: 5).Role);
        Assert.Equal(PartRole.Included, Classifier.ClassifyProperties("Z", materialType: 5).Role);
    }

    [Fact]
    public void WhatWasKnownIsReportedSoAnExclusionCanBeWritten()
    {
        // Without this, a filter is guesswork: the person choosing what to exclude needs to
        // see the prefix, the material and the name the parts actually carry.
        var result = Classifier.ClassifyProperties("Z", profile: "45*145", material: "C24", materialType: 5, name: "STUD");

        Assert.Contains("prefix=Z", result.Reason);
        Assert.Contains("profile=45*145", result.Reason);
        Assert.Contains("material=C24", result.Reason);
        Assert.Contains("materialType=5", result.Reason);
        Assert.Contains("name=STUD", result.Reason);
    }

    [Fact]
    public void AMissingPrefixSaysSoRatherThanBeingBlank()
    {
        Assert.Contains("prefix=<none>", Classifier.ClassifyProperties(null).Reason);
    }

    [Fact]
    public void ItClassifiesAPartInViewFromItsOwnProperties()
    {
        var part = new PartInView { ModelId = 7, PartPos = "R-68", PartPrefix = "R", MaterialType = 5 };
        var classifier = new PartRoleClassifier([PartExclusionRule.ByPrefix("R")]);

        Assert.Equal(PartRole.Excluded, classifier.Classify(part).Role);
        Assert.Equal(PartRole.Included, Classifier.Classify(part).Role);
    }

    [Fact]
    public void ExclusionsGivenToItCannotBeChangedUnderIt()
    {
        // A classifier whose table can be rewritten from outside after it is built would
        // answer differently at different times for reasons no caller can see.
        var rules = new List<PartExclusionRule> { PartExclusionRule.ByPrefix("X") };
        var classifier = new PartRoleClassifier(rules);

        rules.Clear();
        rules.Add(PartExclusionRule.ByPrefix("T"));

        Assert.Equal(PartRole.Excluded, classifier.ClassifyProperties("X").Role);
        Assert.Equal(PartRole.Included, classifier.ClassifyProperties("T").Role);
    }

    [Fact]
    public void AnExclusionWithNoValueIsRefused()
    {
        // It would match nothing and quietly do so, which reads on a report exactly like a
        // filter that was applied and found nothing to remove.
        Assert.Throws<ArgumentException>(() =>
            new PartRoleClassifier([new PartExclusionRule("blank", PartExclusionKind.Prefix, " ")]));
    }

    [Fact]
    public void OnlyThePropertiesAnExclusionReadsAreNeeded()
    {
        // The reader consults these before deciding that a failed property read matters.
        // Requiring PART_PREFIX with no prefix exclusion is what blocked a steel column
        // over a property nobody asked about; not requiring MATERIAL with a material
        // exclusion is worse - the window stays in the extent and the answer still calls
        // itself complete.
        Assert.False(new PartRoleClassifier().NeedsPrefix);
        Assert.False(new PartRoleClassifier().NeedsMaterial);

        var byPrefix = new PartRoleClassifier([PartExclusionRule.ByPrefix("R")]);
        Assert.True(byPrefix.NeedsPrefix);
        Assert.False(byPrefix.NeedsMaterial);

        var byMaterial = new PartRoleClassifier([PartExclusionRule.ByMaterial("WINDOW")]);
        Assert.False(byMaterial.NeedsPrefix);
        Assert.True(byMaterial.NeedsMaterial);
    }

    [Fact]
    public void AMaterialOnlyFilterDoesNotDemandAPrefix()
    {
        // A steel model has no useful prefixes at all; demanding one to run a material
        // filter would refuse the drawing over a property the filter never reads.
        var byMaterial = new PartRoleClassifier([PartExclusionRule.ByMaterial("WINDOW")]);
        var noPrefix = new PartInView { ModelId = 1, Material = "S235JR" };

        Assert.True(byMaterial.CanReclassifyFromSnapshot(noPrefix));
        Assert.False(byMaterial.CanReclassifyFromSnapshot(new PartInView { ModelId = 2, PartPrefix = "P" }));
    }

    [Fact]
    public void ASnapshotIsSafeToClassifyWhenNoExclusionNeedsAPropertyItLacks()
    {
        // With no exclusions there is nothing to read, so a geometry-only copy classifies
        // fine: the answer is Included and it rests on no property at all.
        var geometryOnly = new PartInView { ModelId = 1, BboxMin = [0, 0, 0], BboxMax = [1, 1, 1] };

        Assert.True(Classifier.CanReclassifyFromSnapshot(geometryOnly));

        // With a prefix exclusion it is not: an absent prefix on a snapshot usually means
        // nobody read it, and including a part because its prefix was never read is the
        // silent version of the mistake this area exists to prevent.
        var byPrefix = new PartRoleClassifier([PartExclusionRule.ByPrefix("R")]);

        Assert.False(byPrefix.CanReclassifyFromSnapshot(geometryOnly));
        Assert.True(byPrefix.CanReclassifyFromSnapshot(new PartInView { ModelId = 2, PartPrefix = "T" }));
    }

    [Fact]
    public void UntouchedPartsCarryTheAnswerThatNobodyLooked()
    {
        var geometryOnly = new PartInView { ModelId = 1 };

        Assert.False(geometryOnly.Role.IsClassified);
        Assert.Equal(PartRole.Included, geometryOnly.Role.Role);
    }

    [Fact]
    public void TwoListsBecomeTwoKindsOfExclusion()
    {
        var rules = PartExclusions.Parse(" R , M ", "WINDOW");

        Assert.Equal(
            ["exclude-prefix:R", "exclude-prefix:M", "exclude-material:WINDOW"],
            rules.Select(rule => rule.Id));
    }

    [Fact]
    public void EmptyListsExcludeNothing()
    {
        Assert.Empty(PartExclusions.Parse(null, null));
        Assert.Empty(PartExclusions.Parse("", "   "));
        Assert.Empty(PartExclusions.Parse(",  ,", ""));
    }
}
