using System.Globalization;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Reads one user-defined attribute as a string, trying every storage type GetUserProperty
/// has an overload for. A UDA's storage type (string/integer/double) is fixed by whoever
/// defined the .uda file, and the string overload simply fails - returns false - for
/// anything but a string-typed one. Reporting that as "the UDA does not exist" would be
/// wrong for every plant that defined ZONE (or any other UDA) as an integer, which is the
/// common case for a small closed set of values. Never a report property: GetReportProperty
/// and GetUserProperty read different Tekla data and are not interchangeable.
/// </summary>
internal static class TeklaUserPropertyReader
{
    public static DrawingPartInfoBuilder.PropertyRead ReadAsString(ModelObject mo, string property) =>
        Read(mo.GetUserProperty, mo.GetUserProperty, mo.GetUserProperty, property);

    /// <summary>
    /// A drawing's own attributes (e.g. its ZONE, set from the part it was created for) are
    /// read with Tekla.Structures.Drawing's own GetUserProperty - a different class from the
    /// model-side one above, with the same three overloads, not substitutable for it.
    /// </summary>
    public static DrawingPartInfoBuilder.PropertyRead ReadAsString(
        Tekla.Structures.Drawing.Drawing drawing, string property) =>
        Read(drawing.GetUserProperty, drawing.GetUserProperty, drawing.GetUserProperty, property);

    private delegate bool TryRead<T>(string property, ref T value);

    private static DrawingPartInfoBuilder.PropertyRead Read(
        TryRead<string> readText, TryRead<int> readInt, TryRead<double> readDouble, string property)
    {
        var text = string.Empty;
        if (readText(property, ref text))
            return new DrawingPartInfoBuilder.PropertyRead(true, text);

        var number = 0;
        if (readInt(property, ref number))
            return new DrawingPartInfoBuilder.PropertyRead(true, number.ToString(CultureInfo.InvariantCulture));

        var real = 0.0;
        if (readDouble(property, ref real))
            return new DrawingPartInfoBuilder.PropertyRead(true, real.ToString(CultureInfo.InvariantCulture));

        return new DrawingPartInfoBuilder.PropertyRead(false, string.Empty);
    }
}
