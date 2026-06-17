namespace TeklaMcpServer.Api.Drawing.ViewLayout;

internal enum ScaleFlexibility
{
    Fixed,
    SameAsMain,
    CanBeLarger,
    Independent
}

public enum SecondaryScalePolicy
{
    SameAsMain,
    PreserveIfNotSmaller,
    PreserveLargerIfFits,
    AllowLargerIfFits
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

    // When SecondaryScalePolicy is active, Section is promoted to CanBeLarger
    // so the policy can take effect. Global default stays SameAsMain.
    public static ScaleFlexibility Resolve(ViewSemanticKind semanticKind, SecondaryScalePolicy secondaryPolicy)
    {
        var flex = ResolveDefault(semanticKind);
        if (flex == ScaleFlexibility.SameAsMain
            && semanticKind == ViewSemanticKind.Section
            && secondaryPolicy != SecondaryScalePolicy.SameAsMain)
        {
            return ScaleFlexibility.CanBeLarger;
        }
        return flex;
    }
}
