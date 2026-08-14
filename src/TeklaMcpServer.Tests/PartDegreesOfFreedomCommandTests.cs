using System;
using System.IO;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// `get_part_degrees_of_freedom` is wired at two independent points - the outer
/// command-group switch and the inner geometry switch - and forgetting either still
/// compiles: the call falls through the outer default and the command does not exist at
/// runtime. Pinned as a source-text contract for the same reason
/// `get_dimension_defects` and `get_structural_chain_positions` are.
/// </summary>
public sealed class PartDegreesOfFreedomCommandTests
{
    private const string Command = "get_part_degrees_of_freedom";

    private static string GeometryHandlerSource() => File.ReadAllText(
        Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.Geometry.cs"));

    [Fact]
    public void OuterCommandGroupSwitchRoutesToGeometryCommands()
    {
        var path = Path.Combine(BridgeTestHelpers.FindRepoRoot(), "src", "TeklaBridge", "Commands", "DrawingCommandHandler.cs");
        var text = File.ReadAllText(path);

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return TryHandleGeometryCommands(command, args);", text);
    }

    [Fact]
    public void InnerGeometrySwitchHandlesTheCommand()
    {
        var text = GeometryHandlerSource();

        Assert.Contains($"case \"{Command}\":", text);
        Assert.Contains("return HandleGetPartDegreesOfFreedom(args);", text);
        Assert.Contains("private bool HandleGetPartDegreesOfFreedom(", text);
    }

    [Fact]
    public void AMissingOrUnparsableViewIdIsRejectedBeforeTeklaIsTouched()
    {
        Assert.Contains("args.Length < 2 || !int.TryParse(args[1], out var viewId)", GeometryHandlerSource());
        Assert.Contains($"WriteError(\"{Command} requires viewId argument\")", GeometryHandlerSource());
    }

    [Fact]
    public void ItReadsContactsRatherThanTheRoleDependentChainCalculation()
    {
        // The whole point of this command is to answer on a drawing where no part is
        // classified as Defining and get_structural_chain_positions refuses outright - see
        // ROADMAP_PART_FREEDOM.md, "what it must not depend on". Pinning the call confirms
        // it goes through the contact pipeline, not through anything role-shaped.
        var text = GeometryHandlerSource();
        var start = text.IndexOf("private bool HandleGetPartDegreesOfFreedom(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = text.IndexOf("\n    private", start + 1, StringComparison.Ordinal);
        var body = text[start..(end > 0 ? end : text.Length)];

        Assert.Contains("new TeklaDrawingViewContactApi(_model).GetContactGraph(", body);
        Assert.Contains("ContactGeometryInViewBuilder.Build(contacts)", body);
        Assert.Contains("PartDegreesOfFreedomAnalyzer.Build(contacts, geometry)", body);
        Assert.DoesNotContain("StructuralOutline", body);
        Assert.DoesNotContain("PartRoleClassifier", body);
    }

    [Fact]
    public void TheAnswerCarriesCompletenessBesideThePerPartReading()
    {
        var text = GeometryHandlerSource();

        Assert.Contains("isComplete = result.IsComplete", text);
        // Not "isSaturated" - see PartDegreesOfFreedomAnalyzer.AllPartsHaveObservedAxisContacts.
        // Without a datum this cannot claim the assembly is fixed, and the field name must
        // not either.
        Assert.Contains("allPartsHaveObservedAxisContacts = result.AllPartsHaveObservedAxisContacts", text);
        Assert.Contains("freeRotation = part.FreeRotation", text);
    }

    [Fact]
    public void NothingExcludedFromAVerdictIsHiddenFromTheResponse()
    {
        // A contact that touched but was not counted (wrong kind, or a gap/overlap rather
        // than a settled touch), or one whose direction could not be read, must be visible
        // - not folded silently into "nothing here". Same for the four ways a search itself
        // can be short of the whole view.
        var text = GeometryHandlerSource();

        Assert.Contains("notLoadBearing = result.NotLoadBearing", text);
        Assert.Contains("ambiguousDirection = result.AmbiguousDirection", text);
        Assert.Contains("unflattened = result.Unflattened", text);
        Assert.Contains("unresolved = result.Unresolved", text);
        Assert.Contains("failures = result.Failures", text);
    }

    [Fact]
    public void AnExclusionNamesWhichGuardStoppedItNotJustTheShapeItLeftBehind()
    {
        // shapeKind (Segment/Polygon) says what the flattened patch looks like, not why it
        // was excluded - a Segment can be excluded for its kind, its state, or neither.
        var text = GeometryHandlerSource();

        Assert.Contains("contactKind = shape.Kind.ToString()", text);
        Assert.Contains("contactState = shape.State.ToString()", text);
    }

    [Fact]
    public void WhatConstrainsEachAxisIsNamedNotJustAPartnerId()
    {
        // The roadmap promises "what constrains each" - a partner id alone does not say
        // which contact made that claim, so a reader could not go check it on the drawing.
        var text = GeometryHandlerSource();

        Assert.Contains("constrainedByX = part.ConstrainedByX.Select(Describe)", text);
        Assert.Contains("constrainedByY = part.ConstrainedByY.Select(Describe)", text);
        Assert.Contains("private static object Describe(FreedomConstraint constraint)", text);
        Assert.Contains("contactId = constraint.ContactId", text);
        Assert.Contains("shapeId = constraint.ShapeId", text);
    }
}
