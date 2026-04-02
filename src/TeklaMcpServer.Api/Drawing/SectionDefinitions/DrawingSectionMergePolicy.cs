namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class DrawingSectionMergePolicy
{
    public bool MergeSimilarSections { get; set; }
    public double? MaximumMergeDistance { get; set; }
}
