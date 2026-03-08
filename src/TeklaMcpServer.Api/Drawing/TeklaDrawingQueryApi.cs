using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingQueryApi : IDrawingQueryApi
{
    public IReadOnlyList<DrawingInfo> ListDrawings()
    {
        var drawingHandler = new DrawingHandler();
        var drawingEnumerator = drawingHandler.GetDrawings();
        var drawings = new List<DrawingInfo>();

        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;

            drawings.Add(ToDrawingInfo(drawing));
        }

        return drawings;
    }

    public IReadOnlyList<DrawingInfo> FindDrawings(string? nameContains = null, string? markContains = null)
    {
        var drawingHandler = new DrawingHandler();
        var drawingEnumerator = drawingHandler.GetDrawings();
        var drawings = new List<DrawingInfo>();

        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;
            if (!ContainsIgnoreCase(drawing.Name, nameContains))
                continue;
            if (!ContainsIgnoreCase(drawing.Mark, markContains))
                continue;

            drawings.Add(ToDrawingInfo(drawing));
        }

        return drawings;
    }

    public IReadOnlyList<DrawingInfo> FindDrawingsByProperties(IReadOnlyCollection<DrawingPropertyFilter> filters)
    {
        var filterList = filters?.ToList() ?? new List<DrawingPropertyFilter>();
        var drawingHandler = new DrawingHandler();
        var drawingEnumerator = drawingHandler.GetDrawings();
        var drawings = new List<DrawingInfo>();

        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;
            if (!MatchesAllFilters(drawing, filterList))
                continue;

            drawings.Add(ToDrawingInfo(drawing));
        }

        return drawings;
    }

    private static DrawingInfo ToDrawingInfo(Tekla.Structures.Drawing.Drawing drawing)
    {
        return new DrawingInfo
        {
            Guid = drawing.GetIdentifier().GUID.ToString(),
            Name = drawing.Name,
            Mark = drawing.Mark,
            Type = drawing.GetType().Name,
            Status = drawing.UpToDateStatus.ToString()
        };
    }

    private static bool ContainsIgnoreCase(string? source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source!.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesAllFilters(Tekla.Structures.Drawing.Drawing drawing, IReadOnlyCollection<DrawingPropertyFilter> filters)
    {
        foreach (var filter in filters)
        {
            var key = (filter.Property ?? string.Empty).Trim().ToLowerInvariant();
            var value = filter.Value ?? string.Empty;
            var match = key switch
            {
                "name" => string.Equals(drawing.Name ?? string.Empty, value, StringComparison.OrdinalIgnoreCase),
                "mark" => string.Equals(drawing.Mark ?? string.Empty, value, StringComparison.OrdinalIgnoreCase),
                "type" => string.Equals(drawing.GetType().Name, value, StringComparison.OrdinalIgnoreCase),
                "status" => string.Equals(drawing.UpToDateStatus.ToString(), value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (!match)
                return false;
        }

        return true;
    }
}
