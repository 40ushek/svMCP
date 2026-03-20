using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using ModelAssembly = Tekla.Structures.Model.Assembly;
using ModelPart = Tekla.Structures.Model.Part;

namespace TeklaMcpServer.Api.Drawing;

internal enum CutOrientation
{
    Unknown,
    Horizontal,
    Vertical
}

internal sealed class CutOrientationResult
{
    public CutOrientation Orientation { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}

internal sealed class CutOrientationResolver
{
    private const double AxisAlignmentThreshold = 0.92;
    private readonly Model _model;

    public CutOrientationResolver(Model? model = null)
    {
        _model = model ?? new Model();
    }

    public CutOrientationResult Resolve(Tekla.Structures.Drawing.Drawing drawing, View baseView, View sectionView)
    {
        if (TryResolveFromCoordinateSystems(drawing, baseView, sectionView, out var fromCoordinateSystems))
            return fromCoordinateSystems;

        var heuristic = ResolveFromGeometryHeuristic(sectionView);
        if (heuristic.Orientation != CutOrientation.Unknown)
            return heuristic;

        return new CutOrientationResult
        {
            Orientation = CutOrientation.Unknown,
            Reason = "cut-orientation-unresolved",
            IsFallback = false
        };
    }

    internal static CutOrientation ResolveFromCoordinateSystems(CoordinateSystem reference, CoordinateSystem view)
    {
        var referenceZ = Normalize(TryCross(reference.AxisX, reference.AxisY));
        var viewZ = Normalize(TryCross(view.AxisX, view.AxisY));
        if (referenceZ == null || viewZ == null)
            return CutOrientation.Unknown;

        var alignedWithReferenceZ = Math.Abs(Vector.Dot(viewZ, referenceZ));
        if (alignedWithReferenceZ >= AxisAlignmentThreshold)
            return CutOrientation.Horizontal;

        var referenceX = Normalize(reference.AxisX);
        var referenceY = Normalize(reference.AxisY);
        if (referenceX == null || referenceY == null)
            return CutOrientation.Unknown;

        var alignedWithReferenceX = Math.Abs(Vector.Dot(viewZ, referenceX));
        var alignedWithReferenceY = Math.Abs(Vector.Dot(viewZ, referenceY));
        if (Math.Max(alignedWithReferenceX, alignedWithReferenceY) >= AxisAlignmentThreshold)
            return CutOrientation.Vertical;

        return CutOrientation.Unknown;
    }

    private bool TryResolveFromCoordinateSystems(
        Tekla.Structures.Drawing.Drawing drawing,
        View baseView,
        View sectionView,
        out CutOrientationResult result)
    {
        result = new CutOrientationResult();

        if (!TryGetReferenceCoordinateSystem(drawing, baseView, out var reference, out var referenceReason))
            return false;

        if (!TryGetViewCoordinateSystem(sectionView, out var section, out var sectionReason))
            return false;

        var orientation = ResolveFromCoordinateSystems(reference, section);
        if (orientation == CutOrientation.Unknown)
            return false;

        result = new CutOrientationResult
        {
            Orientation = orientation,
            Reason = $"{referenceReason}+{sectionReason}",
            IsFallback = false
        };
        return true;
    }

    private CutOrientationResult ResolveFromGeometryHeuristic(View sectionView)
    {
        var width = Math.Max(sectionView.Width, 0);
        var height = Math.Max(sectionView.Height, 0);
        if (width <= 0 || height <= 0)
        {
            return new CutOrientationResult
            {
                Orientation = CutOrientation.Unknown,
                Reason = "geometry-heuristic-missing-size",
                IsFallback = true
            };
        }

        const double ratioThreshold = 1.15;
        if (width >= height * ratioThreshold)
        {
            return new CutOrientationResult
            {
                Orientation = CutOrientation.Horizontal,
                Reason = "geometry-heuristic-wide",
                IsFallback = true
            };
        }

        if (height >= width * ratioThreshold)
        {
            return new CutOrientationResult
            {
                Orientation = CutOrientation.Vertical,
                Reason = "geometry-heuristic-tall",
                IsFallback = true
            };
        }

        return new CutOrientationResult
        {
            Orientation = CutOrientation.Unknown,
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
