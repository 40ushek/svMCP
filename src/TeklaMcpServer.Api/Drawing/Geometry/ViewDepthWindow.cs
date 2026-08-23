using System;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// A single, view-local snapshot of the volume Tekla uses to restrict a drawing view.
/// It is deliberately plain numbers: the same snapshot feeds the cache key and the
/// candidate filter, so they cannot silently read different windows.
/// </summary>
internal sealed class ViewDepthWindow
{
    private ViewDepthWindow(DepthBox? box, string? error)
    {
        Box = box;
        Error = error;
    }

    public DepthBox? Box { get; }
    public string? Error { get; }
    public bool IsUsable => Box.HasValue;

    public static ViewDepthWindow Read(Model model, View view)
    {
        var coordinateSystem = view.ViewCoordinateSystem;
        if (DrawingViewPlane.IsModelPlane(coordinateSystem))
            return new ViewDepthWindow(null, DrawingViewPlane.ModelPlaneReason);

        var workPlaneHandler = model.GetWorkPlaneHandler();
        TransformationPlane originalPlane;
        try
        {
            originalPlane = workPlaneHandler.GetCurrentTransformationPlane();
            workPlaneHandler.SetCurrentTransformationPlane(new TransformationPlane(coordinateSystem));
        }
        catch (Exception exception)
        {
            return new ViewDepthWindow(null, $"View coordinate system could not be selected: {exception.Message}");
        }

        try
        {
            view.Select();
            var box = DepthBox.From(view.RestrictionBox);
            return box.IsValid
                ? new ViewDepthWindow(box, null)
                : new ViewDepthWindow(null, "RestrictionBox is degenerate or contains non-finite coordinates");
        }
        catch (Exception exception)
        {
            return new ViewDepthWindow(null, $"RestrictionBox could not be read: {exception.Message}");
        }
        finally
        {
            workPlaneHandler.SetCurrentTransformationPlane(originalPlane);
        }
    }
}

/// <summary>Plain, testable representation of a restriction box in view coordinates.</summary>
internal readonly struct DepthBox
{
    public const double BoundaryTolerance = 0.0001;

    public DepthBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    public bool IsValid =>
        Finite(MinX) && Finite(MinY) && Finite(MinZ) &&
        Finite(MaxX) && Finite(MaxY) && Finite(MaxZ) &&
        MaxX - MinX > BoundaryTolerance &&
        MaxY - MinY > BoundaryTolerance &&
        MaxZ - MinZ > BoundaryTolerance;

    public static DepthBox From(AABB box) => new(
        box.MinPoint.X, box.MinPoint.Y, box.MinPoint.Z,
        box.MaxPoint.X, box.MaxPoint.Y, box.MaxPoint.Z);

    public DepthBoxRelation Classify(DepthBox other)
    {
        if (!IsValid || !other.IsValid)
            return DepthBoxRelation.Invalid;

        if (StrictlySeparated(MinX, MaxX, other.MinX, other.MaxX)
            || StrictlySeparated(MinY, MaxY, other.MinY, other.MaxY)
            || StrictlySeparated(MinZ, MaxZ, other.MinZ, other.MaxZ))
            return DepthBoxRelation.Disjoint;

        if (TouchesBoundary(MinX, MaxX, other.MinX, other.MaxX)
            || TouchesBoundary(MinY, MaxY, other.MinY, other.MaxY)
            || TouchesBoundary(MinZ, MaxZ, other.MinZ, other.MaxZ))
            return DepthBoxRelation.BoundaryTouch;

        return DepthBoxRelation.Overlaps;
    }

    private static bool StrictlySeparated(double minA, double maxA, double minB, double maxB) =>
        maxA < minB - BoundaryTolerance || minA > maxB + BoundaryTolerance;

    private static bool TouchesBoundary(double minA, double maxA, double minB, double maxB) =>
        Math.Abs(maxA - minB) <= BoundaryTolerance || Math.Abs(minA - maxB) <= BoundaryTolerance;

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

internal enum DepthBoxRelation
{
    Invalid,
    Disjoint,
    BoundaryTouch,
    Overlaps
}
