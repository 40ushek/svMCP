namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum ScaleFlexibility
{
    Fixed,
    SameAsMain,
    CanBeLarger,
    Independent
}

internal static class ScaleFlexibilityResolver
{
    public static ScaleFlexibility ResolveDefault(ViewSemanticKind semanticKind)
        => semanticKind switch
        {
            ViewSemanticKind.BaseProjected => ScaleFlexibility.SameAsMain,
            ViewSemanticKind.Section => ScaleFlexibility.SameAsMain,
            ViewSemanticKind.Detail => ScaleFlexibility.CanBeLarger,
            ViewSemanticKind.Model3D => ScaleFlexibility.Fixed,
            ViewSemanticKind.Other => ScaleFlexibility.Fixed,
            _ => ScaleFlexibility.Fixed
        };
}
