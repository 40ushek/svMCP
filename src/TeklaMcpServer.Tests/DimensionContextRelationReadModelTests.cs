using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DimensionContextRelationReadModelTests
{
    [Fact]
    public void MapperNestsSourcesUnderTheirOwningSegment()
    {
        var item = new DimensionItem
        {
            DimensionId = 100,
            DomainDimensionType = DimensionType.Horizontal
        };
        item.SegmentIds.AddRange([200, 201]);
        item.Segments.Add(new DimensionSegmentInfo { Id = 200, StartX = 1, EndX = 2 });
        item.Segments.Add(new DimensionSegmentInfo { Id = 201, StartX = 2, EndX = 3 });

        var context = new DimensionContext
        {
            DimensionId = 100,
            Item = item,
            Role = DimensionContextRole.Internal,
            SourceKind = DimensionSourceKind.Part
        };
        context.Association.RelatedSources.Add(new DimensionContextRelatedSource
        {
            Owner = "segment:200",
            ModelId = 11,
            Type = "ModelObject"
        });
        context.Association.RelatedSources.Add(new DimensionContextRelatedSource
        {
            Owner = "segment:201",
            ModelId = 22,
            Type = "ModelObject"
        });

        var result = DimensionContextReadModelMapper.ToResult(7, [context], []);

        Assert.Equal(2, result.Dimensions[0].SegmentContexts.Count);
        Assert.Equal(11, result.Dimensions[0].SegmentContexts[0].RelatedSources[0].ModelId);
        Assert.Equal(22, result.Dimensions[0].SegmentContexts[1].RelatedSources[0].ModelId);
    }
}
