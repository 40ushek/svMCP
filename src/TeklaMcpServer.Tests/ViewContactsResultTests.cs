using System;
using System.Collections.Generic;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// What the view contact search does with parts it cannot read. Tested through the half
/// that needs no Tekla, with a stand-in geometry reader, because the danger here is not a
/// crash but a quiet one: a part that never arrives leaves a graph that looks complete.
/// </summary>
public sealed class ViewContactsResultTests
{
    /// <summary>Two slabs stacked so they meet over their whole face.</summary>
    private static PartSolidGeometryInViewResult Slab(int modelId, double z0, double z1)
    {
        var result = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = modelId };

        var corners = new (double X, double Y)[] { (0, 0), (100, 0), (100, 100), (0, 100) };
        var index = 0;

        foreach (var z in new[] { z0, z1 })
            foreach (var (x, y) in corners)
                result.Solid.Vertices.Add(new PartVertexGeometry { Index = index++, Point = [x, y, z] });

        var bottom = new PartFaceGeometry { Index = 0, Normal = [0, 0, -1] };
        bottom.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });

        var top = new PartFaceGeometry { Index = 1, Normal = [0, 0, 1] };
        top.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [4, 5, 6, 7] });

        result.Solid.Faces.Add(bottom);
        result.Solid.Faces.Add(top);

        return result;
    }

    private sealed class Reader : IDrawingPartSolidGeometryApi
    {
        private readonly Dictionary<int, Func<PartSolidGeometryInViewResult>> _byId = new();

        public Reader Returns(int modelId, PartSolidGeometryInViewResult result)
        {
            _byId[modelId] = () => result;
            return this;
        }

        public Reader Fails(int modelId, string error)
        {
            _byId[modelId] = () => new PartSolidGeometryInViewResult { Success = false, ModelId = modelId, Error = error };
            return this;
        }

        public Reader Throws(int modelId, string message)
        {
            _byId[modelId] = () => throw new InvalidOperationException(message);
            return this;
        }

        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => _byId[modelId]();
    }

    private static ViewContactsResult Run(Reader reader, IEnumerable<int> ids, ContactOptions? options = null) =>
        TeklaDrawingViewContactApi.Build(1, ids, reader, options ?? new ContactOptions());

    [Fact]
    public void PartsThatMeetAreFound()
    {
        var reader = new Reader().Returns(10, Slab(10, 0, 50)).Returns(11, Slab(11, 50, 100));

        var result = Run(reader, [10, 11]);

        Assert.Single(result.Graph.Junctions);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void APartWhoseGeometryFailedIsNamedRatherThanMissing()
    {
        var reader = new Reader().Returns(10, Slab(10, 0, 50)).Fails(11, "view 1 not found");

        var result = Run(reader, [10, 11]);

        // Without this the graph says part 10 touches nothing, which reads as a fact.
        var unread = Assert.Single(result.Unread);
        Assert.Equal(11, unread.ModelId);
        Assert.Contains("view 1 not found", unread.Reason);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void AReaderThatThrowsLosesOnePartAndNotTheView()
    {
        var reader = new Reader().Returns(10, Slab(10, 0, 50)).Throws(11, "solid unavailable").Returns(12, Slab(12, 50, 100));

        var result = Run(reader, [10, 11, 12]);

        Assert.Single(result.Graph.Junctions);
        Assert.Contains(result.Unread, part => part.ModelId == 11 && part.Reason.Contains("solid unavailable"));
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void GeometryThatTranslatesToNothingIsAlsoNamed()
    {
        var empty = new PartSolidGeometryInViewResult { Success = true, ViewId = 1, ModelId = 11 };
        var reader = new Reader().Returns(10, Slab(10, 0, 50)).Returns(11, empty);

        var result = Run(reader, [10, 11]);

        Assert.Contains(result.Unread, part => part.ModelId == 11);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void TheGapToleranceGivenIsTheOneUsed()
    {
        // A tenth of a millimetre apart: within the default, outside a tighter setting.
        var reader = new Reader().Returns(10, Slab(10, 0, 50)).Returns(11, Slab(11, 50.1, 100));

        Assert.Single(Run(reader, [10, 11], new ContactOptions { GapTolerance = 1.0 }).Graph.Junctions);
        Assert.Empty(Run(reader, [10, 11], new ContactOptions { GapTolerance = 0.05 }).Graph.Junctions);
    }

    [Fact]
    public void AnEmptyViewIsCompleteRatherThanSuspect()
    {
        var result = Run(new Reader(), []);

        Assert.Empty(result.Graph.Junctions);
        Assert.Null(result.Error);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void AViewThatWasNeverSearchedIsNotTheSameAsAnEmptyOne()
    {
        // An empty view is a finding - nothing there touches anything. No view at all is
        // the absence of a question, and reporting it as complete would let something
        // downstream draw the same conclusion from it.
        var missing = new ViewContactsResult(
            99,
            SolidContacts.ContactGraph.Build(Array.Empty<ISolid>()),
            Array.Empty<UnreadPart>(),
            "view 99 is not on the active drawing");

        Assert.False(missing.IsComplete);
        Assert.Contains("not on the active drawing", missing.Error);
    }
}
