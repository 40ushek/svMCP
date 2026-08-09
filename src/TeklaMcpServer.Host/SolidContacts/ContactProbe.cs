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
    /// <summary>
    /// Searches each group of parts on its own.
    ///
    /// A group is whatever somebody is actually looking at: the parts picked in the
    /// model, or the parts drawn in one view. Views are kept apart rather than pooled -
    /// a section shows one layer of a wall, and pouring the other layers in would report
    /// junctions that appear nowhere in it.
    /// </summary>
    public static void Run(double gapTolerance = 1.0, bool draw = true, bool wholeSheet = false)
    {
        var options = new ContactOptions { GapTolerance = gapTolerance };
        var groups = Groups(wholeSheet).Where(group => group.Parts.Count >= 2).ToList();

        if (groups.Count == 0)
        {
            Console.WriteLine("Nothing to search. Select two or more parts in the model, or open a drawing.");
            return;
        }

        Console.WriteLine($"gap tolerance: {options.GapTolerance} mm");

        var drawable = new List<Contact>();

        foreach (var group in groups)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {group.Name}: {group.Parts.Count} part(s) ===");

            var named = new Dictionary<string, Part>();
            var solids = new List<ISolid>();

            foreach (var part in group.Parts)
            {
                var solid = TeklaSolidAdapter.FromPart(part);
                if (solid == null)
                {
                    Console.WriteLine($"  {Describe(part)}: no solid, skipped");
                    continue;
                }

                named[solid.Id] = part;
                solids.Add(solid);
            }

            var graph = ContactGraph.Build(solids, options);
            Report(graph, named);
            drawable.AddRange(graph.AllContacts);
        }

        Console.WriteLine();
        Console.WriteLine($"contacts across {groups.Count} group(s): {drawable.Count}");

        if (draw && drawable.Count > 0)
        {
            ContactDrawer.Draw(drawable);
            Console.WriteLine("drawn in the model view");
        }
    }

    /// <summary>
    /// What is worth shouting about a junction: bodies inside one another, and pairs held
    /// from both sides with little room across the engagement. The second says nothing
    /// any single contact does.
    /// </summary>
    private static string Flags(Junction junction)
    {
        var flags = string.Empty;

        if (junction.Interpenetrates)
            flags += "  INTERPENETRATES";

        if (junction.TightestOpposedClearance is { } room)
            flags += $"  opposed clearance={room:0.###}";

        return flags;
    }

    private static void Report(ContactGraph graph, IReadOnlyDictionary<string, Part> named)
    {
        Console.WriteLine(graph.ToString());

        foreach (var junction in graph.Junctions.OrderByDescending(j => j.Area))
        {
            Console.WriteLine(
                "{0}  <->  {1}   {2} contact(s), area={3:0.#}, closest gap={4:0.###}{5}",
                Name(named, junction.SolidAId),
                Name(named, junction.SolidBId),
                junction.Contacts.Count,
                junction.Area,
                junction.ClosestGap,
                Flags(junction));

            foreach (var contact in junction.Contacts)
            {
                Console.WriteLine(
                    "   {0,-11} {1,-9} at {2}  n={3}  area={4,10:0.#}  gap={5:0.###}",
                    contact.Kind,
                    contact.State,
                    contact.Anchor,
                    contact.Plane.Normal,
                    contact.Area,
                    contact.Gap);
            }
        }

        if (graph.Isolated.Count > 0)
        {
            Console.WriteLine("touching nothing: " +
                string.Join(", ", graph.Isolated.Select(id => Name(named, id))));
        }

        foreach (var failure in graph.Failures)
        {
            Console.WriteLine(
                $"FAILED {Name(named, failure.SolidAId)} <-> {Name(named, failure.SolidBId)}: {failure.Exception.Message}");
        }

        Console.WriteLine("neighbours:");
        foreach (var id in graph.SolidIds.OrderBy(id => Name(named, id), StringComparer.OrdinalIgnoreCase))
        {
            var neighbours = graph.NeighboursOf(id);
            Console.WriteLine(
                "   {0,-18} {1}",
                Name(named, id),
                neighbours.Count == 0 ? "-" : string.Join(", ", neighbours.Select(n => Name(named, n))));
        }
    }

    private static string Name(IReadOnlyDictionary<string, Part> named, string id) =>
        named.TryGetValue(id, out var part) ? Describe(part) : id;

    private sealed class PartGroup
    {
        public PartGroup(string name, IReadOnlyList<Part> parts)
        {
            Name = name;
            Parts = parts;
        }

        public string Name { get; }
        public IReadOnlyList<Part> Parts { get; }
    }

    private static IEnumerable<PartGroup> Groups(bool wholeSheet)
    {
        var selected = SelectedParts().ToList();
        if (selected.Count >= 2)
        {
            yield return new PartGroup("model selection", selected);
            yield break;
        }

        var drawing = new Tekla.Structures.Drawing.DrawingHandler().GetActiveDrawing();
        if (drawing == null)
            yield break;

        var byView = new List<PartGroup>();

        var views = drawing.GetSheet().GetViews();
        var index = 0;

        while (views.MoveNext())
        {
            if (views.Current is not Tekla.Structures.Drawing.View view)
                continue;

            index++;
            var parts = PartsOfView(view).ToList();
            if (parts.Count == 0)
                continue;

            var name = string.IsNullOrWhiteSpace(view.Name) ? $"view {index}" : view.Name;
            byView.Add(new PartGroup(name, parts));
        }

        if (!wholeSheet)
        {
            foreach (var group in byView)
                yield return group;

            yield break;
        }

        // The sheet as one group, for the times the question is about the assembly rather
        // than about a drawing of it. A part shown in several views is one part.
        var pooled = byView
            .SelectMany(group => group.Parts)
            .GroupBy(part => part.Identifier.ID)
            .Select(sameId => sameId.First())
            .ToList();

        yield return new PartGroup("whole sheet", pooled);
    }

    /// <summary>
    /// The model parts one drawing view actually draws.
    ///
    /// Taken from the view, not from an assembly. A section through a layered wall shows
    /// one layer, and reaching past it would report junctions that appear nowhere in it.
    /// Parts hidden in this view are left out for the same reason.
    ///
    /// Read in the model's own plane on purpose. A view has a plane of its own, and
    /// contacts asked in it would be answered in a flattened world.
    /// </summary>
    private static IEnumerable<Part> PartsOfView(Tekla.Structures.Drawing.View view)
    {
        var model = new Model();
        var seen = new HashSet<int>();

        var objects = view.GetObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not Tekla.Structures.Drawing.Part drawingPart)
                continue;

            if (IsHidden(drawingPart))
                continue;

            var id = drawingPart.ModelIdentifier;
            if (!seen.Add(id.ID))
                continue;

            if (model.SelectModelObject(id) is Part part)
                yield return part;
        }
    }

    private static bool IsHidden(Tekla.Structures.Drawing.Part drawingPart)
    {
        try
        {
            return drawingPart.Hideable.IsHidden;
        }
        catch
        {
            // A part that will not say counts as drawn: leaving it out would silently
            // drop junctions, while keeping it only costs a search.
            return false;
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
