using System;
using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// One interpretation of what a part is for, in one place. The same judgement was written
/// inline three times in the defect detector and the three did not agree; these tests hold
/// the replacement to the decisions recorded in ROADMAP_PART_ROLES.md.
/// </summary>
public sealed class PartRoleClassifierTests
{
    private static readonly PartRoleClassifier Classifier = new();

    [Theory]
    [InlineData("T", PartRole.Defining)]
    [InlineData("R", PartRole.Attached)]
    [InlineData("M", PartRole.Ignored)]
    public void KnownPrefixesGetTheirRole(string prefix, PartRole expected)
    {
        Assert.Equal(expected, Classifier.ClassifyProperties(prefix).Role);
    }

    [Fact]
    public void PrefixMatchingIgnoresCase()
    {
        Assert.Equal(PartRole.Defining, Classifier.ClassifyProperties("t").Role);
    }

    [Fact]
    public void AnUnfamiliarPrefixIsUnknownRatherThanIgnored()
    {
        // "Takes no part in dimensions" and "no rule matched" are different, and an extent
        // computed over a set containing the second is a guess, not a fact.
        var result = Classifier.ClassifyProperties("B");

        Assert.Equal(PartRole.Unknown, result.Role);
        Assert.Equal("none", result.RuleId);
    }

    [Fact]
    public void NoPrefixAtAllIsAlsoUnknown()
    {
        Assert.Equal(PartRole.Unknown, Classifier.ClassifyProperties(null).Role);
        Assert.Equal(PartRole.Unknown, Classifier.ClassifyProperties("  ").Role);
    }

    [Fact]
    public void MaterialTypeNeverDecidesAnything()
    {
        // The whole reason this class exists. Insulation reports MATERIAL_TYPE 5, the same
        // as timber, which is how it came to inflate the structural extent; consulted even
        // as a last resort it would quietly become the deciding signal again on every
        // unfamiliar prefix.
        var insulation = Classifier.ClassifyProperties("R", materialType: 5);
        var unfamiliarTimber = Classifier.ClassifyProperties("Z", materialType: 5);

        Assert.Equal(PartRole.Attached, insulation.Role);
        Assert.Equal(PartRole.Unknown, unfamiliarTimber.Role);
    }

    [Fact]
    public void WhatWasKnownIsReportedSoUnknownCanBeActedOn()
    {
        // Without this, Unknown is visible but useless: no prefix, an unfamiliar prefix and
        // a prefix no rule covers yet want three different fixes.
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
    public void TheRuleThatMatchedIsNamed()
    {
        Assert.Equal("prefix-T", Classifier.ClassifyProperties("T").RuleId);
    }

    [Fact]
    public void TwoRulesOnOnePrefixAreRefusedRatherThanOrdered()
    {
        // Matching is on the prefix alone, so the second could never fire whatever the
        // order. Accepting it and calling the outcome "first match wins" would hide dead
        // configuration behind a rule about precedence.
        var rules = new[]
        {
            new PartRoleRule("first", "X", PartRole.Defining),
            new PartRoleRule("second", "x", PartRole.Ignored),
        };

        var thrown = Assert.Throws<ArgumentException>(() => new PartRoleClassifier(rules));

        Assert.Contains("first", thrown.Message);
        Assert.Contains("second", thrown.Message);
    }

    [Fact]
    public void ItClassifiesAPartInViewFromItsOwnProperties()
    {
        var part = new PartInView { ModelId = 7, PartPos = "R-68", PartPrefix = "R", MaterialType = 5 };

        Assert.Equal(PartRole.Attached, Classifier.Classify(part).Role);
    }

    [Fact]
    public void RulesGivenToItCannotBeChangedUnderIt()
    {
        // A classifier whose table can be rewritten from outside after it is built would
        // answer differently at different times for reasons no caller can see.
        var rules = new List<PartRoleRule> { new("only", "X", PartRole.Defining) };
        var classifier = new PartRoleClassifier(rules);

        rules.Clear();
        rules.Add(new PartRoleRule("swapped", "X", PartRole.Ignored));

        Assert.Equal(PartRole.Defining, classifier.ClassifyProperties("X").Role);
        Assert.Equal("only", classifier.ClassifyProperties("X").RuleId);
    }

    [Fact]
    public void ARuleWithoutAnIdOrAPrefixIsRefused()
    {
        // An id is what a report names when the rule matches; a rule with no prefix would
        // match nothing and quietly do so.
        Assert.Throws<ArgumentException>(() => new PartRoleClassifier([new PartRoleRule("", "X", PartRole.Defining)]));
        Assert.Throws<ArgumentException>(() => new PartRoleClassifier([new PartRoleRule("no-prefix", " ", PartRole.Defining)]));
    }

    [Fact]
    public void ReadingAPartWithNoPrefixIsAnAnswer()
    {
        // The live reader looked and there was nothing there. That is Unknown, and it is
        // classified: a rule is what is missing, not the reading.
        var result = Classifier.ClassifyProperties(null, profile: "60X200", material: "C24");

        Assert.Equal(PartRole.Unknown, result.Role);
        Assert.True(result.IsClassified);
    }

    [Fact]
    public void ASnapshotWithNoPropertiesIsNotSomethingToReclassify()
    {
        // On a snapshot an absent prefix usually means nobody read it, so re-reading it
        // would turn "never read" into "no rule covers it".
        var geometryOnly = new PartInView { ModelId = 1, BboxMin = [0, 0, 0], BboxMax = [1, 1, 1] };
        var read = new PartInView { ModelId = 2, PartPrefix = "T" };

        Assert.False(Classifier.CanReclassifyFromSnapshot(geometryOnly));
        Assert.True(Classifier.CanReclassifyFromSnapshot(read));

        // And left alone, it stays the thing that says nobody looked.
        Assert.False(geometryOnly.Role.IsClassified);
        Assert.Equal(PartRole.Unknown, geometryOnly.Role.Role);
    }

    [Fact]
    public void TheDefaultsAreThePrefixesThisPlantUses()
    {
        Assert.Equal(
            ["prefix-T", "prefix-GLB", "prefix-R", "prefix-S", "prefix-M"],
            PartRoleClassifier.DefaultRules.Select(rule => rule.Id));
    }

    [Fact]
    public void AWindowMarkStaysUnknownUntilTheWholeModelHasBeenLookedAt()
    {
        // Observed: W-65 and W-68 on EW.8, W-64 on EW.18 - three ContourPlates with material
        // WINDOW. A prefix rule built on that would speak for every W part in every future
        // assembly, and Ignored is the answer that quietly removes a part from the extent.
        // Unknown is the honest answer: it makes isComplete false and sends the caller to
        // settle the role rather than inheriting a guess.
        Assert.Equal(PartRole.Unknown, Classifier.ClassifyProperties("W").Role);
    }

    [Fact]
    public void AGlulamBeamCarriesTheFrameAndSetsItsExtent()
    {
        // Measured: a dimension point on thirteen drawings had no candidate under it
        // because the GL24h beam beneath it was unclassified.
        Assert.Equal(PartRole.Defining, Classifier.ClassifyProperties("GLB").Role);
    }

    [Fact]
    public void SheathingIsAttachedRatherThanDefining()
    {
        // Calling it Defining would make its overhang part of the overall, which is the
        // difference between an extent starting at 210 and one starting at 200. That a
        // person dimensions the sheet is a fact about another semantic group.
        Assert.Equal(PartRole.Attached, Classifier.ClassifyProperties("S").Role);
    }

    [Fact]
    public void ALongerPrefixIsNotSwallowedByAShorterOne()
    {
        // GLB and G would collide if matching were by first letter. It is not - but the
        // day a G rule is added, this test says which behaviour was intended.
        Assert.Equal("prefix-GLB", Classifier.ClassifyProperties("GLB").RuleId);
        Assert.Equal(PartRole.Unknown, Classifier.ClassifyProperties("G").Role);
    }
}
