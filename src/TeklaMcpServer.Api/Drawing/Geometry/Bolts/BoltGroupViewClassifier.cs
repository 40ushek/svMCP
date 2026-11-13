namespace TeklaMcpServer.Api.Drawing;

/// <summary>Classifies a bolt group by the angle between its axis and the view normal.</summary>
internal static class BoltGroupViewClassifier
{
    /// <param name="axis">Bolt axis in view coordinates; the view normal is the Z axis.</param>
    /// <param name="toleranceDegrees">Symmetric, inclusive limit for both Plan and Side; 0 &lt;= t &lt; 45.</param>
    internal static BoltGroupViewClass Classify(double[]? axis, double toleranceDegrees)
    {
        if (double.IsNaN(toleranceDegrees) || double.IsInfinity(toleranceDegrees)
            || toleranceDegrees < 0 || toleranceDegrees >= 45)
            throw new ArgumentOutOfRangeException(nameof(toleranceDegrees), "Class tolerance must be finite, at least 0 and below 45 degrees.");
        if (axis == null || axis.Length != 3 || axis.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
            return BoltGroupViewClass.Unresolved;
        var length = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
        if (length <= 1e-9) return BoltGroupViewClass.Unresolved;
        var theta = Math.Acos(Math.Min(1, Math.Abs(axis[2]) / length)) * 180 / Math.PI;
        if (theta <= toleranceDegrees) return BoltGroupViewClass.Plan;
        if (90 - theta <= toleranceDegrees) return BoltGroupViewClass.Side;
        return BoltGroupViewClass.Skewed;
    }
}
