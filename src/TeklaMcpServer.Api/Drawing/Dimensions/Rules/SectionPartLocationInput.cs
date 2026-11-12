using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Secondary parts eligible for the section location chain.</summary>
internal sealed class SectionPartLocationInput
{
    public int[] SecondaryPartIds { get; }

    /// <summary>
    /// Parts that reach past the main part's end along the view depth: an end plate or a cap,
    /// which covers the profile instead of standing inside it. Closeness of such a part's edge
    /// to a profile face is a coincidence of coordinates, not an abutting joint.
    /// </summary>
    public int[] EndPartIds { get; }

    public SectionPartLocationInput(int[] secondaryPartIds, int[]? endPartIds = null)
    {
        SecondaryPartIds = (int[])(secondaryPartIds
            ?? throw new ArgumentNullException(nameof(secondaryPartIds))).Clone();
        EndPartIds = (int[])(endPartIds ?? Array.Empty<int>()).Clone();
    }
}
