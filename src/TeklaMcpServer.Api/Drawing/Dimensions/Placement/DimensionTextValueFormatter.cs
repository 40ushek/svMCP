using System.Collections.Concurrent;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionTextValueFormatter
{
    private readonly struct DimensionFormatCacheKey : System.IEquatable<DimensionFormatCacheKey>
    {
        public DimensionFormatCacheKey(
            DimensionSetBaseAttributes.DimensionValueUnits unit,
            DimensionSetBaseAttributes.DimensionValueFormats format,
            DimensionSetBaseAttributes.DimensionValuePrecisions precision,
            double distance)
        {
            Unit = unit;
            Format = format;
            Precision = precision;
            Distance = distance;
        }

        public DimensionSetBaseAttributes.DimensionValueUnits Unit { get; }
        public DimensionSetBaseAttributes.DimensionValueFormats Format { get; }
        public DimensionSetBaseAttributes.DimensionValuePrecisions Precision { get; }
        public double Distance { get; }

        public bool Equals(DimensionFormatCacheKey other)
        {
            return Unit == other.Unit
                && Format == other.Format
                && Precision == other.Precision
                && Distance.Equals(other.Distance);
        }

        public override bool Equals(object? obj) => obj is DimensionFormatCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Unit;
                hashCode = (hashCode * 397) ^ (int)Format;
                hashCode = (hashCode * 397) ^ (int)Precision;
                hashCode = (hashCode * 397) ^ Distance.GetHashCode();
                return hashCode;
            }
        }
    }

    private static readonly ConcurrentDictionary<DimensionFormatCacheKey, string> FormattedValueCache = new();

    internal static string? TryFormatMeasuredValue(
        StraightDimension segment,
        StraightDimensionSet dimSet,
        View view)
    {
        if (!TryGetFormat(dimSet, out var format))
            return null;

        var distance = System.Math.Sqrt(
            System.Math.Pow(segment.EndPoint.X - segment.StartPoint.X, 2) +
            System.Math.Pow(segment.EndPoint.Y - segment.StartPoint.Y, 2));
        if (distance <= 1e-6)
            return null;

        var cacheKey = new DimensionFormatCacheKey(
            format.Unit,
            format.Format,
            format.Precision,
            distance);

        try
        {
            return FormattedValueCache.GetOrAdd(
                cacheKey,
                _ => TryGetTemporaryDimensionText(view, format, distance) ?? string.Empty);
        }
        catch
        {
            return TryGetTemporaryDimensionText(view, format, distance);
        }
    }

    internal static string NormalizeTemporaryValue(
        string? rawValue,
        DimensionSetBaseAttributes.DimensionValueUnits dimensionUnits,
        bool negative)
    {
        if (string.IsNullOrEmpty(rawValue))
            return string.Empty;

        var normalized = rawValue!.Length > 4
            ? rawValue.Substring(2, rawValue.Length - 4)
            : "0";

        if (negative)
            normalized = "-" + normalized;

        if (dimensionUnits == DimensionSetBaseAttributes.DimensionValueUnits.Inch
            && !normalized.Contains('"')
            && (normalized.Contains('-') || normalized.Contains('\\') || !normalized.Contains('\'')))
        {
            normalized += "\"";
        }

        return normalized;
    }

    internal static bool TryGetFormat(
        StraightDimensionSet dimSet,
        out DimensionSetBaseAttributes.DimensionFormatAttributes format)
    {
        format = default!;
        try
        {
            if (dimSet.Attributes is StraightDimensionSet.StraightDimensionSetAttributes attributes)
            {
                format = attributes.Format;
                return true;
            }

            var attributesProperty = dimSet.GetType()
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(static property => property.Name == "Attributes")
                .OrderByDescending(static property => property.PropertyType == typeof(StraightDimensionSet.StraightDimensionSetAttributes))
                .FirstOrDefault();
            var reflectedAttributes = attributesProperty?.GetValue(dimSet, null);
            var formatProperty = reflectedAttributes?.GetType().GetProperty("Format");
            if (formatProperty?.GetValue(reflectedAttributes, null) is DimensionSetBaseAttributes.DimensionFormatAttributes reflectedFormat)
            {
                format = reflectedFormat;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? TryGetTemporaryDimensionText(
        View view,
        DimensionSetBaseAttributes.DimensionFormatAttributes format,
        double distance)
    {
        StraightDimensionSet? temporaryDimensionSet = null;
        StraightDimension? temporarySegment = null;
        try
        {
#pragma warning disable CS0618
            var temporaryAttributes = new StraightDimensionSet.StraightDimensionSetAttributes();
#pragma warning restore CS0618
            temporaryAttributes.Format = format;

            var pointList = new PointList
            {
                new Point(0.0, 0.0, 0.0),
                new Point(distance, 0.0, 0.0)
            };

            temporaryDimensionSet = new StraightDimensionSetHandler().CreateDimensionSet(
                view,
                pointList,
                new Vector(0.0, 0.1, 0.0),
                0.0,
                temporaryAttributes);
            if (temporaryDimensionSet == null)
                return null;

            var objects = temporaryDimensionSet.GetObjects();
            while (objects.MoveNext())
            {
                if (objects.Current is StraightDimension straightDimension)
                {
                    temporarySegment = straightDimension;
                    break;
                }
            }

            if (temporarySegment == null)
                return null;

            var rawValue = temporarySegment.Value?.GetUnformattedString();
            return NormalizeTemporaryValue(rawValue, format.Unit, negative: false);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (temporarySegment != null)
            {
                try
                {
                    temporarySegment.Delete();
                }
                catch
                {
                }
            }

            if (temporaryDimensionSet != null)
            {
                try
                {
                    temporaryDimensionSet.Delete();
                }
                catch
                {
                }
            }
        }
    }
}
