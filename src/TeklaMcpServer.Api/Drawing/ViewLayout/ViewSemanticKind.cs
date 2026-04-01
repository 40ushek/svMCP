using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum ViewSemanticKind
{
    Other,
    BaseProjected,
    Section,
    Detail
}

internal static class ViewSemanticClassifier
{
    public static ViewSemanticKind Classify(View view)
    {
        var byType = Classify(view.ViewType);
        if (byType != ViewSemanticKind.Section)
            return byType;

        return IsDetailLikeName(view.Name)
            ? ViewSemanticKind.Detail
            : ViewSemanticKind.Section;
    }

    public static ViewSemanticKind Classify(View.ViewTypes viewType)
        => viewType switch
        {
            View.ViewTypes.FrontView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.TopView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.BackView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.BottomView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.EndView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.ModelView => ViewSemanticKind.BaseProjected,
            View.ViewTypes.SectionView => ViewSemanticKind.Section,
            View.ViewTypes.DetailView => ViewSemanticKind.Detail,
            _ => ViewSemanticKind.Other
        };

    private static bool IsDetailLikeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name!.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed.StartsWith("Detail", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.StartsWith("Det", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.Length > 1 && trimmed[0] == 'D')
            return true;

        return char.IsLower(trimmed[0]);
    }

    public static bool IsBaseProjected(View.ViewTypes viewType)
        => Classify(viewType) == ViewSemanticKind.BaseProjected;
}

