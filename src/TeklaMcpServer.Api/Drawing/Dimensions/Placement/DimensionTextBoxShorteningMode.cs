using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Experimental switch for dimension text box shortening conversion.
/// Default is <see cref="None"/> until empirical smoke confirms which direction
/// matches the actual coordinate system used by force-flow on a shortened view.
/// Set via the SVMCP_DIM_SHORTENING_MODE environment variable: none, toVisual, toRaw.
/// </summary>
internal enum DimensionTextBoxShorteningMode
{
    None = 0,
    ToVisual = 1,
    ToRaw = 2
}

internal static class DimensionTextBoxShorteningModeResolver
{
    private const string EnvVar = "SVMCP_DIM_SHORTENING_MODE";

    internal static DimensionTextBoxShorteningMode Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return DimensionTextBoxShorteningMode.None;

        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "tovisual" or "visual" or "convertpolygon" => DimensionTextBoxShorteningMode.ToVisual,
            "toraw" or "raw" or "convertpolygontoraw" => DimensionTextBoxShorteningMode.ToRaw,
            _ => DimensionTextBoxShorteningMode.None
        };
    }
}
