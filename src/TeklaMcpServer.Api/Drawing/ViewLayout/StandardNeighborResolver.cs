using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal static class StandardNeighborResolver
{
    private const double AxisAlignmentThreshold = 0.92;
    private const double PositionDominanceRatio = 1.15;

    public static NeighborSet Build(
        IReadOnlyList<View> views,
        SemanticViewSet semanticViews,
        BaseViewSelectionResult baseSelection)
    {
        var baseView = baseSelection.View
            ?? throw new InvalidOperationException("NeighborSet requires resolved BaseView.");

        var set = new NeighborSet(baseView);
        var candidateList = semanticViews.BaseProjected
            .Where(v => v != baseView)
            .ToList();
        var indexById = views
            .Select((view, index) => (Id: view.GetIdentifier().ID, index))
            .ToDictionary(item => item.Id, item => item.index);
        var byRole = new Dictionary<NeighborRole, List<View>>
        {
            [NeighborRole.Top] = new(),
            [NeighborRole.Bottom] = new(),
            [NeighborRole.SideLeft] = new(),
            [NeighborRole.SideRight] = new()
        };

        foreach (var candidate in candidateList)
        {
            var role = ResolveRole(baseView, candidate);
            if (role == NeighborRole.Unknown)
            {
                set.ResidualProjected.Add(candidate);
                continue;
            }

            byRole[role].Add(candidate);
            set.SetRole(candidate, role);
        }

        set.TopNeighbor = PickBest(baseView, NeighborRole.Top, byRole[NeighborRole.Top], indexById);
        set.BottomNeighbor = PickBest(baseView, NeighborRole.Bottom, byRole[NeighborRole.Bottom], indexById);
        set.SideNeighborLeft = PickBest(baseView, NeighborRole.SideLeft, byRole[NeighborRole.SideLeft], indexById);
        set.SideNeighborRight = PickBest(baseView, NeighborRole.SideRight, byRole[NeighborRole.SideRight], indexById);

        var chosenIds = new HashSet<int>(
            new[]
            {
                set.TopNeighbor,
                set.BottomNeighbor,
                set.SideNeighborLeft,
                set.SideNeighborRight
            }
            .Where(v => v != null)
            .Select(v => v!.GetIdentifier().ID));

        foreach (var candidate in candidateList)
        {
            if (chosenIds.Contains(candidate.GetIdentifier().ID))
                continue;

            if (!set.ResidualProjected.Contains(candidate))
                set.ResidualProjected.Add(candidate);
        }

        return set;
    }

    internal static NeighborRole ResolveRole(View baseView, View candidate)
    {
        var byViewType = ResolveFromViewType(candidate.ViewType);
        if (byViewType != NeighborRole.Unknown)
            return byViewType;

        if (TryGetViewCoordinateSystem(baseView, out var reference)
            && TryGetViewCoordinateSystem(candidate, out var view))
        {
            var byCoordinateSystems = ResolveFromCoordinateSystems(reference, view);
            if (byCoordinateSystems != NeighborRole.Unknown)
                return byCoordinateSystems;
        }

        return ResolveFromCurrentPosition(baseView, candidate);
    }

    internal static NeighborRole ResolveFromCoordinateSystems(CoordinateSystem reference, CoordinateSystem view)
    {
        var viewZ = Normalize(TryCross(view.AxisX, view.AxisY));
        if (viewZ == null)
            return NeighborRole.Unknown;

        var referenceX = Normalize(reference.AxisX);
        var referenceY = Normalize(reference.AxisY);
        if (referenceX == null || referenceY == null)
            return NeighborRole.Unknown;

        var alignedWithReferenceX = Vector.Dot(viewZ, referenceX);
        if (Math.Abs(alignedWithReferenceX) >= AxisAlignmentThreshold)
            return alignedWithReferenceX >= 0 ? NeighborRole.SideLeft : NeighborRole.SideRight;

        var alignedWithReferenceY = Vector.Dot(viewZ, referenceY);
        if (Math.Abs(alignedWithReferenceY) >= AxisAlignmentThreshold)
            return alignedWithReferenceY >= 0 ? NeighborRole.Top : NeighborRole.Bottom;

        var referenceZ = Normalize(TryCross(reference.AxisX, reference.AxisY));
        if (referenceZ == null)
            return NeighborRole.Unknown;

        var alignedWithReferenceZ = Vector.Dot(viewZ, referenceZ);
        if (Math.Abs(alignedWithReferenceZ) >= AxisAlignmentThreshold)
            return alignedWithReferenceZ >= 0 ? NeighborRole.Top : NeighborRole.Bottom;

        return NeighborRole.Unknown;
    }

    private static NeighborRole ResolveFromViewType(View.ViewTypes viewType)
        => viewType switch
        {
            View.ViewTypes.TopView => NeighborRole.Top,
            View.ViewTypes.BottomView => NeighborRole.Bottom,
            View.ViewTypes.BackView => NeighborRole.SideLeft,
            View.ViewTypes.EndView => NeighborRole.SideRight,
            _ => NeighborRole.Unknown
        };

    private static NeighborRole ResolveFromCurrentPosition(View baseView, View candidate)
    {
        if (!DrawingViewFrameGeometry.TryGetCenter(baseView, out var baseX, out var baseY)
            || !DrawingViewFrameGeometry.TryGetCenter(candidate, out var candidateX, out var candidateY))
            return NeighborRole.Unknown;

        var dx = candidateX - baseX;
        var dy = candidateY - baseY;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
            return NeighborRole.Unknown;

        if (Math.Abs(dx) >= Math.Abs(dy) * PositionDominanceRatio)
            return dx < 0 ? NeighborRole.SideLeft : NeighborRole.SideRight;

        if (Math.Abs(dy) >= Math.Abs(dx) * PositionDominanceRatio)
            return dy >= 0 ? NeighborRole.Top : NeighborRole.Bottom;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx < 0 ? NeighborRole.SideLeft : NeighborRole.SideRight)
            : (dy >= 0 ? NeighborRole.Top : NeighborRole.Bottom);
    }

    private static View? PickBest(
        View baseView,
        NeighborRole role,
        IReadOnlyList<View> candidates,
        IReadOnlyDictionary<int, int> indexById)
    {
        if (candidates.Count == 0)
            return null;

        DrawingViewFrameGeometry.TryGetCenter(baseView, out var baseCenterX, out var baseCenterY);

        return candidates
            .OrderBy(v => GetTypePriority(role, v.ViewType))
            .ThenByDescending(v => Math.Max(v.Width, 0) * Math.Max(v.Height, 0))
            .ThenBy(v => GetCrossAxisDistance(baseCenterX, baseCenterY, v, role))
            .ThenBy(v => GetPrimaryAxisDistance(baseCenterX, baseCenterY, v, role))
            .ThenBy(v => indexById.TryGetValue(v.GetIdentifier().ID, out var index) ? index : int.MaxValue)
            .First();
    }

    private static int GetTypePriority(NeighborRole role, View.ViewTypes viewType)
        => role switch
        {
            NeighborRole.Top => viewType == View.ViewTypes.TopView ? 0 : 1,
            NeighborRole.Bottom => viewType == View.ViewTypes.BottomView ? 0 : 1,
            NeighborRole.SideLeft => viewType switch
            {
                View.ViewTypes.BackView => 0,
                View.ViewTypes.EndView => 1,
                _ => 2
            },
            NeighborRole.SideRight => viewType switch
            {
                View.ViewTypes.EndView => 0,
                View.ViewTypes.BackView => 1,
                _ => 2
            },
            _ => 3
        };

    private static double GetCrossAxisDistance(double baseCenterX, double baseCenterY, View view, NeighborRole role)
    {
        if (!DrawingViewFrameGeometry.TryGetCenter(view, out var x, out var y))
            return double.MaxValue;

        return role switch
        {
            NeighborRole.Top or NeighborRole.Bottom => Math.Abs(x - baseCenterX),
            NeighborRole.SideLeft or NeighborRole.SideRight => Math.Abs(y - baseCenterY),
            _ => double.MaxValue
        };
    }

    private static double GetPrimaryAxisDistance(double baseCenterX, double baseCenterY, View view, NeighborRole role)
    {
        if (!DrawingViewFrameGeometry.TryGetCenter(view, out var x, out var y))
            return double.MaxValue;

        return role switch
        {
            NeighborRole.Top or NeighborRole.Bottom => Math.Abs(y - baseCenterY),
            NeighborRole.SideLeft or NeighborRole.SideRight => Math.Abs(x - baseCenterX),
            _ => double.MaxValue
        };
    }

    private static bool TryGetViewCoordinateSystem(View view, out CoordinateSystem coordinateSystem)
    {
        coordinateSystem = null!;

        try
        {
            if (view.DisplayCoordinateSystem != null)
            {
                coordinateSystem = view.DisplayCoordinateSystem;
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (view.ViewCoordinateSystem != null)
            {
                coordinateSystem = view.ViewCoordinateSystem;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static Vector? TryCross(Vector? axisX, Vector? axisY)
    {
        if (axisX == null || axisY == null)
            return null;

        return axisX.Cross(axisY);
    }

    private static Vector? Normalize(Vector? vector)
    {
        if (vector == null)
            return null;

        var lengthSquared = (vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z);
        if (lengthSquared <= 1e-12)
            return null;

        return vector.GetNormal();
    }
}

