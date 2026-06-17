namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum LayoutViewKind
{
    Other,
    MainProjected,
    StandardSection,
    AnchorDetailSection,
    Detail,
    Model3D
}

internal static class LayoutViewKindResolver
{
    public static LayoutViewKind ResolveDefault(ViewSemanticKind semanticKind)
        => semanticKind switch
        {
            ViewSemanticKind.BaseProjected => LayoutViewKind.MainProjected,
            ViewSemanticKind.Section => LayoutViewKind.StandardSection,
            ViewSemanticKind.Detail => LayoutViewKind.Detail,
            ViewSemanticKind.Model3D => LayoutViewKind.Model3D,
            ViewSemanticKind.Other => LayoutViewKind.Other,
            _ => LayoutViewKind.Other
        };
}
