using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DimensionCombineActionCandidate
{
    public int? ViewId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string DimensionType { get; set; } = string.Empty;
    public int PacketIndex { get; set; }
    public int BaseDimensionId { get; set; }
    public string ConnectivityMode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool CanCombine { get; set; }
    public DimensionCombinePreviewDebugInfo? Preview { get; set; }
    public List<int> DimensionIds { get; } = [];
    public List<string> BlockingReasons { get; } = [];
}

internal static class DimensionCombineActionPlanner
{
    public static List<DimensionCombineActionCandidate> BuildCandidates(
        DimensionReductionDebugResult reductionDebug,
        IReadOnlyCollection<int>? targetDimensionIds = null)
    {
        var targetIds = targetDimensionIds == null || targetDimensionIds.Count == 0
            ? null
            : new HashSet<int>(targetDimensionIds);
        var result = new List<DimensionCombineActionCandidate>();

        foreach (var group in reductionDebug.Groups)
        {
            foreach (var packet in group.Packets.OrderBy(static packet => packet.PacketIndex))
            {
                var candidate = new DimensionCombineActionCandidate
                {
                    ViewId = group.RawGroup.ViewId,
                    ViewType = group.RawGroup.ViewType,
                    DimensionType = group.RawGroup.DimensionType,
                    PacketIndex = packet.PacketIndex,
                    BaseDimensionId = packet.CombinePreview?.BaseDimensionId ?? packet.RepresentativeDimensionId,
                    ConnectivityMode = packet.CombineConnectivityMode,
                    Preview = packet.CombinePreview
                };

                candidate.DimensionIds.AddRange(packet.DimensionIds);
                candidate.BlockingReasons.AddRange(packet.BlockingReasons);

                if (candidate.DimensionIds.Count <= 1)
                {
                    candidate.Reason = "single_dimension_packet";
                    result.Add(candidate);
                    continue;
                }

                if (targetIds != null && candidate.DimensionIds.Any(id => !targetIds.Contains(id)))
                {
                    candidate.Reason = "target_filter_mismatch";
                    result.Add(candidate);
                    continue;
                }

                if (!packet.IsCombineCandidate)
                {
                    candidate.Reason = candidate.BlockingReasons.FirstOrDefault() ?? "not_combine_candidate";
                    result.Add(candidate);
                    continue;
                }

                if (candidate.Preview == null)
                {
                    candidate.Reason = "combine_preview_unavailable";
                    result.Add(candidate);
                    continue;
                }

                if (candidate.Preview.PointList.Count < 2)
                {
                    candidate.Reason = "combine_preview_has_too_few_points";
                    result.Add(candidate);
                    continue;
                }

                candidate.CanCombine = true;
                candidate.Reason = string.Empty;
                result.Add(candidate);
            }
        }

        return result;
    }
}
