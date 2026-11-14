using System.Globalization;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>The public Bolt-row shape. Selection and placement never read this representation.</summary>
internal static class BoltChainPreviewProjection
{
    internal static object Project(BoltChainDecision decision, double distance)
    {
        var candidate = decision.Candidate;
        return new {
            key = candidate.Key, direction = string.Join(",", new[] {
                candidate.OutwardNormal.X, candidate.OutwardNormal.Y, 0d }
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            attributesFile = "standard", kind = BoltChainRule.Kind,
            chainKind = candidate.Kind, boltGroupId = candidate.GroupId,
            partId = candidate.PartId ?? 0, viewClass = candidate.ViewClass.ToString(),
            segments = candidate.Segments, distance,
            points = candidate.Points.Select(point => new[] { point.X, point.Y }).ToArray(),
            sources = Sources(candidate.Sources), holes = Holes(candidate.Holes),
            incomplete = !decision.Create, incompleteReason = decision.Reason,
            remark = candidate.Remark is { Length: > 0 } remark ? remark : null
        };
    }

    // Compatibility with the generic dimension-rule diagnostics; the bolt pipeline uses typed decisions.
    internal static DimensionRuleResult RuleResult(BoltChainDecision decision)
    {
        var candidate = decision.Candidate;
        var reason = decision.PolicyRefusal;
        return new(new DimensionDirection(candidate.Axis == 0 ? 1 : 0, candidate.Axis == 0 ? 0 : 1),
            new OutsideOutlineDimensionPlacement(candidate.OutwardNormal, 1), BoltChainRule.Kind, candidate.Points,
            note: reason, segments: candidate.Segments, evidence: new Dictionary<string, object> {
                ["key"] = candidate.Key, ["chainKind"] = candidate.Kind, ["create"] = reason == null,
                ["groupId"] = candidate.GroupId, ["partId"] = candidate.PartId ?? 0,
                ["viewClass"] = candidate.ViewClass.ToString(), ["baseOffset"] = candidate.BaseOffset,
                ["sources"] = Sources(candidate.Sources), ["holes"] = Holes(candidate.Holes),
                ["overMainPartChain"] = candidate.OverMainPartChain ?? "",
                ["groupIds"] = candidate.GroupIds.ToArray(),
                ["remark"] = candidate.Remark ?? "",
                ["requiresChain"] = candidate.RequiresChain ?? ""
            });
    }

    internal static object Sources(BoltChainSources sources) => sources switch {
        BoltChainSources.CapturedParts captured => JsonSerializer.SerializeToElement(captured.Positions.Select(Position).ToArray()),
        BoltChainSources.ChainPoints points => points.Positions.Select(point => new { point }).ToArray(),
        BoltChainSources.SpacingIndices spacing => spacing.Indices.ToArray(),
        _ => throw new NotSupportedException("Unknown bolt source representation")
    };

    internal static object[] Holes(IEnumerable<BoltHoleGeometry> holes) => holes.Select(hole => (object)new {
        boltGroupId = hole.GroupId, partId = hole.PartId, boltStandard = hole.Standard, boltSize = hole.BoltSize,
        holeDiameter = hole.HoleDiameter, holeOnly = hole.HoleOnly, specialHole = hole.SpecialHole,
        holeType = hole.HoleType, slottedHoleX = hole.SlottedHoleX, slottedHoleY = hole.SlottedHoleY
    }).ToArray();

    internal static object Position(BoltProjectedPosition position) => new {
        point = position.Point,
        sources = position.Sources.Select(source => new { boltGroupId = source.GroupId, index = source.Index }).ToArray()
    };

    internal static object Part(BoltPartChainGeometry chain) => new {
        proposalId = chain.Key, kind = "part", partId = chain.PartId,
        axis = chain.Axis == 0 ? "X" : "Y", placementSide = chain.Side.ToString(),
        state = chain.Reason == null ? "Candidate" : "Blocked", reason = chain.Reason,
        creationMode = "explicitSelectionOnly", blockedSources = chain.BlockedSources
            .Select(source => new { boltGroupId = source.GroupId, index = source.Index, reason = source.Reason }).ToArray(),
        points = chain.Points, segments = chain.Segments,
        minEnd = chain.MinEnd, maxEnd = chain.MaxEnd, minEndKind = chain.MinEndKind,
        maxEndKind = chain.MaxEndKind, boltRefusal = chain.BoltRefusal,
        projectedPositions = chain.ProjectedPositions.Select(Position).ToArray(),
        contourSource = "captured full-solid projected outer contour; section clipping and visibility are unverified"
    };

    internal static object Group(BoltGroupProjection group) => new {
        boltGroupId = group.Geometry.ModelId, relatedPartIds = group.RelatedPartIds, partCandidates = group.PartCandidates,
        partScopeStatus = group.PartCandidates.Length == 1 ? "single included related part; role/policy still requires review"
            : "unresolved: choose the dimensioned part explicitly",
        shape = group.Geometry.Shape, viewClass = group.ViewClass.ToString(),
        status = group.Reason != null || (group.Chains.Length > 0 && group.Chains.All(chain => !chain.Candidate))
            ? "Blocked" : group.Chains.Length == 0 ? "NoInternalSpacing" : "Candidate",
        candidateChainCount = group.Chains.Count(chain => chain.Candidate), blockedChainCount = group.Chains.Count(chain => !chain.Candidate),
        reason = group.Reason, stepReason = group.StepReason, points = group.Points.Select(Center).ToArray(),
        chains = group.Chains.Select(Spacing).ToArray(), edgeChains = group.EdgeChains.Select(Edge).ToArray(),
        edgeScopeStatus = group.PartCandidates.Length == 0 ? "Blocked: no included related part"
            : "separate proposals per related part; choose part and edge policy before creation"
    };

    private static object Center(BoltProjectedCenter center) => center.RepresentativeIndex.HasValue
        ? (object)new { index = center.Index, point = center.Point, state = center.State, reason = center.Reason,
            representativeIndex = center.RepresentativeIndex.Value }
        : new { index = center.Index, state = center.State, reason = center.Reason, point = center.Point };

    private static object Spacing(BoltSpacingChain chain) => new {
        proposalId = chain.Key, kind = "internal", axis = chain.Axis == 0 ? "X" : "Y", direction = chain.Axis == 0 ? "horizontal" : "vertical",
        state = chain.Candidate ? "Candidate" : "Blocked", creationMode = "explicitSelectionOnly", reason = chain.Reason,
        blockedIndices = chain.BlockedIndices, startIndex = chain.StartIndex,
        points = chain.Points.Select(point => new { point = point.Point, sourceIndices = point.SourceIndices }).ToArray(), segments = chain.Segments
    };

    internal static object Edge(BoltEdgeDistanceGeometry edge) => new {
        proposalId = edge.Key, kind = "edge", boltGroupId = edge.GroupId, partId = edge.PartId,
        axis = edge.Axis == 0 ? "X" : "Y", direction = edge.Axis == 0 ? "horizontal" : "vertical",
        edgeSide = edge.Sign < 0 ? "min" : "max", state = edge.Reason == null ? "Candidate" : "Blocked", reason = edge.Reason,
        creationMode = "explicitSelectionOnly", sourceIndices = edge.SourceIndices,
        center = edge.Center, edgePoint = edge.EdgePoint, distance = edge.Distance, contourIndex = edge.ContourIndex,
        points = edge.EdgePoint == null ? Array.Empty<double[]>()
            : edge.Sign < 0 ? new[] { edge.EdgePoint, edge.Center } : new[] { edge.Center, edge.EdgePoint },
        contourSource = "captured full-solid projected outer contour; section clipping is unverified",
        policyStatus = "part-specific proposal; edge-dimension policy and placement are unverified"
    };
}
