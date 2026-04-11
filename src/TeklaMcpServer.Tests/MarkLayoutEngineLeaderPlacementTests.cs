using System.Collections.Generic;
using System.Linq;
using TeklaMcpServer.Api.Algorithms.Marks;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class MarkLayoutEngineLeaderPlacementTests
{
    [Fact]
    public void Arrange_ForHorizontalPartLeaderMark_ChoosesSourceSideBodyCandidate()
    {
        var engine = new MarkLayoutEngine();
        var item = new MarkLayoutItem
        {
            Id = 1,
            CurrentX = 200,
            CurrentY = 100,
            AnchorX = 50,
            AnchorY = 10,
            Width = 20,
            Height = 10,
            HasLeaderLine = true,
            CanMove = true,
            SourceKind = MarkLayoutSourceKind.Part,
            SourceModelId = 42
        };

        var result = engine.Arrange(
            new[] { item },
            new MarkLayoutOptions
            {
                Gap = 2,
                CandidateOffset = 4,
                CurrentPositionWeight = 0,
                AnchorDistanceWeight = 0,
                CandidatePriorityWeight = 0,
                CrowdingPenaltyWeight = 0,
                PreferredSidePenaltyWeight = 0,
                LeaderLengthWeight = 1.0,
                OverlapPenalty = 0,
                EnableOverlapResolver = false,
                PartPolygonsByModelId = new Dictionary<int, List<double[]>>
                {
                    [42] =
                    [
                        [0.0, 0.0],
                        [100.0, 0.0],
                        [100.0, 20.0],
                        [0.0, 20.0]
                    ]
                }
            });

        var placement = Assert.Single(result.Placements);
        Assert.Equal(50, placement.X, 6);
        Assert.Equal(31, placement.Y, 6);
    }
}
