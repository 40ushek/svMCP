namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum ProjectionStrength
{
    Off,
    Weak,
    Relaxed,
    Strong
}

internal static class ProjectionStrengthResolver
{
    public static ProjectionStrength ResolveDefault(ViewSemanticKind semanticKind)
        => semanticKind switch
        {
            ViewSemanticKind.BaseProjected => ProjectionStrength.Strong,
            ViewSemanticKind.Section => ProjectionStrength.Relaxed,
            ViewSemanticKind.Detail => ProjectionStrength.Weak,
            ViewSemanticKind.Model3D => ProjectionStrength.Off,
            ViewSemanticKind.Other => ProjectionStrength.Off,
            _ => ProjectionStrength.Off
        };
}
