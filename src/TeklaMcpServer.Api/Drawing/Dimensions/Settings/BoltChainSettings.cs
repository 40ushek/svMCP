namespace TeklaMcpServer.Api.Drawing;

/// <summary>Tekla's "Main/Secondary part bolt internal dimensions", with its names and values.</summary>
internal enum BoltInternalDimensions
{
    /// <summary>No bolt chain for this part role.</summary>
    None,
    /// <summary>Only what tells the part's orientation; not implemented yet, so nothing is created.</summary>
    Necessary,
    /// <summary>Steps between the bolts of one group, per row.</summary>
    Internal,
    /// <summary>Steps and edge distances: edge - bolts - edge.</summary>
    All
}

/// <summary>This project's own: which classes of bolt groups are dimensioned.</summary>
internal enum BoltDimensionedProjections
{
    Plan,
    Side,
    Both
}

/// <summary>
/// Which groups of one part share one edge chain. Tekla merges close groups unless
/// <c>XS_DIMENSION_ALL_BOLT_GROUPS_SEPARATELY</c> is TRUE; what counts as alike is this project's own.
/// </summary>
internal enum BoltGroupMerge
{
    /// <summary>Every group its own chain.</summary>
    Separate,
    /// <summary>Groups with the same hole (diameter, type, slots) share one chain.</summary>
    SameHole,
    /// <summary>Groups with the same bolt size share one chain.</summary>
    SameBoltSize
}

/// <summary>Every number and choice for bolt chains; the bolt rule holds no constants of its own.</summary>
internal sealed class BoltChainSettings
{
    /// <summary>Tekla: Main part bolt internal dimensions.</summary>
    public BoltInternalDimensions MainPartInternal { get; }

    /// <summary>Tekla: Secondary part bolt internal dimensions.</summary>
    public BoltInternalDimensions SecondaryPartInternal { get; }

    /// <summary>Own: which classes of groups are dimensioned.</summary>
    public BoltDimensionedProjections DimensionedProjections { get; }

    /// <summary>Own: how far, in degrees, a bolt axis may be from the view normal (Plan) or the view plane (Side).</summary>
    public double ClassToleranceDegrees { get; }

    /// <summary>Which groups of one part share one edge chain.</summary>
    public BoltGroupMerge GroupMerge { get; }

    public BoltChainSettings(double classToleranceDegrees = 1.0,
        BoltInternalDimensions mainPartInternal = BoltInternalDimensions.All,
        BoltInternalDimensions secondaryPartInternal = BoltInternalDimensions.All,
        BoltDimensionedProjections dimensionedProjections = BoltDimensionedProjections.Both,
        BoltGroupMerge groupMerge = BoltGroupMerge.SameHole)
    {
        GroupMerge = groupMerge;
        if (double.IsNaN(classToleranceDegrees) || double.IsInfinity(classToleranceDegrees)
            || classToleranceDegrees < 0 || classToleranceDegrees >= 45)
            throw new ArgumentOutOfRangeException(nameof(classToleranceDegrees), "Class tolerance must be finite, at least 0 and below 45 degrees.");
        ClassToleranceDegrees = classToleranceDegrees;
        MainPartInternal = mainPartInternal;
        SecondaryPartInternal = secondaryPartInternal;
        DimensionedProjections = dimensionedProjections;
    }

    internal BoltInternalDimensions For(bool mainPart) => mainPart ? MainPartInternal : SecondaryPartInternal;

    internal bool Dimensions(BoltGroupViewClass viewClass) => viewClass switch {
        BoltGroupViewClass.Plan => DimensionedProjections != BoltDimensionedProjections.Side,
        BoltGroupViewClass.Side => DimensionedProjections != BoltDimensionedProjections.Plan,
        _ => false
    };
}
