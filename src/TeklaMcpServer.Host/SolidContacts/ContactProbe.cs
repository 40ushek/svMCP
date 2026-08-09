using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using global::SolidContacts;
using global::SolidContacts.TeklaAdapter;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;

namespace TeklaMcpServer.Host.SolidContacts;

/// <summary>
/// Runs the SolidContacts search over the parts selected in the model and reports what
/// it found. The library's own tests use hand-built boxes, which cannot show what a cut
/// beam or a raked plate does to a face; this is how it meets real geometry.
/// </summary>
internal static class ContactProbe
{
    public static void Run(double gapTolerance = 1.0, bool draw = true)
    {
        var parts = SelectedParts().ToList();
        if (parts.Count < 2)
        {
            Console.WriteLine($"Select at least two parts. Selected: {parts.Count}.");
            return;
        }

        var options = new ContactOptions { GapTolerance = gapTolerance };
        Console.WriteLine($"parts: {parts.Count}, gap tolerance: {options.GapTolerance} mm");

        var solids = new List<(Part Part, TeklaSolidAdapter Solid)>();
        foreach (var part in parts)
        {
            var solid = TeklaSolidAdapter.FromPart(part);
            if (solid == null)
            {
                Console.WriteLine($"  {Describe(part)}: no solid, skipped");
                continue;
            }

            solids.Add((part, solid));
        }

        var found = new List<Contact>();

        for (var i = 0; i < solids.Count; i++)
        {
            for (var j = i + 1; j < solids.Count; j++)
            {
                var contacts = ContactFinder.Find(solids[i].Solid, solids[j].Solid, options);
                if (contacts.Count == 0)
                    continue;

                Console.WriteLine();
                Console.WriteLine($"{Describe(solids[i].Part)}  <->  {Describe(solids[j].Part)}");

                foreach (var contact in contacts)
                {
                    found.Add(contact);
                    Console.WriteLine(
                        "   {0,-11} {1,-8} at {2}  n={3}  area={4,10:0.#}  gap={5:0.###}",
                        contact.Kind,
                        contact.State,
                        contact.Anchor,
                        contact.Plane.Normal,
                        contact.Area,
                        contact.Gap);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"contacts: {found.Count}");

        if (draw && found.Count > 0)
        {
            ContactDrawer.Draw(found);
            Console.WriteLine("drawn in the model view");
        }
    }

    private static IEnumerable<Part> SelectedParts()
    {
        var enumerator = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();

        while (enumerator.MoveNext())
        {
            if (enumerator.Current is Part part)
                yield return part;
        }
    }

    private static string Describe(Part part)
    {
        var mark = string.Empty;
        try
        {
            mark = part.GetPartMark() ?? string.Empty;
        }
        catch
        {
            // A part without a mark is still worth naming by id.
        }

        return string.IsNullOrEmpty(mark)
            ? part.Identifier.ID.ToString(CultureInfo.InvariantCulture)
            : $"{mark} ({part.Identifier.ID})";
    }
}
