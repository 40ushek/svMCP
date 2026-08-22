using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// The extent an overall measures comes from the parts a view draws, minus what the
/// caller's filter excludes - and it has to say when a part could not be read at all.
/// </summary>
public sealed class StructuralOutlineTests
{
    private static readonly PartRoleClassifier WithoutInsulationOrFixings =
        new([PartExclusionRule.ByPrefix("R"), PartExclusionRule.ByPrefix("M")]);

    private static PartRoleInView Role(int modelId, string prefix, bool isMainPart = false)
    {
        var result = WithoutInsulationOrFixings.ClassifyProperties(prefix);
        return new PartRoleInView(modelId, $"{prefix}-{modelId}", prefix, result, isMainPart);
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
        public System.Action? OnRead { get; set; }

        public ViewAssemblyOutlineResult GetAssemblyOutline(
            int viewId, OutlineOptions? options = null, IReadOnlyCollection<int>? modelIds = null)
        {
            OnRead?.Invoke();
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
    public void OnlyThePartsTheFilterKeptAreOutlined()
    {
        // The caller states its exclusions once; the selection happens here, from what the
        // parts already carry, so the outline call does not repeat them as ids.
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
    public void IncludedPartsAreReportedBeforeTheirSolidsAreRead()
    {
        var outline = new Outline();
        var callbackRan = false;
        outline.OnRead = () => Assert.True(callbackRan);
        var api = new TeklaDrawingStructuralOutlineApi(new Roles(Role(1, "T"), Role(2, "R")), outline);

        api.Get(7, beforeOutlineRead: included =>
        {
            Assert.Equal(new[] { 1 }, included.Select(part => part.ModelId));
            callbackRan = true;
        });

        Assert.True(callbackRan);
    }

    [Fact]
    public void APartNoExclusionNamesIsMeasuredOverRatherThanBlockingTheExtent()
    {
        // What a prefix table used to make a blocker. An unfamiliar mark is ordinary: the
        // filter names what leaves the set, and nothing else does.
        var outline = new Outline();
        var api = new TeklaDrawingStructuralOutlineApi(new Roles(Role(1, "T"), Role(2, "Q")), outline);

        var result = api.Get(7);

        Assert.Equal([1, 2], outline.Asked);
        Assert.True(result.IsComplete);
        Assert.Null(result.Reservation());
    }

    [Fact]
    public void TheExcludedPartsAreReportedRatherThanJustMissing()
    {
        // An extent that came out short is answered by looking here first.
        var api = new TeklaDrawingStructuralOutlineApi(new Roles(Role(1, "T"), Role(2, "R")), new Outline());

        var result = api.Get(7);

        var excluded = Assert.Single(result.Excluded);
        Assert.Equal(2, excluded.ModelId);
        Assert.Equal("exclude-prefix:R", excluded.Role.RuleId);
    }

    [Fact]
    public void TheMainPartIsCarriedThroughWithoutDecidingAnything()
    {
        // It is the base a secondary part is measured from on a beam or a column and means
        // nothing on a panel. Which of the two this is belongs to the rule set, not here.
        var api = new TeklaDrawingStructuralOutlineApi(
            new Roles(Role(1, "P", isMainPart: true), Role(2, "P")),
            new Outline());

        var result = api.Get(7);

        Assert.Equal([1], result.Included.Where(part => part.IsMainPart).Select(part => part.ModelId));
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
    public void AFailedPropertyReadIsReportedAsUnclassified()
    {
        // "Nobody could look" is its own answer: an exclusion that should have caught this
        // part could not fire, so the set is provisional even though the part is in it.
        var unreadable = new PartRoleInView(2, "T-2", null, PartRoleResult.Unclassified);

        var api = new TeklaDrawingStructuralOutlineApi(
            new Roles([Role(1, "T"), unreadable], [new UnreadPart(2, "PART_PREFIX could not be read")]),
            new Outline());

        var result = api.Get(7);

        Assert.Single(result.Unclassified);
        Assert.Contains("never classified", result.Reservation());
    }

    [Fact]
    public void AnIncludedPartTheViewDoesNotDrawIsNotSilentlyDropped()
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
