using System.Collections.Generic;

namespace TeklaMcpServer.Api.Algorithms.Marks;

public interface IMarkCandidateGenerator
{
    IReadOnlyList<MarkCandidate> GenerateCandidates(MarkLayoutItem item, MarkLayoutOptions options);
}
