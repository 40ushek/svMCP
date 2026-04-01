using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
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
    internal void Classify_UsesNamingRuleForSectionViews(string name, ViewSemanticKind expected)
    {
        var view = ViewTestHelper.Create(View.ViewTypes.SectionView, name: name);

        Assert.Equal(expected, ViewSemanticClassifier.Classify(view));
    }

    [Fact]
    public void Classify_KeepsActualDetailViewAsDetail()
    {
        var view = ViewTestHelper.Create(View.ViewTypes.DetailView, name: "A");

        Assert.Equal(ViewSemanticKind.Detail, ViewSemanticClassifier.Classify(view));
    }
}
