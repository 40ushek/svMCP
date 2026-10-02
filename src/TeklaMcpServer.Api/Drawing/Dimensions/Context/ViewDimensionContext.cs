using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeklaMcpServer.Api.Drawing.Dimensions;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Captured source geometry. Mutable working chains never escape this boundary.</summary>
public sealed class ViewDimensionContext
{
    private readonly GeometryGroup _group;
    private readonly bool _complete;
    private readonly JsonElement _parts;
    private readonly JsonElement _diagnostics;
    private readonly JsonElement _source;
    private readonly JsonElement _metadata;
    private readonly string[] _exclusions;
    private readonly IReadOnlyDictionary<int, PartSolidGeometryInViewResult> _partSolids;
    private readonly int[] _contactModelIds;
    private readonly IReadOnlyList<UnreadPart> _contactSelectionUnread;
    private ViewContactsSnapshot? _contacts;
    private DimensionPointCatalog? _dimensionPointCatalog;
    private readonly Dictionary<string, JsonElement> _boltPreviews = new(StringComparer.Ordinal);
    private readonly int[] _mainPartIds;
    private readonly int[] _mainPartUnresolvedIds;
    private readonly int[] _includedModelIds;
    private readonly JsonElement _bolts;
    private readonly JsonElement _partContours;
    private string? _fingerprint;
    public int ViewId { get; }
    public double Scale { get; }
    public string ContextId { get; } = "ctx_" + Guid.NewGuid().ToString("N");
    internal bool IsComplete => _complete;

    /// <summary>Returns an isolated copy of a captured part solid, or null when it was not read.</summary>
    public PartSolidGeometryInViewResult? GetPartSolidGeometry(int modelId) =>
        _partSolids.TryGetValue(modelId, out var geometry) ? Clone(geometry) : null;

    // Internal geometry consumers can reuse the stored DTO without cloning it.
    // Treat it as immutable; public callers use the defensive-copy method above.
    internal PartSolidGeometryInViewResult? GetPartSolidGeometrySnapshot(int modelId) =>
        _partSolids.TryGetValue(modelId, out var geometry) ? geometry : null;

    internal ViewDimensionContext(int viewId, double scale, StructuralOutline outline,
        IReadOnlyList<PartExclusionRule> exclusions, object source, object metadata,
        ViewBoltGeometrySnapshot? bolts = null)
    {
        ViewId = viewId;
        _partContours = Freeze(outline.Outline.PartNodes);
        Scale = scale;
        _bolts = bolts?.Answer ?? Freeze(new { isComplete = false, selectionComplete = false,
            visibilityVerified = false, error = "Bolt geometry was not captured" });
        _group = StructuralGeometryGroupBuilder.Build(outline);
        _complete = outline.IsComplete && _group.Completeness.IsComplete;
        _partSolids = new ReadOnlyDictionary<int, PartSolidGeometryInViewResult>(
            outline.Outline.PartSolidGeometries.ToDictionary(pair => pair.Key, pair => pair.Value));
        _mainPartIds = outline.Included.Where(p => p.IsMainPart).Select(p => p.ModelId).ToArray();
        _includedModelIds = outline.Included.Select(p => p.ModelId).Distinct().ToArray();
        // A part that was never classified could be the main part: count it as unresolved.
        _mainPartUnresolvedIds = outline.Included.Concat(outline.Excluded)
            .Where(p => !p.IsMainPartKnown).Concat(outline.Unclassified).Select(p => p.ModelId).Distinct().ToArray();
        _contactModelIds = outline.ContactModelIds.Distinct().ToArray();
        _contactSelectionUnread = outline.ContactSelectionUnread;
        _source = Freeze(source);
        _metadata = Freeze(metadata);
        _exclusions = exclusions.Select(x => x.Id).ToArray();
        _parts = Freeze(outline.Included.Concat(outline.Excluded).Concat(outline.Unclassified)
            .GroupBy(p => p.ModelId).Select(g => g.First())
            .Select(p => new { modelId = p.ModelId, partPos = p.PartPos, partPrefix = p.PartPrefix,
                profile = p.Profile, material = p.Material,
                role = p.Role.Role.ToString(), classified = p.Role.IsClassified, ruleId = p.Role.RuleId, reason = p.Role.Reason,
                isMainPart = p.IsMainPart, mainPartKnown = p.IsMainPartKnown }).ToArray());
        _diagnostics = Freeze(new {
            mainPartModelIds = outline.Included.Where(p => p.IsMainPart).Select(p => p.ModelId).ToArray(),
            mainPartUnresolvedModelIds = _mainPartUnresolvedIds,
            excludedModelIds = outline.Excluded.Select(p => p.ModelId).ToArray(),
            outsideDepthModelIds = outline.OutsideDepthModelIds,
            unresolvedDepthModelIds = outline.Outline.UnresolvedDepthModelIds,
            issues = _group.Completeness.Issues.Select(i => new { id = i.Id, reason = i.Reason }).ToArray()
        });
    }

    public DimensionPlacementCalculation Calculate(string direction, double[] points, double? paperGapMm = null)
    {
        if (!_complete || _group.Extent == null)
            throw new InvalidOperationException("Cannot calculate automatic dimension offset: assembly outline is incomplete or empty");
        return DimensionPlacementCalculator.Calculate(ParseSide(direction), points, _group.Extent,
            Scale, paperGapMm ?? DimensionPlacementSettings.DefaultPaperGapMm);
    }

    public JsonElement ChainPositions(bool verbose = false)
    {
        var result = Header();
        try { EnsureChains(); }
        catch (InvalidOperationException ex)
        {
            result["success"] = false;
            result["error"] = _group.Completeness.Issues.FirstOrDefault(i => i.Id == "structural-outline")?.Reason ?? ex.Message;
            result["calculationError"] = ex.Message;
            return Freeze(result, display: true);
        }
        result["partSpanMatchToleranceMm"] = CalcDimensionChains.PartSpanMatchToleranceMm;
        result["sourceFingerprint"] = Fingerprint();
        result["format"] = verbose ? "verbose" : "compact";
        result["sides"] = _group.DimensionChains!.Chains.Select(chain => new {
            side = chain.Side.ToString(),
            positions = verbose ? (object)VerbosePositions(chain) : CompactChainPositions.Project(chain, s => Span(chain.Side, s))
        }).ToArray();
        return Freeze(result, display: true);
    }

    /// <summary>One or several small questions; answers never round or mutate stored points.</summary>
    public JsonElement Query(string questions = "points,edges,scale", string sides = "all",
        double[]? points = null, string direction = "horizontal", double? paperGapMm = null,
        string ruleSet = "steel", string contactPair = "")
    {
        var requested = Split(questions);
        var normalizedRuleSet = (ruleSet ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRuleSet.Length == 0) normalizedRuleSet = "steel";
        if (normalizedRuleSet is not "steel" and not "panel")
            throw new ArgumentException("ruleSet must be 'steel' or 'panel'");
        var allowed = new[] { "points", "dimensionpoints", "chain", "chaindetails", "edges", "parts", "bolts", "boltchains", "scale", "placement", "contacts", "contactdetails", "diagnostics", "all" };
        if (requested.Length == 0 || requested.Any(q => !allowed.Contains(q)))
            throw new ArgumentException("questions must contain points, dimensionPoints, chain, chainDetails, edges, parts, bolts, boltChains, scale, placement, contacts, contactDetails, diagnostics or all");
        var selected = ParseSides(sides);
        bool Wants(string q) => requested.Contains(q) ||
            (requested.Contains("all") && q is not "contacts" and not "bolts" and not "boltchains" and not "dimensionpoints" and not "chain" and not "chaindetails" and not "diagnostics");
        var wantsChain = requested.Contains("chain") || requested.Contains("chaindetails");
        var result = Header(shortAnswer: true, compact: !requested.Contains("diagnostics"));
        if (requested.Contains("contacts") || requested.Contains("contactdetails"))
        {
            if (_contacts == null)
                throw new InvalidOperationException("Contact snapshot is not attached to this view context");
            var pair = ParseContactPair(contactPair);
            result["contacts"] = ContactAnswer(_contacts.Get(), requested.Contains("contactdetails") || pair != null, pair);
        }
        if (requested.Contains("placement"))
            result["placement"] = Calculate(direction, points ?? throw new ArgumentException("placement requires points"), paperGapMm);
        if (Wants("scale")) result["scale"] = Scale;
        if (Wants("parts")) result["parts"] = _parts;
        if (Wants("bolts")) result["bolts"] = _bolts;
        if (requested.Contains("boltchains"))
            result["boltChainPreview"] = BuildBoltPreview(selected);
        if (Wants("points") || requested.Contains("dimensionpoints") || wantsChain)
        {
            try { EnsureChains(); }
            catch (InvalidOperationException ex)
            {
                result["success"] = false;
                result["error"] = _group.Completeness.Issues.FirstOrDefault(i => i.Id == "structural-outline")?.Reason ?? ex.Message;
                result["calculationError"] = ex.Message;
                return Freeze(result, display: true);
            }
        }
        if (requested.Contains("dimensionpoints"))
            result["dimensionPoints"] = GetDimensionPointCatalog().Project(selected);
        if (wantsChain)
        {
            var panel = normalizedRuleSet == "panel";
            var refusal = panel ? PanelPreviewRefusal() : ChainPreviewRefusal();
            var detailed = requested.Contains("chaindetails");
            var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
            var section = viewType is "SectionView" or "EndView";
            var panelPlans = panel && refusal == null
                ? TimberPanelChainPreview.Build(GetDimensionPointCatalog(), _group, _includedModelIds,
                    DimensionPlacementSettings.MinimumChainSegmentViewUnits, () => _contacts?.Get())
                : null;
            if (panelPlans != null)
            {
                var diagnostics = new Dictionary<string, object?> { ["contactStatus"] = panelPlans.ContactStatus };
                if (panelPlans.ContactFallbackPairs.Length > 0) diagnostics["contactFallbackPairs"] = panelPlans.ContactFallbackPairs;
                if (panelPlans.UnlocatedModelIds.Length > 0) diagnostics["unlocatedModelIds"] = panelPlans.UnlocatedModelIds;
                if (panelPlans.MissingSupportModelIds.Length > 0) diagnostics["missingSupportModelIds"] = panelPlans.MissingSupportModelIds;
                if (panelPlans.TiltedPartIds.Length > 0) diagnostics["tiltedPartIds"] = panelPlans.TiltedPartIds;
                result["chainDiagnostics"] = diagnostics;
            }
            SectionDimensionChainPreview? sectionPreview = null;
            if (section && refusal == null && !SectionDimensionChainPreview.TryCreate(
                    GetDimensionPointCatalog(), _group, _mainPartIds[0], _includedModelIds,
                    DimensionPlacementSettings.MinimumChainSegmentViewUnits, Scale,
                    out sectionPreview, out var profileReason))
                refusal = profileReason;
            if (section)
                result["sectionPreviewStatus"] = refusal == null
                    ? "provisional: full-solid projection is not clipped to section depth, check the points on the view"
                    : "refused";
            var sectionPlans = section && refusal == null
                ? ((DimensionChainSide[])Enum.GetValues(typeof(DimensionChainSide))).ToDictionary(
                    side => side, side => sectionPreview!.Build(side))
                : null;
            if (sectionPlans != null)
            {
                var secondary = sectionPreview!.SecondaryIds;
                var unlocatedX = secondary.Except(sectionPlans[DimensionChainSide.Top].LocatedPartIds
                    .Concat(sectionPlans[DimensionChainSide.Bottom].LocatedPartIds)).ToArray();
                var unlocatedY = secondary.Except(sectionPlans[DimensionChainSide.Left].LocatedPartIds
                    .Concat(sectionPlans[DimensionChainSide.Right].LocatedPartIds)).ToArray();
                if (unlocatedX.Length > 0) result["unlocatedXModelIds"] = unlocatedX;
                if (unlocatedY.Length > 0) result["unlocatedYModelIds"] = unlocatedY;
            }
            else if (section)
            {
                result["unlocatedXModelIds"] = _includedModelIds.Except(_mainPartIds).ToArray();
                result["unlocatedYModelIds"] = _includedModelIds.Except(_mainPartIds).ToArray();
            }
            // A section answer is the chains to place, one per axis; the profile/location split and
            // the mirror sides are folded in (detailed answers keep the split).
            var consolidated = sectionPlans != null && !detailed
                ? SectionDimensionChainPreview.Consolidate(sectionPlans.Values).Where(k => selected.Contains(k.Side)).ToArray()
                : null;
            if (consolidated != null && consolidated.Length > 0)
            {
                result["chainPreview"] = consolidated.Select(k => new {
                    side = k.Side.ToString(),
                    chains = new[] { DimensionChainPreview.Short(SectionDimensionChainPreview.MergedChain(k), detailed, k.Side.ToString()) }
                }).ToArray();
                result["partSpanMatchToleranceMm"] = CalcDimensionChains.PartSpanMatchToleranceMm;
                return Freeze(result, display: true);
            }
            result["chainPreview"] = selected.Select(side => new {
                side = side.ToString(),
                chains = panelPlans != null
                    ? panelPlans.Rows.Where(row => StringComparer.Ordinal.Equals(row.side, side.ToString()))
                        .SelectMany(row => row.chains)
                        .Select(chain => DimensionChainPreview.Short(chain, detailed, side.ToString())).ToArray()
                    : (refusal != null
                    ? section ? SectionDimensionChainPreview.Refused(refusal).Chains : DimensionChainPreview.Refused(side, refusal)
                    : section ? sectionPlans![side].Chains : DimensionChainPreview.Build(GetDimensionPointCatalog(), side,
                        _mainPartIds, DimensionPlacementSettings.MinimumChainSegmentViewUnits))
                    .Select(chain => DimensionChainPreview.Short(chain, detailed, side.ToString())).ToArray()
            }).ToArray();
        }
        if (Wants("points") || Wants("edges"))
            result["sides"] = selected.Select(side => {
                var row = new Dictionary<string, object?> { ["side"] = side.ToString() };
                if (Wants("edges")) row["edge"] = Edge(side);
                if (Wants("points")) row["points"] = ShortPoints(_group.DimensionChains![side]);
                return row;
            }).ToArray();
        result["partSpanMatchToleranceMm"] = CalcDimensionChains.PartSpanMatchToleranceMm;
        return Freeze(result, display: true);
    }

    internal void AttachContacts(ViewContactsSnapshot contacts)
    {
        if (contacts == null) throw new ArgumentNullException(nameof(contacts));
        _contacts = contacts;
        contacts.Seed(_partSolids);
    }

    internal int[] ContactModelIds => _contactModelIds;
    internal IReadOnlyList<UnreadPart> ContactSelectionUnread => _contactSelectionUnread;

    // The preview stops instead of guessing when its assumptions do not hold.
    private string? ChainPreviewRefusal()
    {
        if (_mainPartUnresolvedIds.Length > 0)
            return "the main part could not be resolved for some parts";
        if (_mainPartIds.Length != 1)
            return _mainPartIds.Length == 0 ? "no main part in this view" : "more than one main part in this view";
        if (!_complete)
            return "the structural view context is incomplete; inspect issues and unresolvedDepthModelIds";
        return null;
    }

    private string? PanelPreviewRefusal()
    {
        if (!_complete)
            return "the structural view context is incomplete; inspect issues and unresolvedDepthModelIds";
        var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
        if (viewType is not "FrontView" and not "BackView")
            return "panel chain preview currently supports FrontView/BackView elevations only";
        return null;
    }

    internal JsonElement BuildBoltPreview(IReadOnlyCollection<DimensionChainSide> sides)
    {
        var selected = sides.Distinct().OrderBy(side => side).ToArray();
        var key = string.Join(",", selected);
        if (!_boltPreviews.TryGetValue(key, out var preview))
        {
            preview = Freeze(BoltDimensionChainPreview.Build(_bolts, _includedModelIds, selected,
                JsonSerializer.Deserialize<Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>>(_partContours.GetRawText())));
            _boltPreviews.Add(key, preview);
        }
        return preview;
    }

    internal double[] ResolveBoltProposal(string proposalId, int partId, string direction)
    {
        var side = ParseSide(direction);
        var axis = side is DimensionChainSide.Top or DimensionChainSide.Bottom ? "X" : "Y";
        var preview = BuildBoltPreview((DimensionChainSide[])Enum.GetValues(typeof(DimensionChainSide)));
        foreach (var chain in preview.GetProperty("partChains").EnumerateArray())
        {
            if (chain.GetProperty("proposalId").GetString() != proposalId) continue;
            if (chain.GetProperty("state").GetString() != "Candidate")
                throw new ArgumentException("Bolt proposal is blocked: " + chain.GetProperty("reason").GetString());
            if (chain.GetProperty("partId").GetInt32() != partId)
                throw new ArgumentException("Bolt proposal does not belong to the selected included related part");
            if (chain.GetProperty("placementSide").GetString() != side.ToString())
                throw new ArgumentException("Bolt proposal placement side does not match the requested direction");
            return chain.GetProperty("points").EnumerateArray().SelectMany(point =>
                point.EnumerateArray().Select(coordinate => coordinate.GetDouble())).ToArray();
        }
        foreach (var group in preview.GetProperty("groups").EnumerateArray())
        {
            foreach (var chain in group.GetProperty("chains").EnumerateArray()
                .Concat(group.GetProperty("edgeChains").EnumerateArray()))
            {
                if (chain.GetProperty("proposalId").GetString() != proposalId) continue;
                if (chain.GetProperty("state").GetString() != "Candidate")
                    throw new ArgumentException("Bolt proposal is blocked: " + chain.GetProperty("reason").GetString());
                if (chain.GetProperty("axis").GetString() != axis)
                    throw new ArgumentException("Bolt proposal axis does not match the requested placement direction");
                if (!group.GetProperty("partCandidates").EnumerateArray().Any(id => id.GetInt32() == partId)
                    || (chain.TryGetProperty("partId", out var owner) && owner.GetInt32() != partId))
                    throw new ArgumentException("Bolt proposal does not belong to the selected included related part");
                var points = chain.GetProperty("points").EnumerateArray().SelectMany(point =>
                    (point.ValueKind == JsonValueKind.Array ? point : point.GetProperty("point"))
                    .EnumerateArray().Select(coordinate => coordinate.GetDouble())).ToArray();
                if (points.Length < 6) throw new ArgumentException("Bolt proposal needs at least two points");
                return points;
            }
        }
        throw new ArgumentException("Unknown boltProposal in this context; query boltChains again");
    }

    internal double[] ResolvePointIds(IEnumerable<string> pointIds, string direction)
    {
        EnsureChains();
        return GetDimensionPointCatalog().Resolve(pointIds, ParseSide(direction));
    }

    private DimensionPointCatalog GetDimensionPointCatalog() =>
        _dimensionPointCatalog ??= DimensionPointCatalog.Build(_group.DimensionChains!);

    private static object ContactAnswer(ViewContactCandidatePointsResult result, bool detailed, int[]? pair)
    {
        static string PairKey(IEnumerable<int> ids) => string.Join(",", ids.OrderBy(id => id));
        bool Matches(IEnumerable<int> ids) => pair == null || PairKey(ids) == PairKey(pair);

        var shapes = result.Shapes.Where(shape => shape.Participants.Resolved
            && Matches(shape.Participants.ModelObjectIds)).ToArray();
        var points = result.Points.Where(point => Matches(point.ModelObjectIds)).ToArray();
        var pairs = result.Shapes.Where(shape => shape.Participants.Resolved)
            .GroupBy(shape => PairKey(shape.Participants.ModelObjectIds), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new {
                modelObjectIds = group.First().Participants.ModelObjectIds.OrderBy(id => id).ToArray(),
                contactCount = group.Select(shape => shape.ContactId).Distinct(StringComparer.Ordinal).Count(),
                shapeCount = group.Count(),
                pointCount = result.Points.Count(point => PairKey(point.ModelObjectIds) == group.Key),
                states = group.Select(shape => shape.State.ToString()).Distinct().OrderBy(state => state).ToArray(),
                kinds = group.Select(shape => shape.Kind.ToString()).Distinct().OrderBy(kind => kind).ToArray()
            }).ToArray();

        var answer = new Dictionary<string, object?> {
            ["scope"] = "all-depth-visible", ["exclusionsApplied"] = false,
            ["detail"] = detailed ? (pair == null ? "all" : "pair") : "summary",
            ["success"] = result.Error == null, ["isComplete"] = result.IsComplete,
            ["searchComplete"] = result.SearchComplete,
            ["selectionComplete"] = result.SelectionComplete, ["error"] = result.Error,
            ["requestedIds"] = result.RequestedIds, ["pairCount"] = pairs.Length,
            ["shapeCount"] = result.Shapes.Count, ["pointCount"] = result.Points.Count,
            ["pairs"] = pairs
        };
        if (result.Unread.Count > 0) answer["unread"] = result.Unread;
        if (result.Unflattened.Count > 0) answer["unflattened"] = result.Unflattened;
        if (result.Unresolved.Count > 0) answer["unresolved"] = result.Unresolved;
        if (!detailed) return answer;

        if (pair != null) answer["modelObjectIds"] = pair;
        answer["selectedShapeCount"] = shapes.Length;
        answer["selectedPointCount"] = points.Length;
        answer["points"] = points.Select(point => new {
            modelObjectIds = point.ModelObjectIds,
            point = point.Point,
            confidence = point.Confidence.ToString(),
            anchorKind = point.Anchor.Kind.ToString(),
            anchorKey = point.Anchor.Id,
            reason = point.Reason.Code,
            values = point.Reason.Values
        }).ToArray();
        answer["shapes"] = shapes.Select(shape => new {
            contactId = shape.ContactId,
            shapeId = shape.ShapeId,
            modelObjectIds = shape.Participants.ModelObjectIds,
            contactKind = shape.Kind.ToString(),
            contactState = shape.State.ToString(),
            normal = new[] { shape.Normal.X, shape.Normal.Y, shape.Normal.Z },
            shapeKind = shape.Shape.Kind.ToString(),
            points = shape.Shape.Points.Select(point => new[] { point.X, point.Y }).ToArray()
        }).ToArray();
        return answer;
    }

    private static int[]? ParseContactPair(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var values = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => int.TryParse(item.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id : throw new ArgumentException("contactPair must contain exactly two integer model IDs"))
            .Distinct().OrderBy(id => id).ToArray();
        if (values.Length != 2)
            throw new ArgumentException("contactPair must contain exactly two distinct model IDs");
        return values;
    }

    private object[] ShortPoints(DimensionChain chain) => chain.Positions
        .SelectMany(p => p.Supports).GroupBy(s => (s.Point.X, s.Point.Y))
        .OrderBy(g => chain.Side is DimensionChainSide.Top or DimensionChainSide.Bottom ? g.Key.X : g.Key.Y)
        .ThenBy(g => chain.Side is DimensionChainSide.Top or DimensionChainSide.Bottom ? g.Key.Y : g.Key.X)
        .Select(g => (object)new {
            x = g.Key.X, y = g.Key.Y,
            supports = g.GroupBy(s => (s.ModelId, s.Kind, s.Source.IsHole, Extent: Span(chain.Side, s)))
                .Select(s => {
                    var d = new Dictionary<string, object?> { ["kind"] = s.Key.Kind.ToString() };
                    if (s.Key.ModelId.HasValue) d["modelId"] = s.Key.ModelId.Value;
                    if (s.Key.IsHole) d["isHole"] = true;
                    if (s.Key.Extent.HasValue) d["partExtentAlongChain"] = s.Key.Extent.Value;
                    return d;
                }).ToArray()
        }).ToArray();

    // The default short answer keeps only what differs from the norm: empty lists, a single-item
    // main-part list, the fixed projection note and `success:true` are left out. The `diagnostics`
    // question returns the full header.
    private Dictionary<string, object?> CompactHeader()
    {
        var result = new Dictionary<string, object?> {
            ["viewId"] = ViewId, ["contextId"] = ContextId, ["isComplete"] = _complete
        };
        if (_exclusions.Length > 0) result["exclusions"] = _exclusions;
        if (_mainPartIds.Length == 1) result["mainPart"] = _mainPartIds[0];
        foreach (var property in _diagnostics.EnumerateObject())
        {
            if (property.Name == "mainPartModelIds" && _mainPartIds.Length == 1) continue;
            if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0) continue;
            result[property.Name] = property.Value;
        }
        return result;
    }

    private Dictionary<string, object?> Header(bool shortAnswer = false, bool compact = false)
    {
        if (compact) return CompactHeader();
        var result = new Dictionary<string, object?> {
            ["success"] = true, ["viewId"] = ViewId, ["contextId"] = ContextId, ["isComplete"] = _complete,
            ["exclusions"] = _exclusions,
            ["projectionVerification"] = "full-solid projection; section clipping is not verified"
        };
        foreach (var property in _diagnostics.EnumerateObject()) result[property.Name] = property.Value;
        result["view"] = _metadata;
        if (!shortAnswer) { result["source"] = _source; result["extent"] = Extent(); }
        return result;
    }

    private object? Extent() => _group.Extent == null ? null : new {
        minX = _group.Extent.MinX, maxX = _group.Extent.MaxX,
        minY = _group.Extent.MinY, maxY = _group.Extent.MaxY };

    private double? Edge(DimensionChainSide side) => _group.Extent == null ? null : side switch {
        DimensionChainSide.Top => _group.Extent.MaxY, DimensionChainSide.Bottom => _group.Extent.MinY,
        DimensionChainSide.Left => _group.Extent.MinX, _ => _group.Extent.MaxX };

    private void EnsureChains()
    {
        if (_group.DimensionChains == null) CalcDimensionChains.Apply(_group);
    }

    private string Fingerprint()
    {
        if (_fingerprint != null) return _fingerprint;
        using var hash = SHA256.Create();
        var text = JsonSerializer.Serialize(new { source = _source, metadata = _metadata, Scale,
            exclusions = _exclusions, extent = Extent(), parts = _parts, bolts = _bolts,
            chains = _group.DimensionChains!.Chains.Select(c => new { c.Side, positions = VerbosePositions(c) }) });
        return _fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    private static object[] VerbosePositions(DimensionChain chain) => chain.Positions.Select((p, index) => (object)new {
        positionIndex = index, coordinate = p.Coordinate,
        supports = p.Supports.Select((s, i) => new { supportIndex = i, sourceId = s.Source.Id,
            modelId = s.ModelId, partExtentAlongChain = Span(chain.Side, s), isHole = s.Source.IsHole,
            kind = s.Kind.ToString(), point = new[] { s.Point.X, s.Point.Y } }).ToArray()
    }).ToArray();

    private static double? Span(DimensionChainSide side, DimensionChainPositionSupport s) =>
        s.AxisAlignedModelExtent == null ? null : side is DimensionChainSide.Top or DimensionChainSide.Bottom
            ? s.AxisAlignedModelExtent.MaxX - s.AxisAlignedModelExtent.MinX
            : s.AxisAlignedModelExtent.MaxY - s.AxisAlignedModelExtent.MinY;

    private static PartSolidGeometryInViewResult Clone(PartSolidGeometryInViewResult source) => new()
    {
        Success = source.Success,
        ViewId = source.ViewId,
        ModelId = source.ModelId,
        Error = source.Error,
        StartPoint = (double[])source.StartPoint.Clone(),
        EndPoint = (double[])source.EndPoint.Clone(),
        CoordinateSystemOrigin = (double[])source.CoordinateSystemOrigin.Clone(),
        AxisX = (double[])source.AxisX.Clone(),
        AxisY = (double[])source.AxisY.Clone(),
        Solid = new PartSolidGeometry
        {
            BboxMin = (double[])source.Solid.BboxMin.Clone(),
            BboxMax = (double[])source.Solid.BboxMax.Clone(),
            SolidGeometryComplete = source.Solid.SolidGeometryComplete,
            ViewHull = source.Solid.ViewHull.Select(point => (double[])point.Clone()).ToList(),
            Vertices = source.Solid.Vertices.Select(vertex => new PartVertexGeometry
            {
                Index = vertex.Index,
                Point = (double[])vertex.Point.Clone()
            }).ToList(),
            Faces = source.Solid.Faces.Select(face => new PartFaceGeometry
            {
                Index = face.Index,
                Normal = face.Normal == null ? null : (double[])face.Normal.Clone(),
                Loops = face.Loops.Select(loop => new PartLoopGeometry
                {
                    Index = loop.Index,
                    VertexIndexes = new List<int>(loop.VertexIndexes)
                }).ToList()
            }).ToList()
        }
    };

    internal static JsonElement Freeze(object value, bool display = false)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, display ? DisplayOptions : null));
        return doc.RootElement.Clone();
    }

    private static readonly JsonSerializerOptions DisplayOptions = new() { Converters = { new DisplayDoubleConverter() } };
    private sealed class DisplayDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            writer.WriteRawValue(Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string[] Split(string value) => value.Split(',').Select(s => s.Trim().ToLowerInvariant())
        .Where(s => s.Length > 0).Distinct().ToArray();

    private static DimensionChainSide[] ParseSides(string sides)
    {
        var names = Split(sides);
        if (names.Length == 1 && names[0] == "all") return (DimensionChainSide[])Enum.GetValues(typeof(DimensionChainSide));
        if (names.Length == 0) throw new ArgumentException("sides must not be empty");
        return names.Select(n => Enum.TryParse<DimensionChainSide>(n, true, out var s)
            && Enum.IsDefined(typeof(DimensionChainSide), s) && !char.IsDigit(n[0])
                ? s : throw new ArgumentException("sides must contain Top, Bottom, Left, Right or all")).ToArray();
    }

    internal static DimensionChainSide ParseSide(string direction) => direction.Trim().ToLowerInvariant() switch {
        "horizontal" or "h" or "horizontal-up" => DimensionChainSide.Top,
        "horizontal-down" or "h-" => DimensionChainSide.Bottom,
        "vertical-left" or "v-" => DimensionChainSide.Left,
        "vertical" or "v" or "vertical-right" => DimensionChainSide.Right,
        _ => throw new ArgumentException("Automatic offset requires horizontal, horizontal-down, vertical-left or vertical direction; for a custom vector, supply distance explicitly")
    };
}
