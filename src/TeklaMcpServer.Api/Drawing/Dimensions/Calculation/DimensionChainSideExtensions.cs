namespace TeklaMcpServer.Api.Drawing;

/// <summary>What a placement side says about a chain: its axis, which way is outward, the side facing it.</summary>
internal static class DimensionChainSideExtensions
{
    /// <summary>A chain on the top or bottom side runs along X; on the left or right side, along Y.</summary>
    public static bool RunsAlongX(this DimensionChainSide side) =>
        side is DimensionChainSide.Top or DimensionChainSide.Bottom;

    /// <summary>+1 where outward is the growing coordinate (top, right), -1 otherwise.</summary>
    public static int OutwardSign(this DimensionChainSide side) =>
        side is DimensionChainSide.Top or DimensionChainSide.Right ? 1 : -1;

    public static DimensionChainSide Opposite(this DimensionChainSide side) => side switch {
        DimensionChainSide.Top => DimensionChainSide.Bottom,
        DimensionChainSide.Bottom => DimensionChainSide.Top,
        DimensionChainSide.Left => DimensionChainSide.Right,
        _ => DimensionChainSide.Left
    };
}
