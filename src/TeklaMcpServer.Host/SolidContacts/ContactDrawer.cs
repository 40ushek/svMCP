using System.Collections.Generic;
using global::SolidContacts;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model.UI;

namespace TeklaMcpServer.Host.SolidContacts;

/// <summary>
/// Draws found contacts as temporary graphics, so a result can be checked against the
/// model by eye rather than by reading coordinates.
///
/// The outline of the touching area is drawn, not only its centre. The reference could
/// not do this: it computed the overlap polygons, used them to decide the contact
/// existed, and threw them away.
/// </summary>
internal static class ContactDrawer
{
    private const double NormalArrowLength = 100.0;

    private static readonly Color Outline = new(0.0, 0.8, 0.0);
    private static readonly Color Marker = new(1.0, 0.4, 0.0);
    private static readonly Color Edge = new(0.0, 0.0, 1.0);

    public static void Draw(IEnumerable<Contact> contacts)
    {
        var drawer = new GraphicsDrawer();

        foreach (var contact in contacts)
        {
            foreach (var region in contact.Regions)
                DrawClosedOutline(drawer, region, contact.Kind == ContactKind.FaceToFace ? Outline : Edge);

            var anchor = ToPoint(contact.Anchor);
            var tip = ToPoint(contact.Anchor + (contact.Plane.Normal * NormalArrowLength));

            drawer.DrawLineSegment(new LineSegment(anchor, tip), Marker);
            drawer.DrawText(anchor, Label(contact), Marker);
        }
    }

    private static void DrawClosedOutline(GraphicsDrawer drawer, IReadOnlyList<Vec3> points, Color color)
    {
        if (points.Count < 2)
            return;

        // Two points are a line, not a degenerate area: an edge contact is a stretch
        // along which the bodies meet, and wrapping it would draw the same line twice.
        if (points.Count == 2)
        {
            drawer.DrawLineSegment(new LineSegment(ToPoint(points[0]), ToPoint(points[1])), color);
            return;
        }

        // Segment by segment, wrapping past the last point back to the first: the loops
        // carry no duplicate closing vertex, and an unclosed outline reads as a gap in
        // the contact that is not there.
        for (var i = 0; i < points.Count; i++)
        {
            var from = ToPoint(points[i]);
            var to = ToPoint(points[(i + 1) % points.Count]);
            drawer.DrawLineSegment(new LineSegment(from, to), color);
        }
    }

    private static string Label(Contact contact) =>
        contact.Kind == ContactKind.FaceToFace
            ? $"{contact.State} {contact.Area:0} mm2"
            : $"{contact.Kind} {contact.State}";

    private static Point ToPoint(Vec3 value) => new(value.X, value.Y, value.Z);
}
