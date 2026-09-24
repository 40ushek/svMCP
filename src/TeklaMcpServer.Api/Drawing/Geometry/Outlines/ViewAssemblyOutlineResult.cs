using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The Clipper2 outline of a view's visible assembly. <see cref="AssemblyOutline"/> and
/// every value in <see cref="PartOutlines"/> are <see cref="PolyTreeD"/> so components,
/// holes and nesting remain explicit.
/// </summary>
public sealed class ViewAssemblyOutlineResult
{
    public ViewAssemblyOutlineResult(
        int viewId,
        PolyTreeD assemblyOutline,
        IReadOnlyDictionary<int, PolyTreeD> partOutlines,
        IReadOnlyList<UnreadPart> unread,
        string? error = null,
        bool restricted = false,
        int visibleCount = 0,
        IReadOnlyList<int>? requestedIds = null,
        IReadOnlyList<int>? notVisibleRequestedIds = null,
        IReadOnlyList<int>? outsideDepthModelIds = null,
        IReadOnlyList<int>? unresolvedDepthModelIds = null,
        IReadOnlyDictionary<int, PartSolidGeometryInViewResult>? partSolidGeometries = null)
    {
        Restricted = restricted;
        VisibleCount = visibleCount;
        RequestedIds = requestedIds ?? Array.Empty<int>();
        NotVisibleRequestedIds = notVisibleRequestedIds ?? Array.Empty<int>();
        OutsideDepthModelIds = outsideDepthModelIds ?? Array.Empty<int>();
        UnresolvedDepthModelIds = unresolvedDepthModelIds ?? Array.Empty<int>();
        ViewId = viewId;
        AssemblyOutline = assemblyOutline;
        PartOutlines = partOutlines;
        PartSolidGeometries = partSolidGeometries ?? new Dictionary<int, PartSolidGeometryInViewResult>();
        Unread = unread;
        Error = error;
        AssemblyNodes = ToNodes(assemblyOutline);
        PartNodes = partOutlines.ToDictionary(
            part => part.Key,
            part => (IReadOnlyList<OutlineTreeNodeResult>)ToNodes(part.Value));
    }

    public int ViewId { get; }
    public PolyTreeD AssemblyOutline { get; }
    public IReadOnlyDictionary<int, PolyTreeD> PartOutlines { get; }
    internal IReadOnlyDictionary<int, PartSolidGeometryInViewResult> PartSolidGeometries { get; }
    public IReadOnlyList<OutlineTreeNodeResult> AssemblyNodes { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>> PartNodes { get; }
    public IReadOnlyList<UnreadPart> Unread { get; }

    /// <summary>
    /// Whether the caller asked for a subset rather than everything the view draws.
    ///
    /// Recorded because a restricted outline looks exactly like a full one: smaller, with
    /// no sign of why. An extent taken from the frame alone is a different number from the
    /// extent of the sheet, and a reader has to be able to tell which they were given.
    /// </summary>
    public bool Restricted { get; }

    /// <summary>How many parts the view draws, against however many were used.</summary>
    public int VisibleCount { get; }

    /// <summary>The ids the caller asked for, empty when it asked for everything.</summary>
    public IReadOnlyList<int> RequestedIds { get; }

    /// <summary>
    /// Ids the caller asked for that this view does not draw.
    ///
    /// Separate from <see cref="Unread"/>, which is about geometry that failed. These were
    /// dropped before any reading: the part is hidden here, or belongs to another view, or
    /// does not exist. The outline of what remains can be read perfectly and still not be
    /// the outline that was asked for.
    /// </summary>
    public IReadOnlyList<int> NotVisibleRequestedIds { get; }

    /// <summary>
    /// Parts named by the drawing-object enumerator but definitely outside this section/end
    /// view's depth volume. They are distinct from parts that are hidden or absent from
    /// the drawing, and are reported even for an unrestricted outline.
    /// </summary>
    public IReadOnlyList<int> OutsideDepthModelIds { get; }

    /// <summary>
    /// Parts whose depth relation could not be determined safely. Their detailed reason is
    /// carried by <see cref="Unread"/>; this list makes selection incompleteness inspectable.
    /// </summary>
    public IReadOnlyList<int> UnresolvedDepthModelIds { get; }

    /// <summary>
    /// Whether every id asked for made it into the answer with a confirmed depth relation.
    ///
    /// Deliberately not folded into <see cref="IsComplete"/>. That one says the geometry of
    /// the parts used was read in full, which is a different claim: asking for two parts
    /// and getting a clean outline of the one the view draws is a complete read of an
    /// incomplete selection, and reporting it as simply complete would hide a typo, stale
    /// id, depth exclusion, or unresolved depth relation.
    /// </summary>
    public bool SelectionComplete =>
        NotVisibleRequestedIds.Count == 0 &&
        !RequestedIds.Any(OutsideDepthModelIds.Contains) &&
        !RequestedIds.Any(UnresolvedDepthModelIds.Contains);

    /// <summary>Absent view/drawing, distinct from a successfully read empty view.</summary>
    public string? Error { get; }

    /// <summary>
    /// Only a complete result may be used as evidence that an empty area is outside the
    /// assembly. A part with a dropped face is unread here: its partial silhouette is not
    /// an acceptable substitute for its actual contour.
    /// </summary>
    public bool IsComplete => Error == null && Unread.Count == 0;

    private static List<OutlineTreeNodeResult> ToNodes(PolyPathD parent)
    {
        var nodes = new List<OutlineTreeNodeResult>();
        for (var index = 0; index < parent.Count; index++)
        {
            if (parent[index] is not PolyPathD child)
                continue;

            nodes.Add(new OutlineTreeNodeResult
            {
                IsHole = child.IsHole,
                Polygon = child.Polygon?.Select(point => new[] { point.x, point.y }).ToList() ?? [],
                Children = ToNodes(child)
            });
        }

        return nodes;
    }
}
