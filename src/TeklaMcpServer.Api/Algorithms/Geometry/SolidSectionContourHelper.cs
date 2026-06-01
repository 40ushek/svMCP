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
    /// Контур детали в плоскости, ПАРАЛЛЕЛЬНОЙ текущей рабочей плоскости (XY), на глубине
    /// Z = центр solid по Z. Предполагается, что вызывающий код уже установил рабочую плоскость
    /// в СК вида (WorkPlaneHandler.SetCurrentTransformationPlane(view.ViewCoordinateSystem)) —
    /// тогда solid и возвращаемый контур уже в координатах вида.
    ///
    /// Это и есть нужный путь для угловых размеров: секущая плоскость параллельна плоскости
    /// чертежа (вида), а не плоскости детали — поэтому корректно работает и для балок, и для плит.
    /// </summary>
    /// <param name="part">Деталь (work plane уже должен быть в СК вида).</param>
    /// <param name="solidType">Тип solid; для учёта булен — NORMAL/HIGH_ACCURACY.</param>
    public static (List<Point> contour, List<List<Point>> openings) GetViewPlaneSectionPolygons(
        Part part,
        Solid.SolidCreationTypeEnum solidType = Solid.SolidCreationTypeEnum.NORMAL)
    {
        if (part == null)
            throw new ArgumentNullException(nameof(part));

        var solid = part.GetSolid(solidType);
        if (solid == null)
            return (new List<Point>(), new List<List<Point>>());

        // Z по центру тела в координатах текущей рабочей плоскости (= СК вида).
        var z = (solid.MinimumPoint.Z + solid.MaximumPoint.Z) * 0.5;

        // Три точки в плоскости, параллельной XY вида, на глубине z.
        var p1 = new Point(0, 0, z);
        var p2 = new Point(1000, 0, z);
        var p3 = new Point(0, 1000, z);

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

        // Explicitly pick the outer contour by largest area — do not assume Tekla returns
        // the outer polygon first. The remaining polygons are treated as openings (holes).
        var outerIndex = 0;
        var maxArea = PolygonArea(polygons[0]);
        for (var i = 1; i < polygons.Count; i++)
        {
            var area = PolygonArea(polygons[i]);
            if (area > maxArea)
            {
                maxArea = area;
                outerIndex = i;
            }
        }

        var contour = polygons[outerIndex];
        var openings = polygons.Where((_, i) => i != outerIndex).ToList();
        return (contour, openings);
    }

    // Absolute polygon area via the shoelace formula in the section plane (XY).
    // The section lies on a constant-Z plane in the active work plane, so XY is sufficient.
    private static double PolygonArea(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 3)
            return 0;

        var sum = 0.0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return Math.Abs(sum) * 0.5;
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
