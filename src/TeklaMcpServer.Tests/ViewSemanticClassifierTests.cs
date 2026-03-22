using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewSemanticClassifierTests
{
    [Theory]
    [InlineData("D", ViewSemanticKind.Section)]
    [InlineData("D1", ViewSemanticKind.Detail)]
    [InlineData("Det 1", ViewSemanticKind.Detail)]
    [InlineData("DetailA", ViewSemanticKind.Detail)]
    [InlineData("a", ViewSemanticKind.Detail)]
    [InlineData("b12", ViewSemanticKind.Detail)]
    [InlineData("1", ViewSemanticKind.Section)]
    public void Classify_UsesNamingRuleForSectionViews(string name, ViewSemanticKind expected)
    {
        var view = new View
        {
            ViewType = View.ViewTypes.SectionView,
            Name = name
        };

        Assert.Equal(expected, ViewSemanticClassifier.Classify(view));
    }

    [Fact]
    public void Classify_KeepsActualDetailViewAsDetail()
    {
        var view = new View
        {
            ViewType = View.ViewTypes.DetailView,
            Name = "A"
        };

        Assert.Equal(ViewSemanticKind.Detail, ViewSemanticClassifier.Classify(view));
    }
}
