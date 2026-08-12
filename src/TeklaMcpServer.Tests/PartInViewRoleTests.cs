using System;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// How a part's role survives the journey from being read to reaching a consumer. Each of
/// these is a contract something downstream relies on and none of them is obvious from the
/// code that carries it.
/// </summary>
public sealed class PartInViewRoleTests
{
    [Fact]
    public void APartNobodyClassifiedSaysSoRatherThanLookingUnknown()
    {
        // Both carry Unknown, and the difference decides what to do about it: a part no
        // rule covered wants a rule, a part nobody looked at wants reading properly.
        var part = new PartInView { ModelId = 1 };

        Assert.False(part.Role.IsClassified);
        Assert.Equal(PartRole.Unknown, part.Role.Role);
        Assert.Equal("unclassified", part.Role.RuleId);
    }

    [Fact]
    public void APartTheClassifierLookedAtIsMarkedAsSuch()
    {
        var result = new PartRoleClassifier().ClassifyProperties("Z");

        Assert.True(result.IsClassified);
        Assert.Equal(PartRole.Unknown, result.Role);
    }

    [Fact]
    public void TheRoleCannotBeSetToNull()
    {
        // The bridge reaches straight through to Role.Role. A default initializer only
        // covers the parts nobody assigns to.
        var part = new PartInView();

        Assert.Throws<ArgumentNullException>(() => part.Role = null!);
    }

    [Fact]
    public void CloneCarriesTheRole()
    {
        var part = new PartInView
        {
            ModelId = 5,
            PartPrefix = "T",
            Role = new PartRoleClassifier().ClassifyProperties("T")
        };

        var clone = part.Clone();

        Assert.Equal(PartRole.Defining, clone.Role.Role);
        Assert.Equal("prefix-T", clone.Role.RuleId);
    }

    [Fact]
    public void CloneGeometryOnlyDoesNotCarryTheRole()
    {
        // It drops the properties the role is derived from, so carrying a role decided
        // from them would be asserting something this copy cannot support.
        var part = new PartInView
        {
            ModelId = 5,
            PartPrefix = "T",
            Role = new PartRoleClassifier().ClassifyProperties("T")
        };

        var clone = part.CloneGeometryOnly();

        Assert.False(clone.Role.IsClassified);
        Assert.Equal(PartRole.Unknown, clone.Role.Role);
    }

    [Fact]
    public void TheViewContextMapperCarriesTheRole()
    {
        var context = new DrawingViewContext { ViewId = 3, ViewType = "FrontView" };
        context.Parts.Add(new PartInView
        {
            ModelId = 5,
            PartPrefix = "R",
            BboxMin = [0, 0, 0],
            BboxMax = [1, 1, 1],
            Role = new PartRoleClassifier().ClassifyProperties("R")
        });

        var result = DrawingViewContextMapper.ToResult(context);

        var part = Assert.Single(result.Parts);
        Assert.Equal(PartRole.Attached, part.Role.Role);
        Assert.Equal("prefix-R", part.Role.RuleId);
    }
}
