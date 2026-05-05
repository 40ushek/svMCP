using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

        // SetActiveDrawing can fail if Tekla hasn't fully finished closing a previous drawing.
        // Retry up to 3 times with a short pause before each attempt.

        bool opened = false;
        Exception? lastEx = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(500);
            try
            {
                opened = drawingHandler.SetActiveDrawing(targetDrawing);
                lastEx = null;
                break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (IsUpdateRequiredOpenFailure(ex))
                {
                    return new OpenDrawingResult
                    {
                        Found = true,
                        Opened = false,
                        RequiresModelNumbering = true,
                        Error = "The drawing cannot be opened because the model requires numbering before the drawing can be updated.",
                        RequestedGuid = drawingGuid.ToString(),
                        Drawing = ToDrawingInfo(targetDrawing)
                    };
                }
            }
        }

        if (lastEx != null)
            throw new InvalidOperationException($"SetActiveDrawing failed after 3 attempts: {lastEx.Message}", lastEx);

        DrawingReservedAreaReader.InvalidateLayoutCache();
        return new OpenDrawingResult
        {
            Found = true,
            Opened = opened,
            RequestedGuid = drawingGuid.ToString(),
            Drawing = ToDrawingInfo(targetDrawing)
        };
    }

    private static bool IsUpdateRequiredOpenFailure(Exception ex)
    {
        return ex.Message.IndexOf("must be first updated", StringComparison.OrdinalIgnoreCase) >= 0
            || ex.Message.IndexOf("first updated", StringComparison.OrdinalIgnoreCase) >= 0;
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
        DrawingReservedAreaReader.InvalidateLayoutCache();
        var closed = drawingHandler.CloseActiveDrawing(true);
        return new CloseDrawingResult
        {
            HasActiveDrawing = true,
            Closed = closed,
            Drawing = drawingInfo
        };
    }

    public DrawingOperationResult UpdateDrawing(Guid drawingGuid)
    {
        var drawingHandler = new DrawingHandler();
        var drawing = FindDrawingByGuid(drawingHandler, drawingGuid);
        if (drawing == null)
        {
            return new DrawingOperationResult
            {
                Found = false,
                Succeeded = false,
                RequestedGuid = drawingGuid.ToString()
            };
        }

        var updated = drawingHandler.UpdateDrawing(drawing);
        return new DrawingOperationResult
        {
            Found = true,
            Succeeded = updated,
            RequestedGuid = drawingGuid.ToString(),
            Drawing = ToDrawingInfo(drawing)
        };
    }

    public DrawingOperationResult DeleteDrawing(Guid drawingGuid)
    {
        var drawingHandler = new DrawingHandler();
        var drawing = FindDrawingByGuid(drawingHandler, drawingGuid);
        if (drawing == null)
        {
            return new DrawingOperationResult
            {
                Found = false,
                Succeeded = false,
                RequestedGuid = drawingGuid.ToString()
            };
        }

        var drawingInfo = ToDrawingInfo(drawing);
        var deleted = drawing.Delete();
        return new DrawingOperationResult
        {
            Found = true,
            Succeeded = deleted,
            RequestedGuid = drawingGuid.ToString(),
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
        var info = new DrawingInfo
        {
            Guid = drawing.GetIdentifier().GUID.ToString(),
            Name = drawing.Name,
            Mark = drawing.Mark,
            Title1 = drawing.Title1,
            Title2 = drawing.Title2,
            Title3 = drawing.Title3,
            Type = drawing.GetType().Name,
            DrawingType = drawing.DrawingTypeStr,
            Status = drawing.UpToDateStatus.ToString(),
            IsLocked = drawing.IsLocked,
            IsIssued = drawing.IsIssued,
            IsIssuedButModified = drawing.IsIssuedButModified,
            IsFrozen = drawing.IsFrozen,
            IsReadyForIssue = drawing.IsReadyForIssue,
            IsLockedBy = drawing.IsLockedBy,
            IsReadyForIssueBy = drawing.IsReadyForIssueBy,
            CreationDate = FormatDate(drawing.CreationDate),
            ModificationDate = FormatDate(drawing.ModificationDate),
            IssuingDate = FormatDate(drawing.IssuingDate),
            OutputDate = FormatDate(drawing.OutputDate)
        };

        PopulateSourceModelObject(info, drawing);
        return info;
    }

    private static Tekla.Structures.Drawing.Drawing? FindDrawingByGuid(DrawingHandler drawingHandler, Guid drawingGuid)
    {
        var drawingEnumerator = drawingHandler.GetDrawings();
        while (drawingEnumerator.MoveNext())
        {
            if (drawingEnumerator.Current is not Tekla.Structures.Drawing.Drawing drawing)
                continue;
            if (drawing.GetIdentifier().GUID == drawingGuid)
                return drawing;
        }

        return null;
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
        var info = ToDrawingInfo(drawing);
        foreach (var filter in filters)
        {
            var key = (filter.Property ?? string.Empty).Trim().ToLowerInvariant();
            var value = filter.Value ?? string.Empty;
            var actual = GetFilterValue(info, key);
            var match = actual != null && MatchesOperator(actual, filter.Operator, value);

            if (!match)
                return false;
        }

        return true;
    }

    private static void PopulateSourceModelObject(DrawingInfo info, Tekla.Structures.Drawing.Drawing drawing)
    {
        switch (drawing)
        {
            case SinglePartDrawing singlePartDrawing:
                info.SourceModelObjectId = singlePartDrawing.PartIdentifier?.ID;
                info.SourceModelObjectGuid = singlePartDrawing.PartIdentifier?.GUID.ToString();
                info.SourceModelObjectKind = "Part";
                break;

            case AssemblyDrawing assemblyDrawing:
                info.SourceModelObjectId = assemblyDrawing.AssemblyIdentifier?.ID;
                info.SourceModelObjectGuid = assemblyDrawing.AssemblyIdentifier?.GUID.ToString();
                info.SourceModelObjectKind = "Assembly";
                break;
        }
    }

    private static string? GetFilterValue(DrawingInfo drawing, string key)
    {
        return key switch
        {
            "guid" => drawing.Guid,
            "name" => drawing.Name,
            "mark" => drawing.Mark,
            "title1" => drawing.Title1,
            "title2" => drawing.Title2,
            "title3" => drawing.Title3,
            "type" => drawing.Type,
            "drawingtype" or "drawing type" => drawing.DrawingType,
            "status" or "uptodatestatus" or "up to date status" => drawing.Status,
            "sourcemodelobjectid" or "sourceid" or "modelobjectid" => drawing.SourceModelObjectId?.ToString(CultureInfo.InvariantCulture),
            "sourcemodelobjectguid" or "sourceguid" or "modelobjectguid" => drawing.SourceModelObjectGuid,
            "sourcemodelobjectkind" or "sourcekind" => drawing.SourceModelObjectKind,
            "islocked" or "locked" => drawing.IsLocked.ToString(),
            "isissued" or "issued" => drawing.IsIssued.ToString(),
            "isissuedbutmodified" or "issuedbutmodified" => drawing.IsIssuedButModified.ToString(),
            "isfrozen" or "frozen" => drawing.IsFrozen.ToString(),
            "isreadyforissue" or "readyforissue" => drawing.IsReadyForIssue.ToString(),
            "islockedby" or "lockedby" => drawing.IsLockedBy,
            "isreadyforissueby" or "readyforissueby" => drawing.IsReadyForIssueBy,
            "creationdate" or "created" => drawing.CreationDate,
            "modificationdate" or "modified" => drawing.ModificationDate,
            "issuingdate" => drawing.IssuingDate,
            "outputdate" => drawing.OutputDate,
            _ => null
        };
    }

    private static bool MatchesOperator(string actual, string? operatorName, string expected)
    {
        var op = NormalizeOperator(operatorName);
        var comparison = StringComparison.OrdinalIgnoreCase;

        return op switch
        {
            "equals" => string.Equals(actual, expected, comparison),
            "not_equals" => !string.Equals(actual, expected, comparison),
            "contains" => actual.IndexOf(expected, comparison) >= 0,
            "not_contains" => actual.IndexOf(expected, comparison) < 0,
            "starts_with" => actual.StartsWith(expected, comparison),
            "ends_with" => actual.EndsWith(expected, comparison),
            _ => false
        };
    }

    private static string NormalizeOperator(string? operatorName)
    {
        var op = (operatorName ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return op switch
        {
            "" or "=" or "==" or "is_equal" or "equal" => "equals",
            "!=" or "<>" or "is_not_equal" or "not_equal" => "not_equals",
            "contains" => "contains",
            "notcontains" or "not_contains" => "not_contains",
            "startswith" or "starts_with" => "starts_with",
            "endswith" or "ends_with" => "ends_with",
            _ => op
        };
    }

    private static string? FormatDate(DateTime value)
    {
        return value == DateTime.MinValue
            ? null
            : value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }
}
