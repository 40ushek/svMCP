using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionArrangementDedup
{
    internal static DimensionReductionPolicy CreatePolicy()
    {
        return new DimensionReductionPolicy
        {
            RequireSameSourceKindForSimpleReduction = true,
            EnableEquivalentSimpleReduction = true,
            EnableCoverageReduction = false,
            EnableRepresentativeSelection = false
        };
    }

    internal static DimensionReductionDebugResult ReduceWithDebug(IReadOnlyList<DimensionGroup> groups)
    {
        return DimensionOperations.EliminateRedundantItemsWithDebug(
            groups,
            CreatePolicy(),
            DimensionCombinePolicy.Default);
    }

    internal static List<DimensionGroup> Reduce(IReadOnlyList<DimensionGroup> groups)
    {
        return DimensionOperations.EliminateRedundantItems(
            groups,
            CreatePolicy(),
            DimensionCombinePolicy.Default);
    }
}
