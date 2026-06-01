using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using SolidTypes = Tekla.Structures.Solid;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

/// <summary>
/// Контур детали через её грани (Solid.GetFaceEnumerator → Loop → вершины).
/// Берётся грань, нормаль которой сонаправлена с нормалью детали (большая лицевая грань);
/// её внешний loop — это контур, включающий реальную геометрию граней (в т.ч. срезы).
///
/// Возвращает точки в координатах модели (world).
///
/// Примечание: для деталей с булевыми обрезками сечение (см. <see cref="SolidSectionContourHelper"/>)
/// обычно надёжнее — face-loop может содержать дополнительные рёбра. Этот хелпер полезен,
/// когда нужна именно геометрия лицевой грани.
/// </summary>
public static class SolidFaceContourHelper
{
    /// <summary>
    /// Внешний контур лицевой грани детали (грань с нормалью ≈ ±нормаль детали).
    /// </summary>
    /// <param name="part">Деталь.</param>
    /// <param name="solidType">Тип solid.</param>
    /// <returns>Контур (model-CS) или пустой список, если грань не найдена.</returns>
    public static List<Point> GetFaceContour(
        Part part,
        Solid.SolidCreationTypeEnum solidType = Solid.SolidCreationTypeEnum.HIGH_ACCURACY)
    {
        if (part == null)
            throw new ArgumentNullException(nameof(part));

        var solid = part.GetSolid(solidType);
        if (solid == null)
            return new List<Point>();

        var cs = part.GetCoordinateSystem();
        var partNormal = Vector.Cross(cs.AxisX, cs.AxisY);
        partNormal.Normalize();

        SolidTypes.Face? bestFace = null;
        var bestDot = 0.0;

        var faceEnum = solid.GetFaceEnumerator();
        while (faceEnum.MoveNext())
        {
            if (faceEnum.Current is not SolidTypes.Face face)
                continue;

            var n = new Vector(face.Normal);
            if (n.GetLength() < 1e-6)
                continue;
            n.Normalize();

            // Лицевая грань: нормаль почти параллельна нормали детали (±).
            var dot = Math.Abs(n.Dot(partNormal));
            if (dot > bestDot)
            {
                bestDot = dot;
                bestFace = face;
            }
        }

        if (bestFace == null || bestDot < 0.9)
            return new List<Point>();

        return GetOuterLoopVertices(bestFace);
    }

    /// <summary>
    /// Все полигоны грани (внешний loop + отверстия), в порядке обхода loop'ов.
    /// </summary>
    public static List<List<Point>> GetFacePolygons(SolidTypes.Face face)
    {
        if (face == null)
            throw new ArgumentNullException(nameof(face));

        var result = new List<List<Point>>();
        var loopEnum = face.GetLoopEnumerator();
        while (loopEnum.MoveNext())
        {
            if (loopEnum.Current is not SolidTypes.Loop loop)
                continue;

            var pts = new List<Point>();
            var vertexEnum = loop.GetVertexEnumerator();
            while (vertexEnum.MoveNext())
            {
                if (vertexEnum.Current is Point p)
                    pts.Add(p);
            }

            if (pts.Count >= 3)
                result.Add(pts);
        }

        return result;
    }

    // Внешний loop = первый loop грани (Tekla отдаёт внешний контур первым).
    private static List<Point> GetOuterLoopVertices(SolidTypes.Face face)
    {
        var polygons = GetFacePolygons(face);
        return polygons.Count > 0 ? polygons[0] : new List<Point>();
    }
}
