using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Algorithms.Geometry;

public readonly struct FarthestPointPairResult
{
    public FarthestPointPairResult(Point first, Point second, double distanceSquared)
    {
        First = first;
        Second = second;
        DistanceSquared = distanceSquared;
    }

    public Point First { get; }
    public Point Second { get; }
    public double DistanceSquared { get; }
    public double Distance => System.Math.Sqrt(DistanceSquared);
}
