using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

/// <summary>
/// Контур детали через сечение её solid плоскостью (Solid.IntersectAllFaces).
/// В отличие от Contour.ContourPoints / polycurve, учитывает булевы обрезки (cut),
/// т.к. секётся фактическая геометрия solid'а.
///
/// Возвращает точки в координатах модели (world). Преобразование в координаты вида —
/// ответственность вызывающего кода (через WorkPlaneHandler / TransformationPlane).
///
/// Эталон-источник паттерна: sv_tekla_lib ContourPlateUtils.GetContourPlatePolygons.
/// </summary>
public static class SolidSectionContourHelper
{
    /// <summary>
    /// Внешний контур детали в плоскости её системы координат (по центру толщины + zOffset).
    /// </summary>
    /// <param name="part">Деталь.</param>
    /// <param name="zOffset">Смещение секущей плоскости вдоль нормали детали (мм). 0 = центр.</param>
    /// <param name="solidType">Тип solid; для учёта булен использовать NORMAL/HIGH_ACCURACY.</param>
    /// <returns>Внешний контур (model-CS). Пустой список, если сечение не получено.</returns>
    public static List<Point> GetSectionContour(
        Part part,
        double zOffset = 0,
        Solid.SolidCreationTypeEnum solidType = Solid.SolidCreationTypeEnum.NORMAL)
    {
        var (contour, _) = GetSectionPolygons(part, zOffset, solidType);
        return contour;
    }

    /// <summary>
    /// Внешний контур + внутренние полигоны (отверстия) детали в плоскости её СК.
    /// </summary>
    public static (List<Point> contour, List<List<Point>> openings) GetSectionPolygons(
        Part part,
        double zOffset = 0,
        Solid.SolidCreationTypeEnum solidType = Solid.SolidCreationTypeEnum.NORMAL)
    {
        if (part == null)
            throw new ArgumentNullException(nameof(part));

        var solid = part.GetSolid(solidType);
        if (solid == null)
            return (new List<Point>(), new List<List<Point>>());

        // Секущая плоскость = система координат детали, смещённая по нормали на zOffset.
        var plane = new GeometricPlane(part.GetCoordinateSystem());
        var normal = new Vector(plane.Normal);
        normal.Normalize(zOffset);
        plane.Origin += normal;

        Get3PointsOnPlane(plane, out var p1, out var p2, out var p3);

        return IntersectToPolygons(solid, p1, p2, p3);
    }

    /// <summary>
    /// Низкоуровневый вызов: сечение solid плоскостью, заданной тремя точками.
    /// Первый полигон трактуется как внешний контур, остальные — как отверстия.
    /// </summary>
    public static (List<Point> contour, List<List<Point>> openings) IntersectToPolygons(
        Solid solid, Point p1, Point p2, Point p3)
    {
        if (solid == null)
            throw new ArgumentNullException(nameof(solid));

        var faceEnum = solid.IntersectAllFaces(p1, p2, p3);

        // IntersectAllFaces возвращает enumerator коллекций; каждая коллекция — набор
        // полигонов одной грани сечения; каждый полигон — коллекция Point.
        var sections = ToEnumerable<ICollection>(faceEnum).ToList();
        if (sections.Count == 0)
            return (new List<Point>(), new List<List<Point>>());

        var polygons = ToEnumerable<ICollection>(sections[0].GetEnumerator())
            .Select(pts => pts.OfType<Point>().ToList())
            .Where(pts => pts.Count >= 3)
            .ToList();

        if (polygons.Count == 0)
            return (new List<Point>(), new List<List<Point>>());

        var contour = polygons[0];
        var openings = polygons.Skip(1).ToList();
        return (contour, openings);
    }

    // Три точки на плоскости (для IntersectAllFaces). Аналог sv_tekla_lib Utils.Get3PointOnGeomPlane.
    private static void Get3PointsOnPlane(GeometricPlane plane, out Point p1, out Point p2, out Point p3)
    {
        p1 = new Point(plane.Origin);

        // Любой вектор, не параллельный нормали, спроецированный на плоскость -> ось X плоскости.
        var normal = new Vector(plane.Normal);
        normal.Normalize();
        var seed = Math.Abs(normal.X) < 0.9 ? new Vector(1, 0, 0) : new Vector(0, 1, 0);
        var d = seed.Dot(normal);
        var x = new Vector(seed.X - normal.X * d, seed.Y - normal.Y * d, seed.Z - normal.Z * d);
        x.Normalize(1000);
        var y = Vector.Cross(plane.Normal, x);
        y.Normalize(1000);

        p2 = new Point(p1.X + x.X, p1.Y + x.Y, p1.Z + x.Z);
        p3 = new Point(p1.X + y.X, p1.Y + y.Y, p1.Z + y.Z);
    }

    private static IEnumerable<T> ToEnumerable<T>(IEnumerator enumerator)
    {
        while (enumerator.MoveNext())
            yield return (T)enumerator.Current;
    }
}
