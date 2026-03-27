using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DetailRelation
{
    public View DetailView  { get; set; } = null!;
    public View OwnerView   { get; set; } = null!;
    public double? AnchorX  { get; set; }
    public double? AnchorY  { get; set; }
}

internal sealed class DetailRelationSet
{
    private readonly Dictionary<int, DetailRelation> _byDetailId;

    internal DetailRelationSet(Dictionary<int, DetailRelation> dict) => _byDetailId = dict;

    public bool TryGet(int detailViewId, out DetailRelation relation)
        => _byDetailId.TryGetValue(detailViewId, out relation!);

    public int Count => _byDetailId.Count;

    public IEnumerable<DetailRelation> All => _byDetailId.Values;

    public static DetailRelationSet Build(
        IEnumerable<View> allViews,
        IEnumerable<View> detailViews)
        => DetailRelationResolver.Build(allViews, detailViews);
}

internal static class DetailRelationResolver
{
    public static DetailRelationSet Build(
        IEnumerable<View> allViews,
        IEnumerable<View> detailViews)
    {
        var detailList = new List<View>(detailViews);
        if (detailList.Count == 0)
            return new DetailRelationSet(new Dictionary<int, DetailRelation>());

        var detailById = new Dictionary<int, View>();
        foreach (var v in detailList)
            detailById[v.GetIdentifier().ID] = v;

        var dict = new Dictionary<int, DetailRelation>();
        var seen = new HashSet<int>();

        foreach (var ownerView in allViews)
        {
            // DetailMark -> real DetailView
            DrawingObjectEnumerator? detailMarks = null;
            try
            {
                detailMarks = ownerView.GetAllObjects(typeof(DetailMark));
            }
            catch
            {
            }

            while (detailMarks != null && detailMarks.MoveNext())
            {
                if (detailMarks.Current is not DetailMark mark) continue;
                DrawingObjectEnumerator? related = null;
                try
                {
                    related = mark.GetRelatedObjects();
                }
                catch
                {
                }

                while (related != null && related.MoveNext())
                {
                    if (related.Current is not View rv) continue;
                    var id = rv.GetIdentifier().ID;
                    if (!detailById.TryGetValue(id, out var detailView))
                        continue;
                    if (!seen.Add(id))
                        continue;

                    dict[id] = new DetailRelation
                    {
                        DetailView = detailView,
                        OwnerView  = ownerView,
                        AnchorX    = ResolveDetailMarkAnchorX(ownerView, mark),
                        AnchorY    = ResolveDetailMarkAnchorY(ownerView, mark),
                    };
                    break;
                }
            }

            // SectionMark -> detail-like SectionView
            DrawingObjectEnumerator? sectionMarks = null;
            try
            {
                sectionMarks = ownerView.GetAllObjects(typeof(SectionMark));
            }
            catch
            {
            }

            while (sectionMarks != null && sectionMarks.MoveNext())
            {
                if (sectionMarks.Current is not SectionMark sm) continue;
                DrawingObjectEnumerator? related = null;
                try
                {
                    related = sm.GetRelatedObjects();
                }
                catch
                {
                }

                while (related != null && related.MoveNext())
                {
                    if (related.Current is not View rv) continue;
                    var id = rv.GetIdentifier().ID;
                    if (!detailById.TryGetValue(id, out var detailView))
                        continue;
                    if (!seen.Add(id))
                        continue;

                    var mid = TrySectionMarkMidPoint(sm);
                    double? ax = null, ay = null;
                    if (mid != null && TryProjectToSheet(ownerView, mid, out var px, out var py))
                    {
                        ax = px; ay = py;
                    }
                    dict[id] = new DetailRelation
                    {
                        DetailView = detailView,
                        OwnerView  = ownerView,
                        AnchorX    = ax,
                        AnchorY    = ay,
                    };
                    break;
                }
            }
        }

        return new DetailRelationSet(dict);
    }

    // --- anchor helpers ---

    private static double? ResolveDetailMarkAnchorX(View owner, DetailMark mark)
    {
        if (mark.LabelPoint != null && TryProjectToSheet(owner, mark.LabelPoint, out var x, out _)) return x;
        if (mark.BoundaryPoint != null && TryProjectToSheet(owner, mark.BoundaryPoint, out x, out _)) return x;
        if (mark.CenterPoint != null && TryProjectToSheet(owner, mark.CenterPoint, out x, out _)) return x;
        return null;
    }

    private static double? ResolveDetailMarkAnchorY(View owner, DetailMark mark)
    {
        if (mark.LabelPoint != null && TryProjectToSheet(owner, mark.LabelPoint, out _, out var y)) return y;
        if (mark.BoundaryPoint != null && TryProjectToSheet(owner, mark.BoundaryPoint, out _, out y)) return y;
        if (mark.CenterPoint != null && TryProjectToSheet(owner, mark.CenterPoint, out _, out y)) return y;
        return null;
    }

    private static Point? TrySectionMarkMidPoint(SectionMark sm)
    {
        try
        {
            var lp = sm.LeftPoint;
            var rp = sm.RightPoint;
            if (lp != null && rp != null)
                return new Point((lp.X + rp.X) * 0.5, (lp.Y + rp.Y) * 0.5, 0);
            return lp ?? rp;
        }
        catch { return null; }
    }

    internal static bool TryProjectToSheet(View view, Point localPoint, out double sheetX, out double sheetY)
    {
        sheetX = sheetY = 0;
        if (view.Origin == null || localPoint == null) return false;
        var scale = view.Attributes.Scale > 0 ? view.Attributes.Scale : 1.0;
        sheetX = view.Origin.X + localPoint.X / scale;
        sheetY = view.Origin.Y + localPoint.Y / scale;
        return true;
    }
}
