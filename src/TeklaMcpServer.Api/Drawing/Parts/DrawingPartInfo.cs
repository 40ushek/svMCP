namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingPartInfo
{
    public int    ModelId      { get; set; }
    public string Type        { get; set; } = string.Empty;
    public string PartPos     { get; set; } = string.Empty;

    /// <summary>
    /// Mark prefix, e.g. "T" in "T-368". Report property PART_PREFIX.
    ///
    /// Read here because this is where a person decides what to exclude: nothing in the
    /// code reads a meaning into a prefix, so the list of them has to be visible to be
    /// written. Splitting it out of PartPos would be guessing at a mark format.
    /// </summary>
    public string PartPrefix  { get; set; } = string.Empty;

    /// <summary>
    /// False when Tekla refused PART_PREFIX. An empty prefix is an answer; an unread one is
    /// not, and an exclusion filter written from a column of blanks would match nothing
    /// while looking like a filter that found nothing to remove.
    /// </summary>
    public bool PartPrefixKnown { get; set; } = true;
    public string AssemblyPos { get; set; } = string.Empty;
    public string Profile     { get; set; } = string.Empty;
    public string Material    { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
}

public sealed class GetDrawingPartsResult
{
    public int                    Total { get; set; }
    public System.Collections.Generic.List<DrawingPartInfo> Parts { get; set; } = new();
}
