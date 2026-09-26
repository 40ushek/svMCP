using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Secondary parts eligible for the section location chain.</summary>
internal sealed class SectionPartLocationInput
{
    public int[] SecondaryPartIds { get; }

    public SectionPartLocationInput(int[] secondaryPartIds)
    {
        SecondaryPartIds = (int[])(secondaryPartIds
            ?? throw new ArgumentNullException(nameof(secondaryPartIds))).Clone();
    }
}
