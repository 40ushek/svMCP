using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Parses a comma/space-separated model id list from a bridge command argument. No Tekla
/// dependency, so it is tested directly rather than by grepping the bridge dispatcher that
/// calls it.
/// </summary>
public static class ModelIdListParser
{
    /// <summary>
    /// Model ids from the argument, or null when the caller named none.
    ///
    /// A token that is not a number is refused rather than skipped. Skipping it turns a
    /// typo into a full outline that looks like a successful answer, and a full extent is
    /// exactly the wrong number to hand back by accident - it is longer than the frame by
    /// whatever overhangs it.
    /// </summary>
    public static bool TryParse(string argument, out IReadOnlyCollection<int>? modelIds, out string? error)
    {
        modelIds = null;
        error = null;

        var tokens = argument.Split([',', ' '], System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            // A blank argument means "caller named none" - the legitimate no-filter case.
            // Content that is only delimiters (",", " , ") is not blank: it is a malformed
            // attempt to name ids, and must fail loudly rather than silently widen the
            // search to the whole view.
            if (string.IsNullOrWhiteSpace(argument))
                return true;

            error = $"'{argument}' has no model ids in it";
            return false;
        }

        var ids = new HashSet<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var id))
            {
                error = $"'{token}' is not a model id";
                return false;
            }

            ids.Add(id);
        }

        modelIds = ids;
        return true;
    }
}
