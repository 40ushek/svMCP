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

    /// <summary>
    /// Parts whose plane is parallel to the view plane: a plate seen face-on, with its whole
    /// contour drawn. Both of its edges along a chain are real and both are located.
    /// </summary>
    public int[] FaceOnPartIds { get; }

    public SectionPartLocationInput(int[] secondaryPartIds, int[]? endPartIds = null, int[]? faceOnPartIds = null)
    {
        FaceOnPartIds = (int[])(faceOnPartIds ?? Array.Empty<int>()).Clone();
        SecondaryPartIds = (int[])(secondaryPartIds
            ?? throw new ArgumentNullException(nameof(secondaryPartIds))).Clone();
        EndPartIds = (int[])(endPartIds ?? Array.Empty<int>()).Clone();
    }
}
