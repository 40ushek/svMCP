namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Which point of a located part a location chain takes, on a horizontal or a vertical chain
/// alike. A general choice for part location, not a steel one: each rule set reads it from
/// its own settings and keeps its own default.
/// </summary>
internal enum DimensionPartAnchor
{
    /// <summary>The first face along the chain: left on a horizontal chain, lower on a vertical one.</summary>
    LowFace,
    /// <summary>The last face along the chain: right or upper.</summary>
    HighFace,
    /// <summary>Both faces, so the part's own size along the chain is printed too.</summary>
    BothFaces,
    /// <summary>
    /// The middle of the part along the chain. Declared so callers and settings can name it;
    /// not calculated yet, because the middle is not a captured part point.
    /// </summary>
    Center
}
