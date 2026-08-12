using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The extent an overall measures comes from the parts that fix the assembly's size, not
/// from everything the view draws - and it has to say when it could not be sure which was
/// which.
/// </summary>
public sealed class StructuralOutlineTests
{
    private static PartRoleInView Role(int modelId, string prefix)
    {
        var result = new PartRoleClassifier().ClassifyProperties(prefix);
        return new PartRoleInView(modelId, $"{prefix}-{modelId}", prefix, result);
    }

    private sealed class Roles(PartRoleInView[] roles, UnreadPart[]? unread = null) : IDrawingPartRoleApi
    {
        public Roles(params PartRoleInView[] roles) : this(roles, null) { }

        public int Reads { get; private set; }

        public PartRoleReadResult GetRolesInView(int viewId)
        {
            Reads++;
            return new PartRoleReadResult(roles, unread ?? []);
        }
    }

    private sealed class Outline : IDrawingViewOutlineApi
    {
        public IReadOnlyCollection<int>? Asked { get; private set; }

        public ViewAssemblyOutlineResult GetAssemblyOutline(
            int viewId, OutlineOptions? options = null, IReadOnlyCollection<int>? modelIds = null)
        {
            Asked = modelIds;
            return new ViewAssemblyOutlineResult(
                viewId,
                new Clipper2Lib.PolyTreeD(),
                new Dictionary<int, Clipper2Lib.PolyTreeD>(),
                [],
                error: null,
                restricted: modelIds != null,
                visibleCount: 3,
                requestedIds: modelIds?.ToArray(),
                notVisibleRequestedIds: []);
        }
    }

    [Fact]
    public void OnlyTheDefiningPartsAreOutlined()
    {
        // The caller does not have to know which prefixes this plant uses; the selection
        // happens here, from roles the parts already carry.
        var outline = new Outline();
        var roles = new Roles(Role(1, "T"), Role(2, "R"), Role(3, "M"));
        var api = new TeklaDrawingStructuralOutlineApi(roles, outline);

        var result = api.Get(7);

        Assert.Equal([1], outline.Asked);
        Assert.True(result.IsComplete);
        Assert.Null(result.Reservation());

        // Roles come from a properties read, and only the survivors have their solids
        // fetched. Reading every part's geometry to discard most of it would cost the
        // whole point of having roles.
        Assert.Equal(1, roles.Reads);
    }

    [Fact]
    public void APartNoRuleCoversMakesTheExtentProvisional()
    {
        var outline = new Outline();
        var api = new TeklaDrawingStructuralOutlineApi(new Roles(Role(1, "T"), Role(2, "Q")), outline);

        var result = api.Get(7);

        Assert.False(result.IsComplete);
        Assert.Contains("matched no role rule", result.Reservation());
    }

    [Fact]
    public void APartWhosePropertiesCouldNotBeReadMakesTheExtentProvisional()
    {
        // It vanishes from the roles, taking its extent with it, and the outline of what is
        // left comes back clean and short - which reads exactly like a correct one.
        var api = new TeklaDrawingStructuralOutlineApi(
            new Roles([Role(1, "T")], [new UnreadPart(2, "PART_PREFIX could not be read")]),
            new Outline());

        var result = api.Get(7);

        Assert.False(result.IsComplete);
        Assert.Contains("no readable properties", result.Reservation());
        Assert.Contains("PART_PREFIX", result.Reservation());
    }

    [Fact]
    public void AFailedPropertyReadIsUnclassifiedRatherThanUnknown()
    {
        // "Nobody could look" is fixed by reading the part again; "no rule covers it" is
        // fixed by adding a rule. Reporting the first as the second sends someone to edit
        // a table that was never the problem.
        var unreadable = new PartRoleInView(2, "T-2", null, PartRoleResult.Unclassified);

        var api = new TeklaDrawingStructuralOutlineApi(
            new Roles([Role(1, "T"), unreadable], [new UnreadPart(2, "PART_PREFIX could not be read")]),
            new Outline());

        var result = api.Get(7);

        Assert.Single(result.Unclassified);
        Assert.Empty(result.Unknown);
        Assert.Contains("never classified", result.Reservation());
    }

    [Fact]
    public void ADefiningPartTheViewDoesNotDrawIsNotSilentlyDropped()
    {
        // The geometry of what remained can be read perfectly and still not be the outline
        // that was asked for.
        var result = new StructuralOutline(
            new ViewAssemblyOutlineResult(
                7,
                new Clipper2Lib.PolyTreeD(),
                new Dictionary<int, Clipper2Lib.PolyTreeD>(),
                [],
                error: null,
                restricted: true,
                visibleCount: 1,
                requestedIds: [1, 2],
                notVisibleRequestedIds: [2]),
            [Role(1, "T")],
            [],
            [],
            []);

        Assert.True(result.Outline.IsComplete);
        Assert.False(result.Outline.SelectionComplete);
        Assert.False(result.IsComplete);
        Assert.Contains("not drawn in this view", result.Reservation());
    }
}
