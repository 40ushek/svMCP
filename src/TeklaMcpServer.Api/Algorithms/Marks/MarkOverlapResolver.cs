using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public sealed class MarkOverlapResolver
{
    public List<MarkLayoutPlacement> Resolve(
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutOptions options)
    {
        var result = placements
            .Select(p => new MarkLayoutPlacement
            {
                Id = p.Id,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                HasLeaderLine = p.HasLeaderLine,
                CanMove = p.CanMove
            })
            .ToList();

        for (var iteration = 0; iteration < options.MaxResolverIterations; iteration++)
        {
            var movedAny = false;

            for (var i = 0; i < result.Count; i++)
            for (var j = i + 1; j < result.Count; j++)
            {
                var a = result[i];
                var b = result[j];

                var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
                    - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
                var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
                    - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

                if (overlapX <= 0 || overlapY <= 0)
                    continue;

                var moveX = overlapX <= overlapY;
                var push = (moveX ? overlapX : overlapY) + options.Gap;
                var split = GetSplit(a.CanMove, b.CanMove);
                if (split.Total == 0)
                    continue;

                if (moveX)
                {
                    var direction = b.X >= a.X ? 1.0 : -1.0;
                    a.X -= direction * push * split.MoveA;
                    b.X += direction * push * split.MoveB;
                }
                else
                {
                    var direction = b.Y >= a.Y ? 1.0 : -1.0;
                    a.Y -= direction * push * split.MoveA;
                    b.Y += direction * push * split.MoveB;
                }

                movedAny = true;
            }

            if (!movedAny)
                break;
        }

        return result;
    }

    public int CountOverlaps(IReadOnlyList<MarkLayoutPlacement> placements)
    {
        var overlaps = 0;

        for (var i = 0; i < placements.Count; i++)
        for (var j = i + 1; j < placements.Count; j++)
        {
            var a = placements[i];
            var b = placements[j];

            var overlapX = Math.Min(a.X + (a.Width / 2.0), b.X + (b.Width / 2.0))
                - Math.Max(a.X - (a.Width / 2.0), b.X - (b.Width / 2.0));
            var overlapY = Math.Min(a.Y + (a.Height / 2.0), b.Y + (b.Height / 2.0))
                - Math.Max(a.Y - (a.Height / 2.0), b.Y - (b.Height / 2.0));

            if (overlapX > 0 && overlapY > 0)
                overlaps++;
        }

        return overlaps;
    }

    private static (double MoveA, double MoveB, double Total) GetSplit(bool canMoveA, bool canMoveB)
    {
        if (canMoveA && canMoveB)
            return (0.5, 0.5, 1.0);

        if (canMoveA)
            return (1.0, 0.0, 1.0);

        if (canMoveB)
            return (0.0, 1.0, 1.0);

        return (0.0, 0.0, 0.0);
    }
}
