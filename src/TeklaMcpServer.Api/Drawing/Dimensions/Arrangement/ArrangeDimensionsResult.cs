using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public sealed class ArrangeDimensionApplied
{
    public int DimensionId { get; set; }
    public double DistanceDelta { get; set; }
    public double NewDistance { get; set; }
}

public sealed class ArrangeDimensionsResult
{
    public int AppliedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ArrangeDimensionApplied> Applied { get; } = [];
    public List<string> SkipReasons { get; } = [];
}
