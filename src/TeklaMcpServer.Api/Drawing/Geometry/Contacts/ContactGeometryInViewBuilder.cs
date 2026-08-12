using System.Collections.Generic;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Takes what the contact search found in a view and says what it looks like on the sheet.
///
/// Nothing here decides anything about dimensions. It is one step of translation: the
/// search answers in three dimensions because that is where touching happens, and a
/// drawing has two, so the depth comes out and the vertices that only existed to describe
/// it come out with it. What remains is the geometry a dimension could later be hung on -
/// but choosing where to hang one is not this layer's business.
/// </summary>
public static class ContactGeometryInViewBuilder
{
    /// <summary>
    /// Flattens every region of every contact. <paramref name="tolerance"/> is how far
    /// apart two places may be and still be one place; the default is
    /// <see cref="RegionFlattener.CoincidenceTolerance"/>, which is far below the contact
    /// search gap tolerance on purpose - see the note there.
    /// </summary>
    public static ViewContactGeometryResult Build(
        ViewContactsResult contacts, double tolerance = RegionFlattener.CoincidenceTolerance)
    {
        if (contacts == null)
        {
            return new ViewContactGeometryResult(
                0, new List<ContactShapeInView>(), new List<UnflattenedRegion>(),
                new List<UnreadPart>(), searchComplete: false, error: "no contact search to flatten");
        }

        var shapes = new List<ContactShapeInView>();
        var unflattened = new List<UnflattenedRegion>();

        foreach (var junction in contacts.Graph.Junctions)
        {
            foreach (var contact in junction.Contacts)
            {
                var participants = new ContactParticipants(contact.SolidAId, contact.SolidBId);

                if (contact.Regions.Count == 0)
                {
                    // The search found a touch and kept no boundary for it. That is a real
                    // answer about the junction and no answer at all about where it is on
                    // the sheet, so it is a loss rather than an empty shape.
                    unflattened.Add(new UnflattenedRegion(
                        contact.Id, 0, participants, "contact carries no region"));
                    continue;
                }

                for (var index = 0; index < contact.Regions.Count; index++)
                {
                    var shape = RegionFlattener.Flatten(contact.Regions[index], tolerance);

                    if (shape.Kind == PlanarShapeKind.Empty)
                    {
                        unflattened.Add(new UnflattenedRegion(
                            contact.Id, index, participants, "region left nothing once flattened"));
                        continue;
                    }

                    shapes.Add(new ContactShapeInView(contact.Id, participants, contact.Kind, shape));
                }
            }
        }

        return new ViewContactGeometryResult(
            contacts.ViewId, shapes, unflattened, contacts.Unread, contacts.IsComplete, contacts.Error);
    }
}
