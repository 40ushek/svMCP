using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionMeasurementPurpose { Unknown, Overall, Location, Internal, Edge, Check }
internal enum DimensionMeasurementReference { Unknown, Assembly, MainPart, NamedPart, Filter, CurrentPart, None }
internal enum DimensionReferenceSupport { Unspecified, BoundingBox, NearestEdge, Midpoint }

/// <summary>Snapshot-local addressing and measurement semantics, independent of display side/key.</summary>
internal sealed class DimensionProposalIdentity
{
    public string ContextId { get; }
    public int ViewId { get; }
    public string ProposalId { get; }
    public string? PreviewKey { get; }
    public DimensionMeasurementPurpose Purpose { get; }
    public DimensionMeasurementReference Reference { get; }
    public DimensionReferenceSupport ReferenceSupport { get; }

    public DimensionProposalIdentity(string contextId, int viewId, string proposalId,
        DimensionMeasurementPurpose purpose, DimensionMeasurementReference reference,
        DimensionReferenceSupport referenceSupport, string? previewKey)
    {
        if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("A context ID is required.", nameof(contextId));
        if (string.IsNullOrWhiteSpace(proposalId)) throw new ArgumentException("A proposal ID is required.", nameof(proposalId));
        if (viewId <= 0) throw new ArgumentOutOfRangeException(nameof(viewId), "A positive view ID is required.");
        if (previewKey != null && string.IsNullOrWhiteSpace(previewKey))
            throw new ArgumentException("A preview key must be non-empty when supplied.", nameof(previewKey));
        if (!Enum.IsDefined(typeof(DimensionMeasurementPurpose), purpose)
            || !Enum.IsDefined(typeof(DimensionMeasurementReference), reference)
            || !Enum.IsDefined(typeof(DimensionReferenceSupport), referenceSupport))
            throw new ArgumentException("Proposal semantics must use defined vocabulary values.");
        ContextId = contextId;
        ViewId = viewId;
        ProposalId = proposalId;
        PreviewKey = previewKey;
        Purpose = purpose;
        Reference = reference;
        ReferenceSupport = referenceSupport;
    }

    public static DimensionMeasurementPurpose PurposeFromKind(string kind) => kind switch {
        "overall" => DimensionMeasurementPurpose.Overall,
        "location" => DimensionMeasurementPurpose.Location,
        "internal" => DimensionMeasurementPurpose.Internal,
        "edge" => DimensionMeasurementPurpose.Edge,
        "check" => DimensionMeasurementPurpose.Check,
        _ => DimensionMeasurementPurpose.Unknown
    };

    /// <summary>
    /// Content identity within a frozen snapshot, never a global geometry ID.
    /// Point/datum order matters; source evidence order and preview display keys do not.
    /// </summary>
    public static DimensionProposalIdentity Create(string contextId, int viewId, string ruleFamily,
        string? previewKey, DimensionRuleResult result,
        DimensionMeasurementReference reference = DimensionMeasurementReference.Unknown,
        DimensionReferenceSupport referenceSupport = DimensionReferenceSupport.Unspecified)
    {
        if (string.IsNullOrWhiteSpace(ruleFamily)) throw new ArgumentException("A rule family is required.", nameof(ruleFamily));
        var purpose = PurposeFromKind(result.Kind);
        var content = JsonSerializer.Serialize(new {
            ruleFamily, kind = result.Kind, purpose = purpose.ToString(), reference = reference.ToString(),
            referenceSupport = referenceSupport.ToString(),
            direction = new { x = result.Direction.X, y = result.Direction.Y },
            placement = PlacementContent(result.Placement),
            points = result.Points.Select(p => new {
                id = p.Id, x = p.X, y = p.Y,
                sources = p.Sources.Select(s => JsonSerializer.Serialize(new {
                    objectKind = s.ObjectKind, modelId = s.ModelId, geometryId = s.GeometryId,
                    pointIndex = s.PointIndex, featureKind = s.FeatureKind, isHole = s.IsHole,
                    extentAlongChain = s.ExtentAlongChain
                })).OrderBy(s => s, StringComparer.Ordinal).ToArray()
            }).ToArray(),
            // Empty refusals still have distinct content. Nonempty chains use their supports.
            emptyRefusal = result.Points.Count == 0 ? result.Note : null
        });
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        var id = ruleFamily + ":" + BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
        return new(contextId, viewId, id, purpose, reference, referenceSupport, previewKey);
    }

    private static object PlacementContent(DimensionLinePlacement placement) =>
        placement is OutsideOutlineDimensionPlacement outside
            ? new { policy = "outside-outline", x = outside.OutwardNormal.X, y = outside.OutwardNormal.Y }
            : (object)new { policy = "unsupported", type = placement.GetType().FullName,
                evidence = JsonSerializer.SerializeToElement(placement, placement.GetType()) };
}
