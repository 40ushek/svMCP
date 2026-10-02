using System;

namespace TeklaMcpServer.Api.Drawing;

internal enum DimensionSubjectCategory { Part, Bolt }
internal enum DimensionChainClosure { Open, Closed }
internal enum DimensionChainMeasurementType { Relative, Absolute, RelativeAndAbsolute }

/// <summary>Explicit rule evidence required for combination, never inferred from display keys or layers.</summary>
internal sealed class DimensionCompositionIntent
{
    public DimensionSubjectCategory Category { get; }
    public string Units { get; }
    public string ReferenceId { get; }
    public string ScopeId { get; }
    public DimensionChainMeasurementType DimensionType { get; }
    public DimensionChainClosure Closure { get; }

    public DimensionCompositionIntent(DimensionSubjectCategory category, string units, string referenceId,
        string scopeId, DimensionChainMeasurementType dimensionType, DimensionChainClosure closure = DimensionChainClosure.Open)
    {
        if (!Enum.IsDefined(typeof(DimensionSubjectCategory), category)
            || !Enum.IsDefined(typeof(DimensionChainClosure), closure)
            || !Enum.IsDefined(typeof(DimensionChainMeasurementType), dimensionType))
            throw new ArgumentException("Composition intent must use defined category and closure values.");
        foreach (var value in new[] { units, referenceId, scopeId })
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Composition intent fields must be explicit and non-empty.");
        Category = category;
        Units = units;
        ReferenceId = referenceId;
        ScopeId = scopeId;
        DimensionType = dimensionType;
        Closure = closure;
    }

    public object Project() => new { category = Category.ToString(), units = Units, referenceId = ReferenceId,
        scopeId = ScopeId, dimensionType = DimensionType.ToString(), closure = Closure.ToString() };
}
