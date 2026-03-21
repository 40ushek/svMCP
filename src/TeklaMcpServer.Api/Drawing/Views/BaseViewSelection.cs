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
        var eligibleCandidates = views
            .Select((view, index) => new BaseViewCandidate(view, index))
            .Where(candidate => ViewSemanticClassifier.IsBaseProjected(candidate.View.ViewType))
            .ToList();

        if (eligibleCandidates.Count == 1)
        {
            return new BaseViewSelectionResult
            {
                View = eligibleCandidates[0].View,
                SelectionKind = BaseViewSelectionKind.Resolved,
                Reason = "single-base-candidate",
                IsFallback = false
            };
        }

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

        if (eligibleCandidates.Count > 1)
        {
            var centroidX = eligibleCandidates.Average(candidate => candidate.View.Origin?.X ?? 0.0);
            var centroidY = eligibleCandidates.Average(candidate => candidate.View.Origin?.Y ?? 0.0);
            var ranked = eligibleCandidates
                .OrderByDescending(candidate => GetArea(candidate.View))
                .ThenBy(candidate => GetDistanceSquared(candidate.View, centroidX, centroidY))
                .ThenBy(candidate => candidate.Index)
                .ToList();

            return new BaseViewSelectionResult
            {
                View = ranked[0].View,
                SelectionKind = BaseViewSelectionKind.Fallback,
                Reason = "ranked-base-candidate",
                IsFallback = true
            };
        }

        return new BaseViewSelectionResult
        {
            View = null,
            SelectionKind = BaseViewSelectionKind.Unresolved,
            Reason = "no-base-candidate",
            IsFallback = false
        };
    }

    private static double GetArea(View view)
        => System.Math.Max(view.Width, 0) * System.Math.Max(view.Height, 0);

    private static double GetDistanceSquared(View view, double centroidX, double centroidY)
    {
        var origin = view.Origin;
        var dx = (origin?.X ?? 0.0) - centroidX;
        var dy = (origin?.Y ?? 0.0) - centroidY;
        return (dx * dx) + (dy * dy);
    }

    private sealed class BaseViewCandidate
    {
        public BaseViewCandidate(View view, int index)
        {
            View = view;
            Index = index;
        }

        public View View { get; }

        public int Index { get; }
    }
}
