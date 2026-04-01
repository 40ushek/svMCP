using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelAssembly = Tekla.Structures.Model.Assembly;
using ModelPart = Tekla.Structures.Model.Part;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum SectionPlacementSide
{
    Unknown,
    Left,
    Right,
    Top,
    Bottom
}

internal sealed class SectionPlacementSideResult
{
    public SectionPlacementSide PlacementSide { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}

internal sealed class SectionPlacementSideResolver
{
    private const double AxisAlignmentThreshold = 0.92;
    private readonly Model _model;

    public SectionPlacementSideResolver(Model? model = null)
    {
        _model = model ?? new Model();
    }

    public SectionPlacementSideResult Resolve(Tekla.Structures.Drawing.Drawing drawing, View baseView, View sectionView)
    {
        if (TryResolveFromCoordinateSystems(drawing, baseView, sectionView, out var fromCoordinateSystems))
            return fromCoordinateSystems;

        var heuristic = ResolveFromGeometryHeuristic(sectionView);
        if (heuristic.PlacementSide != SectionPlacementSide.Unknown)
            return heuristic;

        return new SectionPlacementSideResult
        {
            PlacementSide = SectionPlacementSide.Unknown,
            Reason = "section-placement-side-unresolved",
            IsFallback = false
        };
    }

    internal bool TryGetDebugCoordinateSystems(
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        View sectionView,
        out CoordinateSystem reference,
        out CoordinateSystem view,
        out string referenceReason,
        out string viewReason)
    {
        reference = null!;
        view = null!;
        referenceReason = string.Empty;
        viewReason = string.Empty;

        if (!TryGetReferenceCoordinateSystem(drawing, baseView, out reference, out referenceReason))
            return false;

        if (!TryGetViewCoordinateSystem(sectionView, out view, out viewReason))
            return false;

        return true;
    }

    internal static SectionPlacementSide ResolveFromCoordinateSystems(CoordinateSystem reference, CoordinateSystem view)
    {
        var viewZ = Normalize(TryCross(view.AxisX, view.AxisY));
        if (viewZ == null)
            return SectionPlacementSide.Unknown;

        var referenceX = Normalize(reference.AxisX);
        var referenceY = Normalize(reference.AxisY);
        if (referenceX == null || referenceY == null)
            return SectionPlacementSide.Unknown;

        var alignedWithReferenceX = Vector.Dot(viewZ, referenceX);
        if (Math.Abs(alignedWithReferenceX) >= AxisAlignmentThreshold)
            return alignedWithReferenceX >= 0 ? SectionPlacementSide.Left : SectionPlacementSide.Right;

        var alignedWithReferenceY = Vector.Dot(viewZ, referenceY);
        if (Math.Abs(alignedWithReferenceY) >= AxisAlignmentThreshold)
            return alignedWithReferenceY >= 0 ? SectionPlacementSide.Top : SectionPlacementSide.Bottom;

        var referenceZ = Normalize(TryCross(reference.AxisX, reference.AxisY));
        if (referenceZ == null)
            return SectionPlacementSide.Unknown;

        var alignedWithReferenceZ = Vector.Dot(viewZ, referenceZ);
        if (Math.Abs(alignedWithReferenceZ) >= AxisAlignmentThreshold)
            return alignedWithReferenceZ >= 0 ? SectionPlacementSide.Top : SectionPlacementSide.Bottom;

        return SectionPlacementSide.Unknown;
    }

    private bool TryResolveFromCoordinateSystems(
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        View sectionView,
        out SectionPlacementSideResult result)
    {
        result = new SectionPlacementSideResult();

        if (!TryGetReferenceCoordinateSystem(drawing, baseView, out var reference, out var referenceReason))
            return false;

        if (!TryGetViewCoordinateSystem(sectionView, out var section, out var sectionReason))
            return false;

        var placementSide = ResolveFromCoordinateSystems(reference, section);
        if (placementSide == SectionPlacementSide.Unknown)
            return false;

        result = new SectionPlacementSideResult
        {
            PlacementSide = placementSide,
            Reason = $"{referenceReason}+{sectionReason}",
            IsFallback = false
        };
        return true;
    }

    private SectionPlacementSideResult ResolveFromGeometryHeuristic(View sectionView)
    {
        var width = Math.Max(sectionView.Width, 0);
        var height = Math.Max(sectionView.Height, 0);
        if (width <= 0 || height <= 0)
        {
            return new SectionPlacementSideResult
            {
                PlacementSide = SectionPlacementSide.Unknown,
                Reason = "geometry-heuristic-missing-size",
                IsFallback = true
            };
        }

        const double ratioThreshold = 1.15;
        if (width >= height * ratioThreshold)
        {
            return new SectionPlacementSideResult
            {
                PlacementSide = SectionPlacementSide.Top,
                Reason = "geometry-heuristic-wide->top",
                IsFallback = true
            };
        }

        if (height >= width * ratioThreshold)
        {
            return new SectionPlacementSideResult
            {
                PlacementSide = SectionPlacementSide.Right,
                Reason = "geometry-heuristic-tall->right",
                IsFallback = true
            };
        }

        return new SectionPlacementSideResult
        {
            PlacementSide = SectionPlacementSide.Unknown,
            Reason = "geometry-heuristic-ambiguous",
            IsFallback = true
        };
    }

    private bool TryGetReferenceCoordinateSystem(
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        out CoordinateSystem coordinateSystem,
        out string reason)
    {
        coordinateSystem = null!;
        reason = string.Empty;

        switch (drawing)
        {
            case SinglePartDrawing singlePartDrawing:
                if (TryGetModelCoordinateSystem(singlePartDrawing.PartIdentifier, out coordinateSystem))
                {
                    reason = "reference-cs:single-part-model";
                    return true;
                }
                break;

            case AssemblyDrawing assemblyDrawing:
                if (TryGetAssemblyMainPartCoordinateSystem(assemblyDrawing, out coordinateSystem))
                {
                    reason = "reference-cs:assembly-main-part";
                    return true;
                }
                break;

            case GADrawing:
                if (TryGetViewCoordinateSystem(baseView, out coordinateSystem, out _))
                {
                    reason = "reference-cs:ga-base-view";
                    return true;
                }
                break;
        }

        if (TryGetViewCoordinateSystem(baseView, out coordinateSystem, out _))
        {
            reason = "reference-cs:base-view-fallback";
            return true;
        }

        return false;
    }

    private bool TryGetAssemblyMainPartCoordinateSystem(AssemblyDrawing drawing, out CoordinateSystem coordinateSystem)
    {
        coordinateSystem = null!;

        var assemblyIdentifier = drawing.AssemblyIdentifier;
        if (assemblyIdentifier == null || (assemblyIdentifier.ID == 0 && assemblyIdentifier.GUID == Guid.Empty))
            return false;

        if (_model.SelectModelObject(assemblyIdentifier) is not ModelAssembly assembly)
            return false;

        if (assembly.GetMainPart() is not ModelPart mainPart)
            return false;

        coordinateSystem = mainPart.GetCoordinateSystem();
        return coordinateSystem != null;
    }

    private bool TryGetModelCoordinateSystem(Identifier identifier, out CoordinateSystem coordinateSystem)
    {
        coordinateSystem = null!;
        if (identifier == null || (identifier.ID == 0 && identifier.GUID == Guid.Empty))
            return false;

        var modelObject = _model.SelectModelObject(identifier);
        if (modelObject == null)
            return false;

        if (modelObject is ModelAssembly assembly && assembly.GetMainPart() is ModelPart mainPart)
            coordinateSystem = mainPart.GetCoordinateSystem();
        else
            coordinateSystem = modelObject.GetCoordinateSystem();

        return coordinateSystem != null;
    }

    private static bool TryGetViewCoordinateSystem(View view, out CoordinateSystem coordinateSystem, out string reason)
    {
        coordinateSystem = null!;
        reason = string.Empty;

        try
        {
            if (view.DisplayCoordinateSystem != null)
            {
                coordinateSystem = view.DisplayCoordinateSystem;
                reason = "view-cs:display";
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
                reason = "view-cs:view";
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

        var cross = axisX.Cross(axisY);
        return cross;
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

