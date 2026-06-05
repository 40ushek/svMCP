using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingTextBoxDiagnostics
{
    /// <summary>
    /// Formats a compact "kind:count[,kind:count]" summary of text boxes by SourceObjectKind.
    /// Used in PerfTrace events so smoke runs can tell apart presentation/fallback/runtime sources.
    /// </summary>
    public static string FormatSources(IReadOnlyList<DrawingTextBox> textBoxes)
    {
        if (textBoxes.Count == 0)
            return "none";

        return string.Join(
            ",",
            textBoxes
                .GroupBy(static textBox => string.IsNullOrWhiteSpace(textBox.SourceObjectKind) ? "<unknown>" : textBox.SourceObjectKind)
                .OrderBy(static group => group.Key)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }
}
