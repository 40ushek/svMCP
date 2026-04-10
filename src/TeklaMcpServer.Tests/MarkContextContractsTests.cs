using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkContextContractsTests
{
    [Fact]
    public void Default_Constructors_InitializeCollections()
    {
        var viewContext = new MarksViewContext();
        var markContext = new MarkContext();
        var geometry = new MarkGeometryContext();
        var buildResult = new MarkContextBuildResult();

        Assert.NotNull(viewContext.Marks);
        Assert.NotNull(viewContext.Warnings);
        Assert.NotNull(markContext.Properties);
        Assert.NotNull(geometry.Corners);
        Assert.NotNull(buildResult.Contexts);
        Assert.NotNull(buildResult.Warnings);
    }

    [Fact]
    public void MarksViewContext_IsEmpty_DependsOnlyOnMarks()
    {
        var context = new MarksViewContext();
        context.Warnings.Add("warn");

        Assert.True(context.IsEmpty);

        context.Marks.Add(new MarkContext { MarkId = 1 });

        Assert.False(context.IsEmpty);
    }

    [Fact]
    public void MarkContextBuildResult_FindByMarkId_ReturnsMatchingContext()
    {
        var result = new MarkContextBuildResult();
        var target = new MarkContext { MarkId = 42, HasLeaderLine = true, CanMove = false };
        result.Contexts.Add(new MarkContext { MarkId = 10 });
        result.Contexts.Add(target);

        var found = result.FindByMarkId(42);

        Assert.Same(target, found);
        Assert.True(found!.HasLeaderLine);
        Assert.False(found.CanMove);
    }

    [Fact]
    public void MarkContextBuildResult_FindByMarkId_ReturnsNull_WhenMissing()
    {
        var result = new MarkContextBuildResult();
        result.Contexts.Add(new MarkContext { MarkId = 10 });

        var found = result.FindByMarkId(999);

        Assert.Null(found);
    }

    [Fact]
    public void MarkContext_Flags_AreSimpleRuntimeFields()
    {
        var context = new MarkContext
        {
            MarkId = 7,
            HasLeaderLine = true,
            CanMove = true,
        };

        Assert.True(context.HasLeaderLine);
        Assert.True(context.CanMove);

        context.HasLeaderLine = false;
        context.CanMove = false;

        Assert.False(context.HasLeaderLine);
        Assert.False(context.CanMove);
    }
}
