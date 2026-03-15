using System;
using System.Collections.Generic;
using System.IO;
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

    public OpenDrawingResult OpenDrawing(Guid drawingGuid)
    {
        var drawingHandler = new DrawingHandler();

        // Save and close the currently active drawing before switching to avoid data loss.
        // CommitChanges() only updates Tekla's in-memory state; CloseActiveDrawing(save:true)
        // is required to persist changes to disk.
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing != null)
            drawingHandler.CloseActiveDrawing(true);

        var drawingEnumerator = drawingHandler.GetDrawings();
        Tekla.Structures.Drawing.Drawing? targetDrawing = null;

        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;
            if (drawing.GetIdentifier().GUID != drawingGuid)
                continue;

            targetDrawing = drawing;
            break;
        }

        if (targetDrawing == null)
        {
            return new OpenDrawingResult
            {
                Found = false,
                Opened = false,
                RequestedGuid = drawingGuid.ToString()
            };
        }

        var opened = drawingHandler.SetActiveDrawing(targetDrawing);
        return new OpenDrawingResult
        {
            Found = true,
            Opened = opened,
            RequestedGuid = drawingGuid.ToString(),
            Drawing = ToDrawingInfo(targetDrawing)
        };
    }

    public CloseDrawingResult CloseActiveDrawing()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
        {
            return new CloseDrawingResult
            {
                HasActiveDrawing = false,
                Closed = false
            };
        }

        var drawingInfo = ToDrawingInfo(activeDrawing);
        var closed = drawingHandler.CloseActiveDrawing(true);
        return new CloseDrawingResult
        {
            HasActiveDrawing = true,
            Closed = closed,
            Drawing = drawingInfo
        };
    }

    public ExportDrawingsPdfResult ExportDrawingsPdf(IReadOnlyCollection<string> drawingGuids, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var requestedGuids = drawingGuids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var drawingHandler = new DrawingHandler();
        var drawingEnumerator = drawingHandler.GetDrawings();
        var exportedFiles = new List<string>();
        var failedToExport = new List<string>();
        var foundGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var printAttributes = new DPMPrinterAttributes
        {
            PrinterName = "Microsoft Print to PDF",
            OutputType = DotPrintOutputType.PDF
        };

        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;

            var guid = drawing.GetIdentifier().GUID.ToString();
            if (!requestedGuids.Contains(guid))
                continue;

            foundGuids.Add(guid);
            var fileName = $"{SanitizeFileName(drawing.Name)}_{SanitizeFileName(drawing.Mark)}.pdf";
            var filePath = Path.Combine(outputDirectory, fileName);

            if (drawingHandler.PrintDrawing(drawing, printAttributes, filePath))
                exportedFiles.Add(filePath);
            else
                failedToExport.Add(guid);
        }

        var missingGuids = requestedGuids.Where(g => !foundGuids.Contains(g)).ToList();
        return new ExportDrawingsPdfResult
        {
            ExportedFiles = exportedFiles,
            FailedToExport = failedToExport,
            MissingGuids = missingGuids,
            OutputDirectory = outputDirectory
        };
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

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }
}
