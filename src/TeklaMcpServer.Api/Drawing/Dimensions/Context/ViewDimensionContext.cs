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
    private readonly IReadOnlyList<PartRoleInView> _partAttributes;
    private readonly JsonElement _diagnostics;
    private readonly JsonElement _source;
    private readonly JsonElement _metadata;
    private readonly string[] _exclusions;
    private readonly IReadOnlyDictionary<int, PartSolidGeometryInViewResult> _partSolids;
    private readonly int[] _contactModelIds;
    private readonly IReadOnlyList<UnreadPart> _contactSelectionUnread;
    private ViewContactsSnapshot? _contacts;
    private DimensionPointCatalog? _dimensionPointCatalog;
    private DimensionPointCatalog? _panelDimensionPointCatalog;
    private readonly ReferenceZoneOutline? _referenceZone;
    private readonly GeometryGroup? _referenceGroup;
    private (double X, double Y)[]? _referenceVertices;
    private GeometryGroupExtent? _panelReferenceExtent;
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
    /// <summary>Tekla view type of the view this context was read from (FrontView, BottomView...).</summary>
    public string? ViewType => _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
    internal bool IsComplete => _complete;
    private readonly Func<ViewDimensionContextCase> _toCase;

    /// <summary>What this context was built from, in a form that can be saved and rebuilt without Tekla.</summary>
    internal ViewDimensionContextCase ToCase() => _toCase();

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
        _toCase = () => ViewDimensionContextCase.From(viewId, scale, outline, exclusions, source, metadata, bolts);
        _partContours = Freeze(outline.Outline.PartNodes);
        Scale = scale;
        _bolts = bolts?.Answer ?? Freeze(new { isComplete = false, selectionComplete = false,
            visibilityVerified = false, error = "Bolt geometry was not captured" });
        _group = StructuralGeometryGroupBuilder.Build(outline);
        _referenceZone = outline.ReferenceZone;
        if (_referenceZone is { IsComplete: true })
            _referenceGroup = StructuralGeometryGroupBuilder.Build(new StructuralOutline(_referenceZone.Outline!,
                outline.Included.Concat(outline.Excluded).Concat(outline.Unclassified)
                    .Where(part => _referenceZone.ModelIds.Contains(part.ModelId)).ToArray(), [], []), "reference-zone");
        _complete = outline.IsComplete && _group.Completeness.IsComplete;
        _partSolids = new ReadOnlyDictionary<int, PartSolidGeometryInViewResult>(
            outline.Outline.PartSolidGeometries
                .Concat(outline.ReferenceZone?.Outline?.PartSolidGeometries ?? new Dictionary<int, PartSolidGeometryInViewResult>())
                .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().Value));
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
        _partAttributes = Array.AsReadOnly(outline.Included.Concat(outline.Excluded).Concat(outline.Unclassified)
            .GroupBy(p => p.ModelId).Select(g => g.First()).ToArray());
        _parts = Freeze(_partAttributes
            .Select(p => new { modelId = p.ModelId, partPos = p.PartPos, partPrefix = p.PartPrefix,
                name = p.Name, profile = p.Profile, material = p.Material,
                partPrefixKnown = p.PartPrefixKnown, materialKnown = p.MaterialKnown,
                zone = p.Zone, zoneKnown = p.ZoneKnown,
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

    public DimensionPlacementCalculation Calculate(string direction, double[] points, double? paperGapMm = null,
        bool usePanelReference = false)
    {
        if (!_complete || _group.Extent == null)
            throw new InvalidOperationException("Cannot calculate automatic dimension offset: assembly outline is incomplete or empty");
        return DimensionPlacementCalculator.Calculate(ParseSide(direction), points,
            usePanelReference ? PanelReferenceExtent() : _group.Extent,
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
        string ruleSet = "steel", string contactPair = "", IReadOnlyList<PartLayerRule>? layerRules = null,
        DimensionCoordinateSettings? coordinateSettings = null,
        Func<IReadOnlyList<DimensionChainRepeats.Placed>>? readSheet = null, bool storedRepeats = false)
    {
        // The sheet is read at most once per answer, however many steps compare against it.
        if (readSheet != null)
        {
            var sheet = new Lazy<IReadOnlyList<DimensionChainRepeats.Placed>>(readSheet);
            readSheet = () => sheet.Value;
        }
        var requested = Split(questions);
        var normalizedRuleSet = (ruleSet ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRuleSet.Length == 0) normalizedRuleSet = "steel";
        if (normalizedRuleSet is not "steel" and not "panel")
            throw new ArgumentException("ruleSet must be 'steel' or 'panel'");
        var allowed = new[] { "points", "dimensionpoints", "chain", "chaindetails", "edges", "parts", "bolts", "boltchains", "scale", "placement", "contacts", "contactdetails", "diagnostics", "repeats", "all" };
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
            result["placement"] = Calculate(direction, points ?? throw new ArgumentException("placement requires points"), paperGapMm,
                usePanelReference: normalizedRuleSet == "panel");
        if (Wants("scale")) result["scale"] = Scale;
        if (Wants("parts")) result["parts"] = _parts;
        if (Wants("bolts")) result["bolts"] = _bolts;
        if (requested.Contains("boltchains"))
            result["boltChainPreview"] = BuildBoltPreview(selected, coordinateSettings);
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
            result["dimensionPoints"] = (normalizedRuleSet == "panel"
                ? GetDimensionPointCatalog() : GetPanelDimensionPointCatalog()).Project(selected);
        if (wantsChain)
        {
            var panel = normalizedRuleSet == "panel";
            var refusal = panel ? PanelPreviewRefusal() : ChainPreviewRefusal();
            var detailed = requested.Contains("chaindetails");
            var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
            var section = viewType is "SectionView" or "EndView";
            var panelPlans = panel && refusal == null
                ? TimberPanelChainPreview.Build(GetDimensionPointCatalog(), _group, _includedModelIds,
                    DimensionPlacementSettings.MinimumChainSegmentViewUnits, () => _contacts?.Get(),
                    contextId: ContextId, viewId: ViewId, coordinateSettings: coordinateSettings, referenceZones: _referenceZone?.Zones,
                    referenceGroup: UsableReferenceGroup(), panelCatalog: GetPanelDimensionPointCatalog())
                : null;
            if (panelPlans != null)
            {
                var diagnostics = new Dictionary<string, object?> { ["contactStatus"] = panelPlans.ContactStatus };
                if (_referenceZone != null) diagnostics["referenceZone"] = new {
                    zones = _referenceZone.Zones, modelIds = _referenceZone.ModelIds,
                    status = UsableReferenceGroup() != null ? "complete" : "suppressed",
                    reason = _referenceZone.Error ?? (UsableReferenceGroup() == null ? "Reference polygon is incomplete or empty" : null)
                };
                if (panelPlans.ContactFallbackPairs.Length > 0) diagnostics["contactFallbackPairs"] = panelPlans.ContactFallbackPairs;
                if (panelPlans.UnlocatedModelIds.Length > 0) diagnostics["unlocatedModelIds"] = panelPlans.UnlocatedModelIds;
                if (panelPlans.MissingSupportModelIds.Length > 0) diagnostics["missingSupportModelIds"] = panelPlans.MissingSupportModelIds;
                if (panelPlans.TiltedPartIds.Length > 0) diagnostics["tiltedPartIds"] = panelPlans.TiltedPartIds;
                if (panelPlans.UnsupportedProposalCount > 0) diagnostics["unsupportedProposalCount"] = panelPlans.UnsupportedProposalCount;
                result["chainDiagnostics"] = diagnostics;
                if (detailed) result["compositionPlan"] = panelPlans.ProjectCompositionPlan(
                    plan => plan.Project(layerRules is { Count: > 0 } ? new PartLayerSnapshot(_partAttributes, layerRules) : null));
            }
            SectionDimensionChainPreview? sectionPreview = null;
            if (section && refusal == null && !SectionDimensionChainPreview.TryCreate(
                    GetPanelDimensionPointCatalog(), _group, _mainPartIds[0], _includedModelIds,
                    DimensionPlacementSettings.MinimumChainSegmentViewUnits, Scale,
                    out sectionPreview, out var profileReason, EndPartIds(_mainPartIds[0]), FaceOnPartIds()))
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
                var sideRows = consolidated.Select(k => (object)new {
                    side = k.Side.ToString(),
                    chains = new[] { DimensionChainPreview.Short(SectionDimensionChainPreview.MergedChain(k), detailed, k.Side.ToString()) }
                }).ToArray();
                sideRows = MarkRepeats(sideRows, requested.Contains("repeats") ? readSheet : null, storedRepeats, result);
                result["chainPreview"] = sideRows.Concat(selected.Length == 4 ? BoltRow(coordinateSettings, sideRows, sectionPlans?.Values) : []).ToArray();
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
                    : section ? sectionPlans![side].Chains : DimensionChainPreview.Build(GetPanelDimensionPointCatalog(), side,
                        _mainPartIds, DimensionPlacementSettings.MinimumSteelChainSegment(Scale),
                        inclinedPartIds: InclinedChains().SelectMany(chain => chain.LocatedPartIds).ToArray(),
                        faceOnPartIds: FaceOnPartIds(), partThickness: PartThicknesses(),
                        carriedPartIds: InclinedChains().SelectMany(chain => chain.AttachedPartIds).ToArray()))
                    .Select(chain => DimensionChainPreview.Short(chain, detailed, side.ToString())).ToArray()
            }).ToArray();
            // Chains along tilted parts belong to no side; they come with a whole-view answer.
            if (!panel && !section && refusal == null && selected.Length == 4 && InclinedChains().Count > 0)
                result["chainPreview"] = ((System.Collections.IEnumerable)result["chainPreview"]!).Cast<object>().Concat([new {
                    side = InclinedSide, chains = InclinedChains().Select(chain => chain.Project()).ToArray()
                }]).ToArray();
            // Short chains next to single parts, across the main part; they too belong to no side.
            if (!panel && !section && refusal == null && selected.Length == 4 && LocalChains().Count > 0)
                result["chainPreview"] = ((System.Collections.IEnumerable)result["chainPreview"]!).Cast<object>().Concat([new {
                    side = LocalSide, chains = LocalChains().Select(chain => chain.Project()).ToArray()
                }]).ToArray();
            // Repeats are decided before the bolt lines take their rows, so no row is kept for a
            // chain that will not be placed.
            var rows = MarkRepeats(((System.Collections.IEnumerable)result["chainPreview"]!).Cast<object>().ToArray(),
                requested.Contains("repeats") ? readSheet : null, storedRepeats, result);
            result["chainPreview"] = rows;
            // Bolt chains belong to no side either; each one says whether it is to be created.
            if (!panel && refusal == null && selected.Length == 4)
            {
                var withBolts = rows.Concat(BoltRow(coordinateSettings, rows, sectionPlans?.Values)).ToArray();
                // Sections confirm bolt edges by their own section chains; nothing is carried there.
                result["chainPreview"] = section ? withBolts
                    : MarkCarriedRepeats(CarryEdgelessBolts(withBolts, (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm),
                        requested.Contains("repeats") ? readSheet : null, storedRepeats, result);
            }
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

    internal const string InclinedSide = "Inclined";
    private IReadOnlyList<InclinedChain>? _inclinedChains;

    /// <summary>A chain along a tilted part, ready to be written: points and offset are final.</summary>
    internal sealed class InclinedChain(int partId, double[] points, double[] segments, string direction, double distance,
        string side = InclinedSide, string kind = InclinedPartDimensionRule.Kind, int[]? attachedPartIds = null)
    {
        public int PartId { get; } = partId;
        /// <summary>Parts standing on this one that the chain locates as well.</summary>
        public int[] AttachedPartIds { get; } = attachedPartIds ?? [];
        /// <summary>Every part this chain locates: no other chain needs to.</summary>
        public IEnumerable<int> LocatedPartIds => new[] { PartId }.Concat(AttachedPartIds);
        public string Key => side + "-" + PartId.ToString(CultureInfo.InvariantCulture);
        /// <summary>Flat view coordinates [x0,y0,0, x1,y1,0, ...], ordered along the part.</summary>
        public double[] Points { get; } = points;
        public double[] Segments { get; } = segments;
        /// <summary>The offset direction as a 'dx,dy,dz' vector: from the points toward the line.</summary>
        public string Direction { get; } = direction;
        public double Distance { get; } = distance;

        public object Project() => new {
            key = Key, direction = Direction, attributesFile = "standard", kind,
            partId = PartId, segments = Segments, distance = Distance,
            points = Enumerable.Range(0, Points.Length / 3).Select(i => new[] { Points[i * 3], Points[i * 3 + 1] }).ToArray(),
            incomplete = false
        };
    }

    // On a view with one resolved main part: the chains along its tilted parts. The offset puts the
    // line the default paper gap beyond the chain point farthest toward it.
    internal IReadOnlyList<InclinedChain> InclinedChains()
    {
        if (_inclinedChains != null) return _inclinedChains;
        if (!_complete || _mainPartIds.Length != 1 || _mainPartUnresolvedIds.Length > 0) return _inclinedChains = [];
        var evaluation = new DimensionRuleSet(new InclinedPartDimensionRule(new InclinedPartDimensionSettings(
                minimumSegmentViewUnits: DimensionPlacementSettings.MinimumChainSegmentViewUnits)))
            .Calculate(new DimensionRuleContext([], null, InclinedPartDimensionInput.FromGroup(_group, _mainPartIds[0])));
        return _inclinedChains = evaluation.Results.Select(result => {
            var normal = ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal;
            return new InclinedChain((int)result.Evidence["partId"],
                result.Points.SelectMany(point => new[] { point.X, point.Y, 0d }).ToArray(),
                result.Segments.ToArray(),
                string.Join(",", new[] { normal.X, normal.Y, 0d }.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                DimensionPlacementSettings.DefaultPaperGapMm * Scale + (double)result.Evidence["acrossSpan"],
                attachedPartIds: (int[])result.Evidence["attachedPartIds"]);
        }).Where(InsideViewFrame).ToArray();
    }

    // These chains are built from the whole part, not from the point catalog the frame has cut:
    // one with a point the view does not show is not offered.
    private bool InsideViewFrame(InclinedChain chain) => ViewFrame() is not { } frame
        || Enumerable.Range(0, chain.Points.Length / 3).All(i =>
            chain.Points[i * 3] >= frame.MinX && chain.Points[i * 3] <= frame.MaxX
            && chain.Points[i * 3 + 1] >= frame.MinY && chain.Points[i * 3 + 1] <= frame.MaxY);

    internal const string LocalSide = "Local";
    private IReadOnlyList<InclinedChain>? _localChains;

    // Short chains across the main part next to single parts. A tilted part has its own chain; a
    // part with a slanted edge (a fin plate cut at an angle) is measured to its real corner across
    // the main part. The line stands the default paper gap past the part, toward its far end.
    internal IReadOnlyList<InclinedChain> LocalChains()
    {
        if (_localChains != null) return _localChains;
        if (!_complete || _mainPartIds.Length != 1 || _mainPartUnresolvedIds.Length > 0) return _localChains = [];
        var mainId = _mainPartIds[0];
        var inclined = InclinedChains().SelectMany(chain => chain.LocatedPartIds).ToArray();
        (double MinX, double MaxX, double MinY, double MaxY)? Box(int id, bool straightOnly)
        {
            var shapes = _group.Shapes.Where(shape => shape.ModelId == id && !shape.IsHole
                && shape.Shape.Kind == SolidContacts.PlanarShapeKind.Polygon).ToArray();
            if (shapes.Length == 0) return null;
            var points = shapes.SelectMany(shape => shape.Shape.Points).ToArray();
            if (straightOnly && shapes.Any(shape => Enumerable.Range(0, shape.Shape.Points.Count).Any(i => {
                    var a = shape.Shape.Points[i];
                    var b = shape.Shape.Points[(i + 1) % shape.Shape.Points.Count];
                    return Math.Abs(a.X - b.X) > 0.01 && Math.Abs(a.Y - b.Y) > 0.01;
                }))) return null;
            return (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
        }
        var solid = GetPartSolidGeometrySnapshot(mainId);
        if (Box(mainId, straightOnly: false) is not { } main || solid == null) return _localChains = [];
        double[] Levels(int axis) => solid.Solid.Vertices.Where(vertex => vertex.Point.Length >= 3)
            .Select(vertex => Math.Round(vertex.Point[axis], 2)).Distinct().OrderBy(value => value).ToArray();
        var others = _includedModelIds.Where(id => id != mainId && !inclined.Contains(id)).ToArray();
        var parts = others.Select(id => (Id: id, Box: Box(id, straightOnly: false))).Where(part => part.Box.HasValue)
            .ToDictionary(part => part.Id, part => part.Box!.Value);
        // A part with a slanted edge: where along the main part its lowest and highest corners
        // across it stand; of several at one level, the one toward the part's far end, where the line is.
        var alongX = main.MaxX - main.MinX >= main.MaxY - main.MinY;
        var corners = parts.Keys.Where(id => Box(id, straightOnly: true) == null).ToDictionary(id => id, id => {
            var points = _group.Shapes.Where(shape => shape.ModelId == id && !shape.IsHole
                    && shape.Shape.Kind == SolidContacts.PlanarShapeKind.Polygon)
                .SelectMany(shape => shape.Shape.Points).Select(p => alongX ? (Along: p.X, Across: p.Y) : (Along: p.Y, Across: p.X)).ToArray();
            var (low, high) = (points.Min(p => p.Across), points.Max(p => p.Across));
            double At(double level) => points.Where(p => Math.Abs(p.Across - level) <= 0.01).Max(p => p.Along);
            return (LowAlong: At(low), HighAlong: At(high));
        });
        var evaluation = new DimensionRuleSet(new LocalPartDimensionRule(new LocalPartDimensionSettings(
                repeatGapViewUnits: DimensionPlacementSettings.LocalChainRepeatGapPaperMm * Scale)))
            .Calculate(new DimensionRuleContext([], null, new LocalPartDimensionInput(main, Levels(0), Levels(1), parts, corners)));
        var gap = DimensionPlacementSettings.DefaultPaperGapMm * Scale;
        var lines = new List<(double Nx, double Ny, double Line, double From, double To)>();
        return _localChains = evaluation.Results.Select(result => {
            var normal = ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal;
            var distance = gap + (double)result.Evidence["baseOffset"];
            return (Result: result, Normal: normal, Distance: distance,
                Line: result.Points[0].X * normal.X + result.Points[0].Y * normal.Y + distance);
        }).OrderBy(item => item.Line).Select(item => {
            // A line within one gap of a line already taken, whose span overlaps it, stands one gap
            // beyond that line: two parts at one place whose chains could not merge, or a
            // neighbour's chain, never share a line.
            var (normal, first) = (item.Normal, item.Result.Points[0]);
            var start = first.X * normal.X + first.Y * normal.Y;
            var along = item.Result.Points.Select(point => point.X * -normal.Y + point.Y * normal.X).ToArray();
            var (from, to) = (along.Min(), along.Max());
            var distance = item.Distance;
            while (lines.Where(line => Math.Abs(line.Nx - normal.X) < 1e-9 && Math.Abs(line.Ny - normal.Y) < 1e-9
                       && Math.Abs(line.Line - (start + distance)) < gap - DimensionPlacementSettings.VerificationToleranceViewUnits
                       && line.From <= to && from <= line.To)
                   .Select(line => (double?)line.Line).Max() is { } inTheWay)
                distance = inTheWay + gap - start;
            lines.Add((normal.X, normal.Y, start + distance, from, to));
            return new InclinedChain((int)item.Result.Evidence["partId"],
                item.Result.Points.SelectMany(point => new[] { point.X, point.Y, 0d }).ToArray(), item.Result.Segments.ToArray(),
                string.Join(",", new[] { normal.X, normal.Y, 0d }.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                distance, LocalSide, LocalPartDimensionRule.Kind);
        }).Where(InsideViewFrame).ToArray();
    }

    // The repeats of the last chain read with the repeats question; a plain chain read clears them.
    // The batch writes from the same answer the caller saw: same repeats, same bolt rows.
    private IReadOnlyList<DimensionChainRepeats.Repeat>? _repeats;

    // A chain that repeats another one, or a dimension on the sheet, is marked incomplete with the
    // reason, like any other chain not to be placed. readSheet: decide now (the repeats question);
    // stored: use the last decision (the batch); neither: no repeats, and the last decision is cleared.
    private object[] MarkRepeats(object[] rows, Func<IReadOnlyList<DimensionChainRepeats.Placed>>? readSheet,
        bool stored, Dictionary<string, object?> result)
    {
        var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
        if (readSheet != null)
        {
            _repeats = DimensionChainRepeats.Find(ViewId, viewType,
                DimensionChainRepeats.FromPreview(JsonSerializer.SerializeToElement(new { chainPreview = rows })), readSheet);
            result["repeats"] = ViewDimensionContextProvider.ProjectRepeats(_repeats);
        }
        else if (!stored) _repeats = null;
        var repeats = (readSheet != null || stored ? _repeats : null)?.ToDictionary(repeat => repeat.Key, StringComparer.Ordinal);
        if (repeats is not { Count: > 0 }) return rows;
        return JsonSerializer.SerializeToElement(rows).EnumerateArray().Select(row => (object)row.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Name != "chains" ? (object?)property.Value.Clone()
                : property.Value.EnumerateArray().Select(chain => chain.TryGetProperty("key", out var key)
                    && key.GetString() is { } name && repeats.TryGetValue(name, out var repeat)
                    ? (object)Repeated(chain, repeat) : chain.Clone()).ToArray())).ToArray();
    }

    private static Dictionary<string, object?> Repeated(JsonElement chain, DimensionChainRepeats.Repeat repeat)
    {
        var marked = chain.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        marked["incomplete"] = true;
        marked["repeat"] = repeat.Reason;
        marked["incompleteReason"] = repeat.Reason switch {
            "sameView" => $"repeats {repeat.Of} on this view",
            "overallOnSheet" => $"repeats dimension {repeat.DimensionId} on view {repeat.ViewId}",
            _ => $"mirrors dimension {repeat.DimensionId} on view {repeat.ViewId}"
        };
        return marked;
    }

    internal const string BoltEdgeless = "no part edge is confirmed on this view";

    // A plan bolt chain left without any edge of its part (a fin plate with slanted edges beside the
    // bolts) gives its bolts to the location chain of the same side, as a detailer measures them from
    // the edges that chain already gives: the location chain and the bolts become one chain in the
    // Bolt row, placed where the location chain would stand, and the location chain is not placed.
    // Only bolts lying inside that chain's span are carried; a location chain not to be placed (a
    // repeat) carries nothing.
    private object[] CarryEdgelessBolts(object[] rows, double tolerance)
    {
        var json = JsonSerializer.SerializeToElement(rows);
        static Dictionary<string, object?> Copy(JsonElement element) =>
            element.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        var copied = json.EnumerateArray().Select(row => {
            var copy = Copy(row);
            copy["chains"] = row.GetProperty("chains").EnumerateArray().Select(Copy).ToList();
            return copy;
        }).ToList();
        List<Dictionary<string, object?>> Chains(Dictionary<string, object?> row) => (List<Dictionary<string, object?>>)row["chains"]!;
        static JsonElement Value(Dictionary<string, object?> chain, string name) =>
            chain.TryGetValue(name, out var value) && value is JsonElement element ? element : default;
        static string? Text(Dictionary<string, object?> chain, string name) =>
            chain.TryGetValue(name, out var value) && value is string text ? text
            : Value(chain, name) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
        static bool Incomplete(Dictionary<string, object?> chain) =>
            chain.TryGetValue("incomplete", out var value) && (value is true || value is JsonElement { ValueKind: JsonValueKind.True });

        var bolt = copied.FirstOrDefault(row => Value(row, "side").GetString() == BoltSide);
        // An incomplete bolt read creates no bolt dimension, a carried one included.
        if (bolt == null || Value(bolt, "readIncomplete").ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) return rows;
        var carried = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var locations = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var chain in Chains(bolt).ToArray())
        {
            if (!Incomplete(chain) || Text(chain, "incompleteReason") != BoltEdgeless || Text(chain, "viewClass") != "Plan"
                || Text(chain, "chainKind") != "part") continue;
            var sideName = Text(chain, "key")!.Split('-').Last();
            if (!Enum.TryParse<DimensionChainSide>(sideName, out var side)) continue;
            var axis = side.RunsAlongX() ? 0 : 1;
            var bolts = Value(chain, "sources").EnumerateArray()
                .Select(source => source.GetProperty("point")).Select(point => new[] { point[0].GetDouble(), point[1].GetDouble() }).ToArray();
            if (bolts.Length == 0) continue;
            Dictionary<string, object?> target;
            double[][] points;
            if (carried.TryGetValue(sideName, out var existing))
            {
                target = existing;
                points = Value(existing, "points").ValueKind == JsonValueKind.Array
                    ? Value(existing, "points").EnumerateArray().Select(point => new[] { point[0].GetDouble(), point[1].GetDouble() }).ToArray()
                    : ((double[][])existing["points"]!);
            }
            else
            {
                var row = copied.FirstOrDefault(candidate => Value(candidate, "side").GetString() == sideName);
                var location = row == null ? null : Chains(row).FirstOrDefault(candidate => Text(candidate, "kind") == "location" && !Incomplete(candidate));
                if (location == null || Text(location, "direction") == null) continue;
                var ids = Value(location, "pointIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
                if (ids.Length < 2) continue;
                var flat = ResolvePointIds(ids, Text(location, "direction")!, usePanelReference: false);
                points = Enumerable.Range(0, flat.Length / 3).Select(i => new[] { flat[i * 3], flat[i * 3 + 1] }).ToArray();
                target = new Dictionary<string, object?> {
                    ["key"] = Text(location, "key") + "-bolts", ["direction"] = Text(location, "direction"),
                    ["attributesFile"] = Text(location, "attributesFile") ?? "standard", ["kind"] = BoltChainRule.Kind,
                    ["chainKind"] = "location", ["viewClass"] = "Plan", ["partId"] = Value(chain, "partId").Clone(),
                    ["carries"] = Text(location, "key")
                };
                location["incomplete"] = true;
                location["incompleteReason"] = "carried with the bolts of its part by " + target["key"];
            }
            var (from, to) = (points.Min(point => point[axis]), points.Max(point => point[axis]));
            if (bolts.Any(point => point[axis] <= from + tolerance || point[axis] >= to - tolerance))
            {
                if (!carried.ContainsKey(sideName) && target.TryGetValue("carries", out var key))
                {
                    // Nothing carried after all: the location chain stays as it was.
                    var row = copied.First(candidate => Value(candidate, "side").GetString() == sideName);
                    var location = Chains(row).First(candidate => Text(candidate, "key") == (string)key!);
                    location["incomplete"] = false;
                    location.Remove("incompleteReason");
                }
                continue;
            }
            var merged = points.Concat(bolts).OrderBy(point => point[axis]).Aggregate(new List<double[]>(), (list, point) => {
                if (list.Count == 0 || point[axis] - list[list.Count - 1][axis] > tolerance) list.Add(point);
                return list;
            }).ToArray();
            var placement = Calculate(Text(target, "direction")!, merged.SelectMany(point => new[] { point[0], point[1], 0d }).ToArray());
            target["points"] = merged;
            target["segments"] = merged.Skip(1).Select((point, index) => Math.Round(point[axis] - merged[index][axis], 3)).ToArray();
            target["distance"] = placement.Distance;
            target["incomplete"] = false;
            target["sources"] = (target.TryGetValue("sources", out var before) && before is List<JsonElement> list ? list : new List<JsonElement>())
                .Concat(Value(chain, "sources").EnumerateArray().Select(source => source.Clone())).ToList();
            if (!carried.ContainsKey(sideName))
            {
                carried[sideName] = target;
                Chains(bolt).Add(target);
                var row = copied.First(candidate => Value(candidate, "side").GetString() == sideName);
                locations[sideName] = Chains(row).First(candidate => Text(candidate, "key") == Text(target, "carries"));
            }
            chain["incompleteReason"] = BoltEdgeless + "; its bolts are carried by " + target["key"];
        }
        foreach (var pair in carried) RestoreOverall(copied.First(row => Value(row, "side").GetString() == pair.Key),
            locations[pair.Key], pair.Value);
        return carried.Count == 0 ? rows : copied.Cast<object>().ToArray();

        // A location chain of one segment was the side's overall; with the bolts in it, it is not any
        // more, so the overall comes back on its two points. Only an overall left out for that reason
        // returns; one refused for anything else stays refused, and its repeats are decided after.
        void RestoreOverall(Dictionary<string, object?> row, Dictionary<string, object?> location, Dictionary<string, object?> target)
        {
            var ids = Value(location, "pointIds");
            if (ids.GetArrayLength() != 2 || ((double[][])target["points"]!).Length <= 2) return;
            var overall = Chains(row).FirstOrDefault(candidate => Text(candidate, "kind") == "overall" && Incomplete(candidate)
                && Text(candidate, "incompleteReason")?.EndsWith(DimensionChainPreview.OverallSpanned, StringComparison.Ordinal) == true);
            if (overall == null) return;
            overall["pointIds"] = ids.Clone();
            overall["segments"] = Value(location, "segments").Clone();
            overall["incomplete"] = false;
            overall.Remove("incompleteReason");
            overall["restoredBesides"] = Text(target, "key");
        }
    }

    // A location chain carrying bolts is compared for repeats like the location chain it stands
    // for: on a back view, the mirror of the front view's carried chain is a repeat too. Its repeats
    // join the stored ones, so the batch sees the same answer.
    private object[] MarkCarriedRepeats(object[] rows, Func<IReadOnlyList<DimensionChainRepeats.Placed>>? readSheet,
        bool stored, Dictionary<string, object?> result)
    {
        if (readSheet == null && !stored) return rows;
        var json = JsonSerializer.SerializeToElement(rows);
        var restored = json.EnumerateArray().SelectMany(row => row.GetProperty("chains").EnumerateArray())
            .Where(chain => chain.TryGetProperty("restoredBesides", out _)).Select(chain => chain.GetProperty("key").GetString()!).ToArray();
        var carried = json.EnumerateArray().Where(row => row.GetProperty("side").GetString() == BoltSide)
            .SelectMany(row => row.GetProperty("chains").EnumerateArray())
            .Where(chain => chain.TryGetProperty("carries", out var carries) && carries.ValueKind == JsonValueKind.String
                && !(chain.TryGetProperty("incomplete", out var incomplete) && incomplete.ValueKind == JsonValueKind.True))
            .Select(chain => new DimensionChainRepeats.Chain(chain.GetProperty("key").GetString()!,
                chain.GetProperty("carries").GetString()!.Split('-')[0], "location",
                chain.GetProperty("segments").EnumerateArray().Select(value => value.GetDouble()).ToArray()))
            .ToArray();
        if (carried.Length == 0) return rows;
        var keys = carried.Select(chain => chain.Key).Concat(restored).ToHashSet(StringComparer.Ordinal);
        if (readSheet != null)
        {
            var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
            // A restored overall is compared with every chain left on the view, as on any other side.
            var chains = DimensionChainRepeats.FromPreview(JsonSerializer.SerializeToElement(new { chainPreview = rows })).Concat(carried).ToArray();
            var found = DimensionChainRepeats.Find(ViewId, viewType, chains, readSheet).Where(repeat => keys.Contains(repeat.Key));
            _repeats = (_repeats ?? []).Where(repeat => !keys.Contains(repeat.Key)).Concat(found).ToArray();
            result["repeats"] = ViewDimensionContextProvider.ProjectRepeats(_repeats);
        }
        var repeats = (_repeats ?? []).Where(repeat => keys.Contains(repeat.Key))
            .ToDictionary(repeat => repeat.Key, StringComparer.Ordinal);
        if (repeats.Count == 0) return rows;
        return json.EnumerateArray().Select(row => (object)row.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Name != "chains" ? (object?)property.Value.Clone()
                : property.Value.EnumerateArray().Select(chain => chain.TryGetProperty("key", out var key)
                    && key.GetString() is { } name && repeats.TryGetValue(name, out var repeat)
                    ? (object)Repeated(chain, repeat) : chain.Clone()).ToArray())).ToArray();
    }

    internal const string BoltSide = "Bolt";
    private readonly Dictionary<string, IReadOnlyList<BoltRowChain>> _boltChains = new(StringComparer.Ordinal);

    /// <summary>A bolt chain of the Bolt row: written by key like a Local chain when it is to be created.</summary>
    internal sealed class BoltRowChain(DimensionRuleResult result, double distance, string? frameRefusal)
    {
        public string Key { get; } = (string)result.Evidence["key"];
        public string? Reason { get; } = result.Note ?? frameRefusal;
        public bool Create => Reason == null;

        public object Project() => new {
            key = Key, direction = string.Join(",", new[] {
                ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal.X,
                ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal.Y, 0d }
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            attributesFile = "standard", kind = BoltChainRule.Kind,
            chainKind = result.Evidence["chainKind"], boltGroupId = result.Evidence["groupId"],
            partId = result.Evidence["partId"], viewClass = result.Evidence["viewClass"],
            segments = result.Segments, distance,
            points = result.Points.Select(point => new[] { point.X, point.Y }).ToArray(),
            sources = result.Evidence["sources"], holes = result.Evidence["holes"],
            incomplete = !Create, incompleteReason = Reason
        };
    }

    private object[] BoltRow(DimensionCoordinateSettings? coordinateSettings, object[] sideRows,
        IEnumerable<SectionDimensionChainPreview.SideResult>? sectionPlans)
    {
        var chains = BoltChains(coordinateSettings, sideRows, sectionPlans);
        var readRefusal = BoltReadRefusal();
        // An incomplete read stays visible even when no group was read: no chains is not "no bolts".
        return chains.Count == 0 && readRefusal == null ? [] : [new { side = BoltSide, readIncomplete = readRefusal,
            chains = chains.Select(chain => chain.Project()).ToArray() }];
    }

    // Bolt chains by Tekla-like settings. Part edges count as confirmed only off sections: a
    // section contour is not read yet, so there an edge counts only where a part chain uses it.
    internal IReadOnlyList<BoltRowChain> BoltChains(DimensionCoordinateSettings? coordinateSettings = null,
        object[]? sideRows = null, IEnumerable<SectionDimensionChainPreview.SideResult>? sectionPlans = null,
        BoltChainSettings? boltSettings = null)
    {
        var tolerance = (coordinateSettings ?? new DimensionCoordinateSettings()).ToleranceMm;
        var sideRowCounts = sideRows == null ? null : SideRowCounts(sideRows);
        var cacheKey = tolerance.ToString("R", CultureInfo.InvariantCulture) + ":" + (sideRows == null ? ""
            : JsonSerializer.SerializeToElement(sideRows).GetRawText());
        // Settings given by the caller are not the context's own: their answer is not kept.
        if (boltSettings == null && _boltChains.TryGetValue(cacheKey, out var cached)) return cached;
        var settings = boltSettings ?? new BoltChainSettings();
        var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
        var candidates = BoltChainCandidates.Build(_bolts, _includedModelIds, _mainPartIds,
            JsonSerializer.Deserialize<Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>>(_partContours.GetRawText()),
            edgeConfirmed: viewType is not ("SectionView" or "EndView") ? (_, _, _) => true : SectionEdgeCheck(sectionPlans ?? []),
            settings, coordinateSettings, DimensionPlacementSettings.DefaultPaperGapMm * Scale);
        var evaluation = new DimensionRuleSet(new BoltChainRule(settings))
            .Calculate(new DimensionRuleContext([], null, new BoltChainInput(candidates)));
        var frame = ViewFrame();
        var readRefusal = BoltReadRefusal();
        var gap = DimensionPlacementSettings.DefaultPaperGapMm * Scale;
        var rows = OccupiedLines(sideRowCounts, gap);
        var refusals = evaluation.Results.Select(result => {
            var outside = frame is { } f && result.Points.Any(point =>
                point.X < f.MinX || point.X > f.MaxX || point.Y < f.MinY || point.Y > f.MaxY);
            return readRefusal ?? (outside ? "a point lies outside the view frame" : null);
        }).ToArray();
        // A secondary part's bolts standing on the main part's are left out only now, when the main
        // part's chain is known to be created: settings and view frame have refused what they refuse.
        var created = new HashSet<string>(evaluation.Results.Where((result, index) => result.Note == null && refusals[index] == null)
            .Select(result => (string)result.Evidence["key"]), StringComparer.Ordinal);
        for (var i = 0; i < refusals.Length; i++)
            if (refusals[i] == null && evaluation.Results[i].Note == null
                && evaluation.Results[i].Evidence.TryGetValue("overMainPartChain", out var over)
                && over is string main && main.Length > 0 && created.Contains(main))
                refusals[i] = $"the same bolts are dimensioned from the main part by {main}";
        // Rows go only to the chains that will be placed.
        var chains = evaluation.Results.Select((result, index) => {
            var distance = gap + (double)result.Evidence["baseOffset"];
            if (result.Note == null && refusals[index] == null && result.Points.Count > 0)
                distance = NextFreeRow(rows, result, distance, gap);
            return new BoltRowChain(result, distance, refusals[index]);
        }).ToArray();
        if (boltSettings == null) _boltChains.Add(cacheKey, chains);
        return chains;
    }

    // Use the unmerged section supports, never the formatted answer. Both ends must be
    // supported by the same part on this axis; a matching coordinate on another part proves nothing.
    private Func<int, int, double[], bool> SectionEdgeCheck(
        IEnumerable<SectionDimensionChainPreview.SideResult> sectionPlans)
    {
        var supports = sectionPlans.SelectMany(plan => plan.EdgePoints.SelectMany(point => point.Parents
            .Where(parent => parent.ModelId.HasValue && !parent.IsHole)
            .Select(parent => (PartId: parent.ModelId!.Value, Axis: plan.Side.RunsAlongX() ? 0 : 1,
                Along: plan.Side.RunsAlongX() ? point.X : point.Y)))).ToArray();
        // A part seen flat on the section is drawn whole, as on an ordinary view: its outline is true.
        var flat = new HashSet<int>(FaceOnPartIds());
        var tolerance = CalcDimensionChains.PartSpanMatchToleranceMm;
        // A support counts only from a chain of the same axis: a vertical chain's point says nothing of an X edge.
        return (partId, axis, end) => flat.Contains(partId) || supports.Any(support =>
            support.PartId == partId && support.Axis == axis && Math.Abs(support.Along - end[axis]) <= tolerance);
    }

    // How many chains each side row offers for placing; each takes one row beyond the outline.
    private static IReadOnlyDictionary<DimensionChainSide, int> SideRowCounts(object[] sideRows) =>
        JsonSerializer.SerializeToElement(sideRows).EnumerateArray()
            .Where(row => Enum.TryParse<DimensionChainSide>(row.GetProperty("side").GetString(), out _))
            .GroupBy(row => (DimensionChainSide)Enum.Parse(typeof(DimensionChainSide), row.GetProperty("side").GetString()!))
            .ToDictionary(side => side.Key, side => side.Sum(row => row.GetProperty("chains").EnumerateArray()
                .Count(chain => !(chain.TryGetProperty("incomplete", out var incomplete) && incomplete.ValueKind == JsonValueKind.True))));

    // The lines bolt chains must not fall on: the rows of the side chains beyond the outline, and
    // the lines of the local and inclined chains, which carry their own offset.
    private List<(double Nx, double Ny, double Line, double From, double To)> OccupiedLines(
        IReadOnlyDictionary<DimensionChainSide, int>? sideRows, double gap)
    {
        var rows = new List<(double Nx, double Ny, double Line, double From, double To)>();
        if (sideRows != null && _group.Extent is { } extent)
            foreach (var side in sideRows)
                for (var row = 1; row <= side.Value; row++)
                {
                    var (nx, ny, bound) = side.Key switch {
                        DimensionChainSide.Top => (0d, 1d, extent.MaxY),
                        DimensionChainSide.Bottom => (0d, -1d, -extent.MinY),
                        DimensionChainSide.Left => (-1d, 0d, -extent.MinX),
                        _ => (1d, 0d, extent.MaxX)
                    };
                    rows.Add((nx, ny, bound + row * gap, double.NegativeInfinity, double.PositiveInfinity));
                }
        // Sections and end views offer no local or inclined row.
        var viewType = _metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
        var ownOffset = viewType is "SectionView" or "EndView" ? [] : InclinedChains().Concat(LocalChains());
        foreach (var chain in ownOffset)
        {
            var normal = chain.Direction.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            var length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1]);
            if (length <= 1e-9 || chain.Points.Length < 6) continue;
            var (nx, ny) = (normal[0] / length, normal[1] / length);
            var along = Enumerable.Range(0, chain.Points.Length / 3)
                .Select(i => chain.Points[i * 3] * -ny + chain.Points[i * 3 + 1] * nx).ToArray();
            rows.Add((nx, ny, chain.Points[0] * nx + chain.Points[1] * ny + chain.Distance, along.Min(), along.Max()));
        }
        return rows;
    }

    // Bolt chains on one side whose lines would fall on one another and whose spans overlap are
    // moved out to one paper gap beyond the line in the way, so a chain whose points stand a little
    // off the outline does not lose a row. Tekla measures the offset from the chain's first point.
    private static double NextFreeRow(List<(double Nx, double Ny, double Line, double From, double To)> rows,
        DimensionRuleResult result, double distance, double gap)
    {
        var normal = ((OutsideOutlineDimensionPlacement)result.Placement).OutwardNormal;
        var first = result.Points[0];
        var along = result.Points.Select(point => point.X * -normal.Y + point.Y * normal.X).ToArray();
        var (from, to) = (along.Min(), along.Max());
        var start = first.X * normal.X + first.Y * normal.Y;
        while (rows.Where(row => Math.Abs(row.Nx - normal.X) < 1e-9 && Math.Abs(row.Ny - normal.Y) < 1e-9
                   // One gap apart is a free row: rounded points must not push a line one row further.
                   && Math.Abs(row.Line - (start + distance)) < gap - DimensionPlacementSettings.VerificationToleranceViewUnits
                   && row.From <= to && from <= row.To)
               .Select(row => (double?)row.Line).Max() is { } inTheWay)
            distance = inTheWay + gap - start;
        double Line(double d) => start + d;
        rows.Add((normal.X, normal.Y, Line(distance), from, to));
        return distance;
    }

    // An incomplete bolt read can hide groups: no bolt chain is created from it.
    private string? BoltReadRefusal()
    {
        if (_bolts.TryGetProperty("isComplete", out var complete) && complete.ValueKind == JsonValueKind.True) return null;
        // A context built without a bolt snapshot has nothing to report; a captured one always lists its groups.
        if (!_bolts.TryGetProperty("boltGroups", out _)) return null;
        var unread = _bolts.TryGetProperty("unread", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.TryGetProperty("partId", out _))
                .Select(item => item.GetProperty("partId").GetInt32().ToString(CultureInfo.InvariantCulture)).ToArray()
            : [];
        var error = _bolts.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return "bolt geometry was read incompletely" + (unread.Length > 0 ? " (unread parts " + string.Join(",", unread) + ")" : "")
            + (error != null ? " (" + error + ")" : "") + "; no bolt chain is created";
    }

    // Parts lying in the view plane, or nearly: the part's own X and Y axes both lie in it.
    // Without readable axes, a part thinner along the view depth than along either view axis.
    private int[] FaceOnPartIds() => _includedModelIds.Where(id => {
        var solid = GetPartSolidGeometrySnapshot(id);
        static double? OutOfPlane(double[] axis)
        {
            if (axis.Length < 3) return null;
            var length = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
            return length <= 1e-9 ? null : Math.Abs(axis[2]) / length;
        }
        if (solid != null && OutOfPlane(solid.AxisX) is { } x && OutOfPlane(solid.AxisY) is { } y)
            return x <= DimensionPlacementSettings.FaceOnAxisOutOfPlaneTolerance
                && y <= DimensionPlacementSettings.FaceOnAxisOutOfPlaneTolerance;
        var points = solid?.Solid.Vertices
            .Where(vertex => vertex.Point.Length >= 3).Select(vertex => vertex.Point).ToArray();
        if (points is not { Length: > 0 }) return false;
        double Extent(int axis) => points.Max(point => point[axis]) - points.Min(point => point[axis]);
        var depth = Extent(2);
        return depth > 0 && depth < Extent(0) && depth < Extent(1);
    }).ToArray();

    // Parts reaching past the main part's end along the view depth (an end plate, a cap).
    // Unread depth says nothing, so such a part is not treated as an end part.
    private int[] EndPartIds(int mainId)
    {
        static (double Min, double Max)? Depth(PartSolidGeometryInViewResult? solid)
        {
            var depths = solid?.Solid.Vertices.Where(vertex => vertex.Point.Length >= 3)
                .Select(vertex => vertex.Point[2]).ToArray();
            return depths is { Length: > 0 } ? (depths.Min(), depths.Max()) : null;
        }
        if (Depth(GetPartSolidGeometrySnapshot(mainId)) is not { } main) return [];
        return _includedModelIds.Where(id => id != mainId
            && Depth(GetPartSolidGeometrySnapshot(id)) is { } part
            && (part.Min < main.Min - DimensionPlacementSettings.EndPartDepthToleranceViewUnits
                || part.Max > main.Max + DimensionPlacementSettings.EndPartDepthToleranceViewUnits)).ToArray();
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

    /// <summary>The obsolete boltChains answer; the Bolt row of the chain preview replaces it.</summary>
    internal JsonElement BuildBoltPreview(IReadOnlyCollection<DimensionChainSide> sides, DimensionCoordinateSettings? coordinateSettings = null)
    {
        var settings = coordinateSettings ?? new DimensionCoordinateSettings();
        var selected = sides.Distinct().OrderBy(side => side).ToArray();
        var key = string.Join(",", selected) + ":" + settings.ToleranceMm.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (!_boltPreviews.TryGetValue(key, out var preview))
        {
            preview = Freeze(BoltDimensionChainPreview.Build(_bolts, _includedModelIds, selected,
                JsonSerializer.Deserialize<Dictionary<int, IReadOnlyList<OutlineTreeNodeResult>>>(_partContours.GetRawText()), settings));
            _boltPreviews.Add(key, preview);
        }
        return preview;
    }

    [Obsolete("Use a key from the Bolt row of the chain preview; boltProposal is kept only for placing one chosen bolt chain.")]
    internal double[] ResolveBoltProposal(string proposalId, int partId, string direction)
    {
        var side = ParseSide(direction);
        var axis = side.RunsAlongX() ? "X" : "Y";
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

    internal double[] ResolvePointIds(IEnumerable<string> pointIds, string direction, bool usePanelReference = true)
    {
        EnsureChains();
        return (usePanelReference ? GetDimensionPointCatalog() : GetPanelDimensionPointCatalog()).Resolve(pointIds, ParseSide(direction));
    }

    private (double X, double Y)[] ReferenceVertices()
    {
        if (_referenceVertices != null) return _referenceVertices;
        var reference = UsableReferenceGroup();
        return _referenceVertices = reference == null || _group.Extent == null ? []
            : ReferenceZoneOutline.VerticesForPanel(reference, _group.Extent);
    }

    private GeometryGroupExtent PanelReferenceExtent() =>
        _panelReferenceExtent ??= _group.Extent!.Include(ReferenceVertices());

    internal bool HasScopedReferenceGeometry => ReferenceVertices().Length > 0;

    private GeometryGroup? UsableReferenceGroup() => _referenceGroup is { Extent: not null }
        && _referenceGroup.Completeness.IsComplete ? _referenceGroup : null;

    // The catalog the steel preview reads; exposed so a test can run the rule with other settings.
    internal DimensionPointCatalog SteelCatalogForTests()
    {
        EnsureChains();
        return GetPanelDimensionPointCatalog();
    }

    private DimensionPointCatalog GetPanelDimensionPointCatalog()
    {
        if (_panelDimensionPointCatalog != null) return _panelDimensionPointCatalog;
        var catalog = DimensionPointCatalog.Build(_group.DimensionChains!);
        return _panelDimensionPointCatalog = ViewFrame() is { } frame
            ? catalog.Within(frame.MinX, frame.MaxX, frame.MinY, frame.MaxY) : catalog;
    }

    // The view's frame on the sheet, in the coordinates of the captured points: [minX, minY,
    // maxX, maxY]. It is the drawn frame, annotations included, so it never cuts off a point the
    // view shows. The restriction box is not used for this: its X and Y limits can be narrower
    // than the drawn view (M.81 section H).
    private (double MinX, double MaxX, double MinY, double MaxY)? ViewFrame()
    {
        if (_metadata.ValueKind != JsonValueKind.Object
            || !_metadata.TryGetProperty("viewFrame", out var frame) || frame.ValueKind != JsonValueKind.Array
            || frame.GetArrayLength() != 4) return null;
        const double tolerance = 0.5;
        return (frame[0].GetDouble() - tolerance, frame[2].GetDouble() + tolerance,
            frame[1].GetDouble() - tolerance, frame[3].GetDouble() + tolerance);
    }

    // A part's thickness: its smallest size along its own axes.
    private IReadOnlyDictionary<int, double> PartThicknesses()
    {
        var result = new Dictionary<int, double>();
        foreach (var id in _includedModelIds)
        {
            var solid = GetPartSolidGeometrySnapshot(id);
            if (solid == null || solid.AxisX.Length < 3 || solid.AxisY.Length < 3) continue;
            double[] Unit(double[] axis)
            {
                var length = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
                return length <= 1e-9 ? [] : [axis[0] / length, axis[1] / length, axis[2] / length];
            }
            var x = Unit(solid.AxisX);
            var y = Unit(solid.AxisY);
            if (x.Length == 0 || y.Length == 0) continue;
            double[] z = [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]];
            var points = solid.Solid.Vertices.Where(vertex => vertex.Point.Length >= 3).Select(vertex => vertex.Point).ToArray();
            if (points.Length == 0) continue;
            double Size(double[] axis)
            {
                var along = points.Select(point => point[0] * axis[0] + point[1] * axis[1] + point[2] * axis[2]).ToArray();
                return along.Max() - along.Min();
            }
            result[id] = new[] { Size(x), Size(y), Size(z) }.Min();
        }
        return result;
    }

    private DimensionPointCatalog GetDimensionPointCatalog()
    {
        if (_dimensionPointCatalog != null) return _dimensionPointCatalog;
        var panel = GetPanelDimensionPointCatalog();
        var reference = UsableReferenceGroup();
        if (reference == null) return _dimensionPointCatalog = panel;
        if (reference.DimensionChains == null) CalcDimensionChains.Apply(reference);
        return _dimensionPointCatalog = panel.WithReference(DimensionPointCatalog.Build(reference.DimensionChains!), _group.Extent!,
            ReferenceVertices()).WithExtentSupports(PanelReferenceExtent());
    }

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
        .OrderBy(g => chain.Side.RunsAlongX() ? g.Key.X : g.Key.Y)
        .ThenBy(g => chain.Side.RunsAlongX() ? g.Key.Y : g.Key.X)
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
            referenceZones = _referenceZone?.Zones, referenceNodes = _referenceZone?.Outline?.AssemblyNodes,
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
        s.AxisAlignedModelExtent == null ? null : side.RunsAlongX()
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
