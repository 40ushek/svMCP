namespace TeklaMcpServer.Api.Drawing;

/// <summary>How a bolt group is turned to the view it is drawn on.</summary>
internal enum BoltGroupViewClass
{
    /// <summary>The bolt axis could not be read.</summary>
    Unresolved,
    /// <summary>The bolt axis points at the viewer; the pattern is seen in full.</summary>
    Plan,
    /// <summary>The bolt axis lies in the view plane.</summary>
    Side,
    /// <summary>Neither; no chain is built.</summary>
    Skewed
}
