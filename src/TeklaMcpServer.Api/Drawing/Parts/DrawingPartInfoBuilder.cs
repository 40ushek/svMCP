using System;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Assembles one part's reported properties, with no Tekla type in sight.
///
/// Split out of the reader for two reasons a live-only method could not carry. The first
/// is the select: report properties come back empty unless the object is fetched from the
/// model first, and forgetting it produces a full answer of blank strings rather than an
/// error - the failure this class exists to pin, since it happened here and the blank
/// prefixes would have gone straight into an exclusion filter that then matched nothing.
/// The second is that "empty" and "could not be read" are different answers and only the
/// caller of <c>GetReportProperty</c> can tell them apart.
/// </summary>
public static class DrawingPartInfoBuilder
{
    /// <summary>What one report property read returned.</summary>
    public readonly struct PropertyRead
    {
        public PropertyRead(bool read, string value)
        {
            Read = read;
            Value = value ?? string.Empty;
        }

        /// <summary>False when Tekla refused the property. Not the same as an empty value.</summary>
        public bool Read { get; }

        public string Value { get; }
    }

    /// <summary>
    /// <paramref name="select"/> is invoked once, before any read. It is a parameter rather
    /// than a step inside the reader so that the order can be held by a test: every read
    /// after a missed select answers blank, and a drawing full of blank prefixes reads
    /// exactly like a model whose parts have none.
    /// </summary>
    /// <summary>
    /// <paramref name="readUserProperty"/> is separate from <paramref name="read"/>: ZONE is
    /// a user-defined attribute, read with GetUserProperty, not a report property, and the
    /// two Tekla calls are not interchangeable. Omitting it (null) answers ZONE as unread -
    /// the same fail-closed default as a model that has no such UDA at all.
    /// </summary>
    public static DrawingPartInfo Build(
        int modelId,
        string type,
        Action select,
        Func<string, PropertyRead> read,
        Func<string, PropertyRead>? readUserProperty = null)
    {
        if (select == null) throw new ArgumentNullException(nameof(select));
        if (read == null) throw new ArgumentNullException(nameof(read));

        select();

        var prefix = read("PART_PREFIX");
        var zone = readUserProperty?.Invoke("ZONE") ?? new PropertyRead(false, string.Empty);

        return new DrawingPartInfo
        {
            ModelId = modelId,
            Type = type,
            PartPos = read("PART_POS").Value,
            PartPrefix = prefix.Value,
            PartPrefixKnown = prefix.Read,
            AssemblyPos = read("ASSEMBLY_POS").Value,
            Profile = read("PROFILE").Value,
            Material = read("MATERIAL").Value,
            Name = read("NAME").Value,
            Zone = zone.Value,
            ZoneKnown = zone.Read
        };
    }
}
