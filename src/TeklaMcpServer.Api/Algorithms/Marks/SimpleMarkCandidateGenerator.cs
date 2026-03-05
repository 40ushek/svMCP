using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class SimpleMarkCandidateGenerator : IMarkCandidateGenerator
{
    public IReadOnlyList<MarkCandidate> GenerateCandidates(MarkLayoutItem item, MarkLayoutOptions options)
    {
        var offsetX = (item.Width / 2.0) + options.CandidateOffset + options.Gap;
        var offsetY = (item.Height / 2.0) + options.CandidateOffset + options.Gap;

        return new List<MarkCandidate>
        {
            new() { X = item.CurrentX,                Y = item.CurrentY },
            new() { X = item.AnchorX + offsetX,       Y = item.AnchorY + offsetY },
            new() { X = item.AnchorX + offsetX,       Y = item.AnchorY - offsetY },
            new() { X = item.AnchorX - offsetX,       Y = item.AnchorY + offsetY },
            new() { X = item.AnchorX - offsetX,       Y = item.AnchorY - offsetY },
        };
    }
}
