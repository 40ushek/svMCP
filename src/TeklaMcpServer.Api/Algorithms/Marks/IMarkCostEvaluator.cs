using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public interface IMarkCostEvaluator
{
    double EvaluateCandidate(
        MarkLayoutItem item,
        MarkCandidate candidate,
        IReadOnlyList<MarkLayoutPlacement> placements,
        MarkLayoutOptions options);
}
