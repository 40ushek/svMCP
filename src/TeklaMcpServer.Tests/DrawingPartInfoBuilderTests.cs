using System;
using System.Collections.Generic;
using TeklaMcpServer.Api.Drawing;
using Xunit;

namespace TeklaMcpServer.Tests;

/// <summary>
/// `get_drawing_parts` is where a person reads the marks and materials an exclusion filter
/// is written from. Both failures below produce a full answer of blanks rather than an
/// error, and a filter written from blanks matches nothing while looking like one that
/// found nothing to remove - the insulation goes back into the extent and nobody is told.
/// </summary>
public sealed class DrawingPartInfoBuilderTests
{
    private sealed class Reader
    {
        private readonly Dictionary<string, DrawingPartInfoBuilder.PropertyRead> _values;

        public Reader(Dictionary<string, DrawingPartInfoBuilder.PropertyRead> values) => _values = values;

        public bool Selected { get; private set; }
        public bool ReadBeforeSelect { get; private set; }

        public void Select() => Selected = true;

        public DrawingPartInfoBuilder.PropertyRead Read(string property)
        {
            if (!Selected)
                ReadBeforeSelect = true;

            return _values.TryGetValue(property, out var value)
                ? value
                : new DrawingPartInfoBuilder.PropertyRead(true, string.Empty);
        }
    }

    private static Reader WithProperties(params (string Property, string Value)[] properties)
    {
        var values = new Dictionary<string, DrawingPartInfoBuilder.PropertyRead>();
        foreach (var (property, value) in properties)
            values[property] = new DrawingPartInfoBuilder.PropertyRead(true, value);

        return new Reader(values);
    }

    [Fact]
    public void ThePartIsFetchedFromTheModelBeforeAnyPropertyIsRead()
    {
        // Without the select every read answers blank. This path shipped without it and the
        // prefixes it returned would all have been empty.
        var reader = WithProperties(("PART_POS", "T-368"), ("PART_PREFIX", "T"));

        DrawingPartInfoBuilder.Build(5, "Beam", reader.Select, reader.Read);

        Assert.True(reader.Selected);
        Assert.False(reader.ReadBeforeSelect);
    }

    [Fact]
    public void AReadPrefixIsCarriedWithItsReadState()
    {
        var reader = WithProperties(("PART_PREFIX", "T"), ("MATERIAL", "C24"));

        var info = DrawingPartInfoBuilder.Build(5, "Beam", reader.Select, reader.Read);

        Assert.Equal("T", info.PartPrefix);
        Assert.True(info.PartPrefixKnown);
        Assert.Equal("C24", info.Material);
        Assert.Equal(5, info.ModelId);
        Assert.Equal("Beam", info.Type);
    }

    [Fact]
    public void AnUnreadablePrefixIsNotReportedAsAnEmptyOne()
    {
        // The distinction the filter depends on: "this part has no prefix" is something to
        // act on, "Tekla would not say" is something to fix first.
        var reader = new Reader(new Dictionary<string, DrawingPartInfoBuilder.PropertyRead>
        {
            ["PART_PREFIX"] = new(false, string.Empty),
            ["PART_POS"] = new(true, "T-368")
        });

        var info = DrawingPartInfoBuilder.Build(5, "Beam", reader.Select, reader.Read);

        Assert.False(info.PartPrefixKnown);
        Assert.Equal(string.Empty, info.PartPrefix);
        Assert.Equal("T-368", info.PartPos);
    }

    [Fact]
    public void APartWithNoPrefixAtAllIsStillAKnownAnswer()
    {
        var reader = WithProperties(("PART_PREFIX", ""));

        var info = DrawingPartInfoBuilder.Build(5, "Beam", reader.Select, reader.Read);

        Assert.True(info.PartPrefixKnown);
        Assert.Equal(string.Empty, info.PartPrefix);
    }

    [Fact]
    public void ABuilderWithoutASelectOrAReaderIsRefused()
    {
        var reader = WithProperties();

        Assert.Throws<ArgumentNullException>(() => DrawingPartInfoBuilder.Build(1, "Beam", null!, reader.Read));
        Assert.Throws<ArgumentNullException>(() => DrawingPartInfoBuilder.Build(1, "Beam", reader.Select, null!));
    }
}
