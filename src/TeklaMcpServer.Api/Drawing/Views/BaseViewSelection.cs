using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal enum BaseViewSelectionKind
{
    Resolved,
    Fallback,
    Unresolved
}

internal sealed class BaseViewSelectionResult
{
    public View? View { get; set; }

    public BaseViewSelectionKind SelectionKind { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}

internal static class BaseViewSelection
{
    public static BaseViewSelectionResult Select(IReadOnlyList<View> views)
    {
        var front = views.FirstOrDefault(v => v.ViewType == View.ViewTypes.FrontView);
        if (front != null)
        {
            return new BaseViewSelectionResult
            {
                View = front,
                SelectionKind = BaseViewSelectionKind.Fallback,
                Reason = "front-view-shortcut",
                IsFallback = true
            };
        }

        return new BaseViewSelectionResult
        {
            View = null,
            SelectionKind = BaseViewSelectionKind.Unresolved,
            Reason = "no-base-view",
            IsFallback = false
        };
    }
}
