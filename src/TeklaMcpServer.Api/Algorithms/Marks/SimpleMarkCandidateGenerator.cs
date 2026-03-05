using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class SimpleMarkCandidateGenerator : IMarkCandidateGenerator
{
    public IReadOnlyList<MarkCandidate> GenerateCandidates(MarkLayoutItem item, MarkLayoutOptions options)
    {
        var offsetX = (item.Width / 2.0) + options.CandidateOffset + options.Gap;
        var offsetY = (item.Height / 2.0) + options.CandidateOffset + options.Gap;
        var candidates = item.HasLeaderLine
            ? BuildLeaderCandidates(item, offsetX, offsetY)
            : BuildLocalCandidates(item, offsetX, offsetY);

        return RemoveNearDuplicates(candidates);
    }

    private static List<MarkCandidate> BuildLeaderCandidates(MarkLayoutItem item, double offsetX, double offsetY)
    {
        return new List<MarkCandidate>
        {
            new() { X = item.CurrentX,          Y = item.CurrentY,          Priority = 0 },
            new() { X = item.AnchorX + offsetX, Y = item.AnchorY + offsetY, Priority = 1 },
            new() { X = item.AnchorX + offsetX, Y = item.AnchorY - offsetY, Priority = 2 },
            new() { X = item.AnchorX - offsetX, Y = item.AnchorY + offsetY, Priority = 3 },
            new() { X = item.AnchorX - offsetX, Y = item.AnchorY - offsetY, Priority = 4 },
            new() { X = item.AnchorX + offsetX, Y = item.AnchorY,           Priority = 5 },
            new() { X = item.AnchorX - offsetX, Y = item.AnchorY,           Priority = 6 },
            new() { X = item.AnchorX,           Y = item.AnchorY + offsetY, Priority = 7 },
            new() { X = item.AnchorX,           Y = item.AnchorY - offsetY, Priority = 8 },
        };
    }

    private static List<MarkCandidate> BuildLocalCandidates(MarkLayoutItem item, double offsetX, double offsetY)
    {
        var localOffsetX = Math.Max(offsetX * 0.6, 2.0);
        var localOffsetY = Math.Max(offsetY * 0.6, 2.0);

        return new List<MarkCandidate>
        {
            new() { X = item.CurrentX,               Y = item.CurrentY,               Priority = 0 },
            new() { X = item.CurrentX + localOffsetX, Y = item.CurrentY,              Priority = 1 },
            new() { X = item.CurrentX - localOffsetX, Y = item.CurrentY,              Priority = 2 },
            new() { X = item.CurrentX,               Y = item.CurrentY + localOffsetY, Priority = 3 },
            new() { X = item.CurrentX,               Y = item.CurrentY - localOffsetY, Priority = 4 },
            new() { X = item.CurrentX + localOffsetX, Y = item.CurrentY + localOffsetY, Priority = 5 },
            new() { X = item.CurrentX + localOffsetX, Y = item.CurrentY - localOffsetY, Priority = 6 },
            new() { X = item.CurrentX - localOffsetX, Y = item.CurrentY + localOffsetY, Priority = 7 },
            new() { X = item.CurrentX - localOffsetX, Y = item.CurrentY - localOffsetY, Priority = 8 },
        };
    }

    private static IReadOnlyList<MarkCandidate> RemoveNearDuplicates(IEnumerable<MarkCandidate> candidates)
    {
        const double epsilon = 0.01;
        var result = new List<MarkCandidate>();

        foreach (var candidate in candidates.OrderBy(x => x.Priority))
        {
            if (result.Any(existing =>
                    Math.Abs(existing.X - candidate.X) < epsilon &&
                    Math.Abs(existing.Y - candidate.Y) < epsilon))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }
}
