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
        string? error = null)
    {
        ViewId = viewId;
        AssemblyOutline = assemblyOutline;
        PartOutlines = partOutlines;
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
    public IReadOnlyList<OutlineTreeNodeResult> AssemblyNodes { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<OutlineTreeNodeResult>> PartNodes { get; }
    public IReadOnlyList<UnreadPart> Unread { get; }

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
