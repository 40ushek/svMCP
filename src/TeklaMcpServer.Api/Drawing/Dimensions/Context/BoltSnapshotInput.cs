using System.Linq;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Decoded once at the snapshot boundary, shared by bolt calculations.</summary>
internal sealed class BoltSnapshotInput(BoltSnapshotGroup[] groups)
{
    internal BoltSnapshotGroup[] Groups { get; } = groups;
    internal static BoltSnapshotInput Read(JsonElement snapshot) => new(
        snapshot.TryGetProperty("boltGroups", out var rows) ? rows.EnumerateArray().Select(row =>
            new BoltSnapshotGroup(BoltDimensionChainPreview.ReadGroup(row),
                row.GetProperty("restriction").EnumerateArray().Select(value => new BoltCenterRestriction(
                    value.GetProperty("index").GetInt32(), value.GetProperty("centerState").GetString())).ToArray())).ToArray() : []);
}

internal sealed class BoltSnapshotGroup(BoltGroupGeometry geometry, BoltCenterRestriction[] restrictions)
{
    internal BoltGroupGeometry Geometry { get; } = geometry;
    internal BoltCenterRestriction[] Restrictions { get; } = restrictions;
}

internal sealed class BoltCenterRestriction(int index, string? centerState)
{
    internal int Index { get; } = index;
    internal string? CenterState { get; } = centerState;
}
