using Tekla.Structures.Drawing;

namespace TeklaMcpServer.Api.Drawing;

internal enum ViewSemanticKind
{
    Other,
    BaseProjected,
    Section,
    Detail
}

internal static class ViewSemanticClassifier
{
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

    public static bool IsBaseProjected(View.ViewTypes viewType)
        => Classify(viewType) == ViewSemanticKind.BaseProjected;
}
