using System.Collections.Generic;
using SolidContacts;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Reads the exact projected outline of the visible parts of one drawing view.</summary>
public interface IDrawingViewOutlineApi
{
    /// <summary>
    /// The outline of what the view draws, or of a chosen subset of it.
    ///
    /// The subset arrives as model ids rather than as a rule, so one mechanism serves both
    /// directions: leave out the insulation, or ask for the frame alone. Deciding which
    /// parts those are belongs to whoever knows about roles; this only projects polygons
    /// and has no business knowing what a part is for.
    /// </summary>
    ViewAssemblyOutlineResult GetAssemblyOutline(
        int viewId,
        OutlineOptions? options = null,
        IReadOnlyCollection<int>? modelIds = null);
}
