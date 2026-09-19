using System;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Non-transactional write: preserve IDs and stop before deleting the original
/// unless a committed replacement has passed read-back. Failed replacements are left
/// available for inspection; this protocol never claims rollback.</summary>
public sealed class DimensionWriteState
{
    public int NewDimensionId { get; set; }
    public bool Verified { get; set; }
    public bool OriginalDeleteAttempted { get; set; }
    public bool OriginalDeleteAccepted { get; set; }
    public bool OriginalDeletionVerified { get; set; }
    public bool Completed { get; set; }
    public double? ObservedDistance { get; set; }
    public double? InitialDistance { get; set; }
    public string Stage { get; set; } = "notStarted";
    public string? Error { get; set; }
}

internal static class DimensionWriteProtocol
{
    internal static DimensionWriteState Execute(Func<int> create, Func<bool> commit,
        Func<int, string?> verify, Func<bool>? deleteOriginal = null,
        Func<bool>? originalIsAbsent = null)
    {
        var state = new DimensionWriteState();
        try
        {
            state.Stage = "creating";
            state.NewDimensionId = create();
            if (state.NewDimensionId <= 0)
                throw new InvalidOperationException("CreateDimensionSet returned no usable ID");
            state.Stage = "committingReplacement";
            if (!commit()) throw new InvalidOperationException("Replacement CommitChanges() returned false");
            state.Stage = "verifyingReplacement";
            var error = verify(state.NewDimensionId);
            if (error != null) throw new InvalidOperationException(error);
            state.Verified = true;
            if (deleteOriginal != null)
            {
                state.Stage = "deletingOriginal";
                state.OriginalDeleteAttempted = true;
                state.OriginalDeleteAccepted = deleteOriginal();
                if (!state.OriginalDeleteAccepted)
                    throw new InvalidOperationException("Original Delete() returned false; inspect both IDs");
                state.Verified = false;
                state.Stage = "committingDeletion";
                if (!commit()) throw new InvalidOperationException("Deletion CommitChanges() returned false");
                state.Stage = "verifyingDeletion";
                if (originalIsAbsent == null || !originalIsAbsent())
                    throw new InvalidOperationException("Original deletion could not be confirmed");
                state.OriginalDeletionVerified = true;
                // Deletion/commit can reflow neighbours, including the replacement.
                state.Verified = false;
                error = verify(state.NewDimensionId);
                if (error != null) throw new InvalidOperationException(error);
                state.Verified = true;
            }
            state.Stage = "completed";
            state.Completed = true;
        }
        catch (Exception ex) { state.Error = ex.Message; }
        return state;
    }

    internal static string? Validate(double[]? points, double? distance)
    {
        if (points == null || points.Length < 6 || points.Length % 3 != 0)
            return "points must be a flat array [x0,y0,z0, x1,y1,z1, ...] with at least 2 points";
        if (points.Any(value => !Finite(value))) return "points must be finite";
        if (distance.HasValue && (!Finite(distance.Value) || distance.Value < 0))
            return "distance must be finite and non-negative; choose side with direction";
        for (var i = 0; i < points.Length; i += 3)
            for (var j = 0; j < i; j += 3)
                if (Math.Abs(points[i] - points[j]) < 1e-6 &&
                    Math.Abs(points[i + 1] - points[j + 1]) < 1e-6)
                    return "dimension points must be distinct in the view plane";
        return null;
    }

    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
