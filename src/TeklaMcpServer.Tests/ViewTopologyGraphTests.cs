using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class ViewTopologyGraphTests
{
    [Fact]
    public void Build_UsesNeighborResolverForStandardProjectedViews()
    {
        var front = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40, originX: 100, originY: 100);
        var top = ViewTestHelper.Create(View.ViewTypes.TopView, width: 50, height: 30, originX: 100, originY: 170);
        var section = ViewTestHelper.Create(View.ViewTypes.SectionView, width: 40, height: 20, originX: 170, originY: 100);
        TrySetIdentifier(front, 1);
        TrySetIdentifier(top, 2);
        TrySetIdentifier(section, 3);

        var graph = ViewTopologyGraph.Build(new[] { front, top, section });

        Assert.Equal(front, graph.BaseView);
        Assert.NotNull(graph.Neighbors);
        Assert.Equal(top, graph.Neighbors!.TopNeighbor);
        Assert.Equal(ProjectionMethod.NeighborAxis, graph.GetProjectionMethod(top));
        Assert.Equal(ProjectionMethod.SectionSide, graph.GetProjectionMethod(section));
    }

    [Fact]
    public void Build_ClassifiesDetailLikeSectionOutsideMainSectionSkeleton()
    {
        var front = ViewTestHelper.Create(View.ViewTypes.FrontView, width: 60, height: 40, originX: 100, originY: 100);
        var detailLikeSection = ViewTestHelper.Create(View.ViewTypes.SectionView, name: "Detail A", width: 20, height: 10, originX: 180, originY: 150);
        TrySetIdentifier(front, 1);
        TrySetIdentifier(detailLikeSection, 2);

        var graph = ViewTopologyGraph.Build(new[] { front, detailLikeSection });

        Assert.Contains(detailLikeSection, graph.SemanticViews.Details);
        Assert.DoesNotContain(detailLikeSection, graph.SemanticViews.Sections);
        Assert.Equal(ProjectionMethod.None, graph.GetProjectionMethod(detailLikeSection));
    }

    [Theory]
    [InlineData(ViewSemanticKind.Detail, NeighborRole.Unknown, true, ProjectionMethod.DetailAnchor)]
    [InlineData(ViewSemanticKind.Section, NeighborRole.Unknown, false, ProjectionMethod.SectionSide)]
    [InlineData(ViewSemanticKind.BaseProjected, NeighborRole.Top, false, ProjectionMethod.NeighborAxis)]
    [InlineData(ViewSemanticKind.Other, NeighborRole.Unknown, false, ProjectionMethod.None)]
    internal void ResolveProjectionMethod_UsesSemanticAndDependencyPriority(
        ViewSemanticKind semanticKind,
        NeighborRole neighborRole,
        bool hasDetailRelation,
        ProjectionMethod expected)
    {
        Assert.Equal(expected, ViewTopologyGraph.ResolveProjectionMethod(semanticKind, neighborRole, hasDetailRelation));
    }

    private static void TrySetIdentifier(View view, int id)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var identifier = view.GetIdentifier();
        if (identifier == null)
        {
            var type = view.GetType();
            while (type != null)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (!field.FieldType.Name.Contains("Identifier")) continue;
                    try
                    {
#pragma warning disable SYSLIB0050
                        var created = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(field.FieldType);
#pragma warning restore SYSLIB0050
                        field.SetValue(view, created);
                    }
                    catch { }
                }
                type = type.BaseType;
            }
            identifier = view.GetIdentifier();
        }

        if (identifier == null) return;

        var idProperty = identifier.GetType().GetProperty("ID", flags);
        var setter = idProperty?.GetSetMethod(nonPublic: true);
        if (setter != null)
        {
            setter.Invoke(identifier, new object[] { id });
            return;
        }

        foreach (var fieldName in new[] { "<ID>k__BackingField", "m_ID", "m_id", "_id" })
        {
            var field = identifier.GetType().GetField(fieldName, flags);
            if (field != null)
            {
                field.SetValue(identifier, id);
                return;
            }
        }
    }
}
