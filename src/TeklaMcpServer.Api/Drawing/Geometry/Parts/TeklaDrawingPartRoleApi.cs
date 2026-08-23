using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>What each part a view draws is for, without reading any geometry.</summary>
public interface IDrawingPartRoleApi
{
    PartRoleReadResult GetRolesInView(int viewId);
}

/// <summary>A part of a view and the role its properties give it. No geometry.</summary>
public sealed class PartRoleInView
{
    public PartRoleInView(
        int modelId,
        string? partPos,
        string? partPrefix,
        PartRoleResult role,
        bool isMainPart = false,
        bool isMainPartKnown = true)
    {
        ModelId = modelId;
        PartPos = partPos;
        PartPrefix = partPrefix;
        Role = role;
        IsMainPart = isMainPart;
        IsMainPartKnown = isMainPartKnown;
    }

    public int ModelId { get; }
    public string? PartPos { get; }
    public string? PartPrefix { get; }
    public PartRoleResult Role { get; }

    /// <summary>
    /// Whether Tekla reports this part as the main part of its assembly.
    ///
    /// A fact, not a judgement, and deliberately outside <see cref="PartRoleResult"/>: it
    /// decides nothing here. On a beam or a column it is the member everything else is
    /// fixed to, and the base a secondary part is measured from; on a panel it is one of
    /// many equal members and means nothing at all. Which of the two applies is the rule
    /// set's business, so this is reported and never acted on in this layer.
    /// </summary>
    public bool IsMainPart { get; }

    /// <summary>
    /// Whether the assembly could be asked at all.
    ///
    /// False means the read threw, and then <see cref="IsMainPart"/> is not "no" but "not
    /// known" - the two carry the same value and only this flag separates them. A rule set
    /// that measures from the main part has to refuse the drawing here rather than measure
    /// from whatever is left, which is what a swallowed exception would have let it do.
    /// </summary>
    public bool IsMainPartKnown { get; }

    public override string ToString() => $"{PartPos ?? ModelId.ToString()} {Role}";
}

/// <summary>
/// The roles read from a view, and the parts whose properties could not be read at all.
///
/// The second list is not decoration. A part that vanishes from the first one takes its
/// extent with it, and an outline over what remains comes back clean and short - which is
/// the failure this whole area keeps producing, because a short overall looks exactly like
/// a correct one.
/// </summary>
public sealed class PartRoleReadResult
{
    public PartRoleReadResult(
        IReadOnlyList<PartRoleInView> roles,
        IReadOnlyList<UnreadPart> unread,
        IReadOnlyList<int>? outsideDepthModelIds = null)
    {
        Roles = roles;
        Unread = unread;
        OutsideDepthModelIds = outsideDepthModelIds ?? Array.Empty<int>();
    }

    public IReadOnlyList<PartRoleInView> Roles { get; }

    /// <summary>Parts the view draws whose properties did not come back.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>
    /// Parts a section/end view named but whose solid was confirmed outside the view's
    /// depth window - a definite answer, not a read failure, so it does not affect
    /// <see cref="IsComplete"/>. Recorded so this confirmed exclusion stays visible instead
    /// of looking identical to a candidate that was never named at all.
    /// </summary>
    public IReadOnlyList<int> OutsideDepthModelIds { get; }

    public bool IsComplete => Unread.Count == 0;
}

/// <summary>
/// Reads only what a role is decided from: the mark, the prefix, the profile, the material.
///
/// Separate from the geometry reader on purpose. A role is decided from properties, and
/// reading every part's solid in order to discard most of them costs the whole point of
/// having roles - on a wall that is the insulation and the fixings read in full so they can
/// be left out. The outline then reads solids once, for the parts that survived.
/// </summary>
public sealed class TeklaDrawingPartRoleApi : IDrawingPartRoleApi
{
    private readonly Model _model;
    private readonly PartRoleClassifier _classifier;

    public TeklaDrawingPartRoleApi(Model model, PartRoleClassifier? classifier = null)
    {
        _model = model;
        _classifier = classifier ?? new PartRoleClassifier();
    }

    public PartRoleReadResult GetRolesInView(int viewId)
    {
        var drawing = new DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            return new PartRoleReadResult([], [new UnreadPart(0, "no drawing is open")]);

        var view = DrawingViewParts.FindView(drawing, viewId);
        if (view == null)
            return new PartRoleReadResult([], [new UnreadPart(0, $"view {viewId} is not on the active drawing")]);

        var roles = new List<PartRoleInView>();
        var unread = new List<UnreadPart>();
        var mainPartByAssembly = new Dictionary<int, int>();
        var selected = DrawingViewParts.GetDepthFilteredParts(_model, view);
        unread.AddRange(selected.Incomplete);

        foreach (var modelId in selected.ModelIds)
        {
            if (_model.SelectModelObject(new Identifier(modelId)) is not ModelPart part)
            {
                // Named by the view and not in the model. Skipping it silently would drop a
                // part out of the extent with nothing to show for it.
                unread.Add(new UnreadPart(modelId, "the view draws it but the model does not have it as a part"));
                continue;
            }

            // Report properties come back empty unless the part is selected first. Without
            // this the prefix reads as blank and every part in the view looks like one no
            // rule covers - a whole drawing quietly reclassified.
            part.Select();

            string partPos = string.Empty, partPrefix = string.Empty, material = string.Empty;
            var materialType = -1;

            part.GetReportProperty("PART_POS", ref partPos);
            part.GetReportProperty("MATERIAL_TYPE", ref materialType);

            // Both are read whatever the filter says, because both go into the reason a
            // person reads before writing an exclusion. Only the ones an exclusion actually
            // consults are required: refusing a part because Tekla would not hand over a
            // property nobody asked about is the blocker this redesign removed.
            var prefixRead = part.GetReportProperty("PART_PREFIX", ref partPrefix);
            var materialRead = part.GetReportProperty("MATERIAL", ref material);

            var missing = new List<string>();
            if (_classifier.NeedsPrefix && !prefixRead) missing.Add("PART_PREFIX");
            if (_classifier.NeedsMaterial && !materialRead) missing.Add("MATERIAL");

            var mainPart = IsMainPart(part, mainPartByAssembly, out var mainPartKnown);

            // Fail-closed, and this is the whole point of the check. A material read that
            // came back empty would leave excludeMaterials matching nothing while the
            // answer still called itself complete - the window stays in the extent and
            // nobody is told.
            if (missing.Count > 0)
            {
                unread.Add(new UnreadPart(modelId, string.Join(" and ", missing) + " could not be read"));
                roles.Add(new PartRoleInView(
                    modelId,
                    NullIfEmpty(partPos),
                    prefixRead ? NullIfEmpty(partPrefix) : null,
                    PartRoleResult.Unclassified,
                    mainPart,
                    mainPartKnown));
                continue;
            }

            var role = _classifier.ClassifyProperties(
                partPrefix,
                part.Profile?.ProfileString,
                material,
                materialType,
                part.Name);

            roles.Add(new PartRoleInView(
                modelId,
                NullIfEmpty(partPos),
                NullIfEmpty(partPrefix),
                role,
                mainPart,
                mainPartKnown));
        }

        return new PartRoleReadResult(roles, unread, selected.OutsideDepthModelIds);
    }

    /// <summary>
    /// Asks the assembly, once per assembly rather than once per part: the answer is the
    /// same for every part of one assembly, and each read costs a round trip to Tekla.
    ///
    /// A part with no assembly, or one whose assembly names another part, is simply not the
    /// main part and <paramref name="known"/> stays true. A read that throws is a different
    /// answer and says so: "false" would be indistinguishable from "no", and a rule set that
    /// measures from the main part would then measure from nothing while the drawing still
    /// looked complete.
    /// </summary>
    private static bool IsMainPart(ModelPart part, IDictionary<int, int> mainPartByAssembly, out bool known)
    {
        known = true;

        try
        {
            var assembly = part.GetAssembly();
            if (assembly == null)
                return false;

            var assemblyId = assembly.Identifier.ID;
            if (!mainPartByAssembly.TryGetValue(assemblyId, out var mainPartId))
            {
                mainPartId = assembly.GetMainPart() is ModelPart main ? main.Identifier.ID : 0;
                mainPartByAssembly[assemblyId] = mainPartId;
            }

            return mainPartId != 0 && mainPartId == part.Identifier.ID;
        }
        catch (Exception)
        {
            known = false;
            return false;
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
