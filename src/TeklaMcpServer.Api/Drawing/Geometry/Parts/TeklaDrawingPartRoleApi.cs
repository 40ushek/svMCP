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
    public PartRoleInView(int modelId, string? partPos, string? partPrefix, PartRoleResult role)
    {
        ModelId = modelId;
        PartPos = partPos;
        PartPrefix = partPrefix;
        Role = role;
    }

    public int ModelId { get; }
    public string? PartPos { get; }
    public string? PartPrefix { get; }
    public PartRoleResult Role { get; }

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
    public PartRoleReadResult(IReadOnlyList<PartRoleInView> roles, IReadOnlyList<UnreadPart> unread)
    {
        Roles = roles;
        Unread = unread;
    }

    public IReadOnlyList<PartRoleInView> Roles { get; }

    /// <summary>Parts the view draws whose properties did not come back.</summary>
    public IReadOnlyList<UnreadPart> Unread { get; }

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

        foreach (var modelId in DrawingViewParts.VisibleModelIds(view))
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
            part.GetReportProperty("MATERIAL", ref material);
            part.GetReportProperty("MATERIAL_TYPE", ref materialType);

            // The prefix is the only input the rules read, so its read is the one that must
            // be checked. Failing it means the role is unknown because nobody could look,
            // which is not the same as no rule covering it - and only the first is fixed by
            // reading the part again.
            if (!part.GetReportProperty("PART_PREFIX", ref partPrefix))
            {
                unread.Add(new UnreadPart(modelId, "PART_PREFIX could not be read"));
                roles.Add(new PartRoleInView(modelId, NullIfEmpty(partPos), null, PartRoleResult.Unclassified));
                continue;
            }

            var role = _classifier.ClassifyProperties(
                partPrefix,
                part.Profile?.ProfileString,
                material,
                materialType,
                part.Name);

            roles.Add(new PartRoleInView(modelId, NullIfEmpty(partPos), NullIfEmpty(partPrefix), role));
        }

        return new PartRoleReadResult(roles, unread);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
