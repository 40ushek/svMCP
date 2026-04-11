using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Algorithms.Marks;

internal static class LeaderAnchorCandidateScorer
{
    public static LeaderAnchorCandidate? SelectBestCandidate(IReadOnlyList<LeaderAnchorCandidate> candidates)
    {
        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));

        return candidates
            .Where(static candidate => candidate.AnchorPoint != null)
            .OrderBy(static candidate => candidate.LineLengthToLeaderEnd)
            .ThenByDescending(static candidate => candidate.CornerDistance)
            .ThenByDescending(static candidate => candidate.FarEdgeClearance)
            .ThenBy(static candidate => GetKindRank(candidate.Kind))
            .FirstOrDefault();
    }

    private static int GetKindRank(LeaderAnchorCandidateKind kind) => kind switch
    {
        LeaderAnchorCandidateKind.Nearest => 0,
        LeaderAnchorCandidateKind.ShiftedLeft => 1,
        LeaderAnchorCandidateKind.ShiftedRight => 2,
        _ => 99,
    };
}
