using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal abstract class DimensionRuleDetails
{
    internal abstract IReadOnlyDictionary<string, object> ToEvidence();
}

internal sealed class SteelLocationDetails(string[][] roles, int[][] partIds, int[] skippedPartIds, int[] offeredOnOtherSidePartIds, string[] droppedShortPointIds) : DimensionRuleDetails
{
    public string[][] Roles { get; } = roles;
    public int[][] PartIds { get; } = partIds;
    public int[] SkippedPartIds { get; } = skippedPartIds;
    public int[] OfferedOnOtherSidePartIds { get; } = offeredOnOtherSidePartIds;
    public string[] DroppedShortPointIds { get; } = droppedShortPointIds;
    internal override IReadOnlyDictionary<string, object> ToEvidence() => new Dictionary<string, object> {
        ["roles"] = Roles,
        ["partIds"] = PartIds,
        ["skippedPartIds"] = SkippedPartIds,
        ["offeredOnOtherSidePartIds"] = OfferedOnOtherSidePartIds,
        ["droppedShortPointIds"] = DroppedShortPointIds,
    };
}

internal sealed class SectionLocationDetails(int[] locatedByProfilePartIds, int[] offeredOnOtherSidePartIds, int[] mergedNearbyPartIds, int[] locatedPartIds) : DimensionRuleDetails
{
    public int[] LocatedByProfilePartIds { get; } = locatedByProfilePartIds;
    public int[] OfferedOnOtherSidePartIds { get; } = offeredOnOtherSidePartIds;
    public int[] MergedNearbyPartIds { get; } = mergedNearbyPartIds;
    public int[] LocatedPartIds { get; } = locatedPartIds;
    internal override IReadOnlyDictionary<string, object> ToEvidence() => new Dictionary<string, object> {
        ["locatedByProfilePartIds"] = LocatedByProfilePartIds,
        ["offeredOnOtherSidePartIds"] = OfferedOnOtherSidePartIds,
        ["mergedNearbyPartIds"] = MergedNearbyPartIds,
        ["locatedPartIds"] = LocatedPartIds,
    };
}

internal sealed class PanelLocationDetails(int[][] pointPartIds, string[][] candidateKinds, string[] pointRoles, int[] droppedShortPartIds, double minimumSegmentViewUnits, int[] missingSupportModelIds, bool incomplete) : DimensionRuleDetails
{
    public int[][] PointPartIds { get; } = pointPartIds;
    public string[][] CandidateKinds { get; } = candidateKinds;
    public string[] PointRoles { get; } = pointRoles;
    public int[] DroppedShortPartIds { get; } = droppedShortPartIds;
    public double MinimumSegmentViewUnits { get; } = minimumSegmentViewUnits;
    public int[] MissingSupportModelIds { get; } = missingSupportModelIds;
    public bool Incomplete { get; } = incomplete;
    internal override IReadOnlyDictionary<string, object> ToEvidence() => new Dictionary<string, object> {
        ["pointPartIds"] = PointPartIds,
        ["candidateKinds"] = CandidateKinds,
        ["pointRoles"] = PointRoles,
        ["droppedShortPartIds"] = DroppedShortPartIds,
        ["minimumSegmentViewUnits"] = MinimumSegmentViewUnits,
        ["missingSupportModelIds"] = MissingSupportModelIds,
        ["incomplete"] = Incomplete,
    };
}

internal sealed class InclinedPartDetails(int partId, int[] attachedPartIds, double tiltDegrees, double acrossSpan) : DimensionRuleDetails
{
    public int PartId { get; } = partId;
    public int[] AttachedPartIds { get; } = attachedPartIds;
    public double TiltDegrees { get; } = tiltDegrees;
    public double AcrossSpan { get; } = acrossSpan;
    internal override IReadOnlyDictionary<string, object> ToEvidence() => new Dictionary<string, object> {
        ["partId"] = PartId,
        ["attachedPartIds"] = AttachedPartIds,
        ["tiltDegrees"] = TiltDegrees,
        ["acrossSpan"] = AcrossSpan,
    };
}

internal sealed class LocalPartDetails(int partId, int[] partIds, double baseOffset) : DimensionRuleDetails
{
    public int PartId { get; } = partId;
    public int[] PartIds { get; } = partIds;
    public double BaseOffset { get; } = baseOffset;
    internal override IReadOnlyDictionary<string, object> ToEvidence() => new Dictionary<string, object> {
        ["partId"] = PartId,
        ["partIds"] = PartIds,
        ["baseOffset"] = BaseOffset,
    };
}
