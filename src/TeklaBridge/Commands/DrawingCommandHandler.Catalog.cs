using System.Collections.Generic;
using System.IO;
using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleDrawingCatalogCommands(string command, string[] args)
    {
        var api = new TeklaDrawingQueryApi();

        switch (command)
        {
            case "list_drawings":
                return HandleListDrawings(api);

            case "find_drawings":
                return HandleFindDrawings(api, args);

            case "open_drawing":
                return HandleOpenDrawing(api, args);

            case "close_drawing":
                return HandleCloseDrawing(api);

            case "update_drawing":
                return HandleUpdateDrawing(api, args);

            case "delete_drawing":
                return HandleDeleteDrawing(api, args);

            case "export_drawings_pdf":
                return HandleExportDrawingsPdf(api, args);

            case "find_drawings_by_properties":
                return HandleFindDrawingsByProperties(api, args);

            default:
                return false;
        }
    }

    private bool HandleListDrawings(TeklaDrawingQueryApi api)
    {
        var drawings = MapBasicDrawings(api.ListDrawings());
        WriteDrawingsList(drawings);
        return true;
    }

    private bool HandleFindDrawings(TeklaDrawingQueryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseFindDrawingsRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var drawings = MapBasicDrawings(
            api.FindDrawings(parseResult.Request.NameContains, parseResult.Request.MarkContains));
        WriteDrawingsList(drawings);
        return true;
    }

    private bool HandleOpenDrawing(TeklaDrawingQueryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseOpenDrawingRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.OpenDrawing(parseResult.Request.RequestedGuid);
        if (!result.Found)
        {
            WriteDrawingFailure("Drawing not found", result.RequestedGuid);
            return true;
        }

        if (!result.Opened)
        {
            if (result.RequiresModelNumbering)
            {
                WriteJson(new
                {
                    error = result.Error,
                    requiresModelNumbering = true,
                    guid = result.RequestedGuid,
                    name = result.Drawing.Name,
                    mark = result.Drawing.Mark,
                    type = result.Drawing.Type,
                    status = result.Drawing.Status
                });
                return true;
            }

            WriteDrawingFailure("Failed to open drawing", result.RequestedGuid);
            return true;
        }

        WriteOpenedDrawing(
            result.RequestedGuid,
            result.Drawing.Name,
            result.Drawing.Mark,
            result.Drawing.Type,
            result.Drawing.Title1,
            result.Drawing.Title2,
            result.Drawing.Title3);
        return true;
    }

    private bool HandleCloseDrawing(TeklaDrawingQueryApi api)
    {
        var result = api.CloseActiveDrawing();
        if (!result.HasActiveDrawing)
        {
            WriteRawJson(NoActiveDrawingErrorJson);
            return true;
        }

        if (!result.Closed)
        {
            WriteDrawingFailure(
                "Failed to close active drawing",
                result.Drawing.Guid,
                result.Drawing.Name,
                result.Drawing.Mark,
                result.Drawing.Type,
                result.Drawing.Title1,
                result.Drawing.Title2,
                result.Drawing.Title3);
            return true;
        }

        WriteClosedDrawing(
            result.Drawing.Guid,
            result.Drawing.Name,
            result.Drawing.Mark,
            result.Drawing.Type,
            result.Drawing.Title1,
            result.Drawing.Title2,
            result.Drawing.Title3);
        return true;
    }

    private bool HandleUpdateDrawing(TeklaDrawingQueryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseOpenDrawingRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.UpdateDrawing(parseResult.Request.RequestedGuid);
        WriteDrawingOperationResult("updated", result);
        return true;
    }

    private bool HandleDeleteDrawing(TeklaDrawingQueryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseOpenDrawingRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.DeleteDrawing(parseResult.Request.RequestedGuid);
        WriteDrawingOperationResult("deleted", result);
        return true;
    }

    private bool HandleExportDrawingsPdf(TeklaDrawingQueryApi api, string[] args)
    {
        var modelInfo = _model.GetInfo();
        var defaultOutputDirectory = Path.Combine(modelInfo.ModelPath, "PlotFiles");
        var parseResult = DrawingCommandParsers.ParseExportDrawingsPdfRequest(args, defaultOutputDirectory);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.ExportDrawingsPdf(
            parseResult.Request.RequestedGuids,
            parseResult.Request.OutputDirectory);
        WriteExportDrawingsPdfResult(result);
        return true;
    }

    private bool HandleFindDrawingsByProperties(TeklaDrawingQueryApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseFindDrawingsByPropertiesRequest(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var drawings = MapBasicDrawings(
            api.FindDrawingsByProperties(parseResult.Request.Filters),
            includeStatus: true);
        WriteDrawingsList(drawings);
        return true;
    }

    private void WriteDrawingsList(IEnumerable<object> drawings)
    {
        WriteJson(drawings);
    }

    private void WriteDrawingFailure(string error, string? guid)
    {
        WriteJson(new
        {
            error,
            guid
        });
    }

    private void WriteDrawingFailure(
        string error,
        string? guid,
        string? name,
        string? mark,
        string? type,
        string? title1 = null,
        string? title2 = null,
        string? title3 = null)
    {
        WriteJson(new
        {
            error,
            guid,
            name,
            mark,
            type,
            title1,
            title2,
            title3
        });
    }

    private void WriteOpenedDrawing(string? guid, string? name, string? mark, string? type, string? title1, string? title2, string? title3)
    {
        WriteJson(new
        {
            opened = true,
            guid,
            name,
            mark,
            type,
            title1,
            title2,
            title3
        });
    }

    private void WriteClosedDrawing(string? guid, string? name, string? mark, string? type, string? title1, string? title2, string? title3)
    {
        WriteJson(new
        {
            closed = true,
            guid,
            name,
            mark,
            type,
            title1,
            title2,
            title3
        });
    }

    private void WriteExportDrawingsPdfResult(ExportDrawingsPdfResult result)
    {
        WriteJson(new
        {
            exportedCount = result.ExportedFiles.Count,
            exportedFiles = result.ExportedFiles,
            failedToExport = result.FailedToExport,
            missingGuids = result.MissingGuids,
            outputDirectory = result.OutputDirectory
        });
    }

    private void WriteDrawingOperationResult(string operation, DrawingOperationResult result)
    {
        if (!result.Found)
        {
            WriteJson(new
            {
                error = "Drawing not found",
                guid = result.RequestedGuid,
                operation
            });
            return;
        }

        WriteJson(new
        {
            operation,
            succeeded = result.Succeeded,
            guid = result.Drawing.Guid,
            name = result.Drawing.Name,
            mark = result.Drawing.Mark,
            type = result.Drawing.Type,
            drawingType = result.Drawing.DrawingType,
            status = result.Drawing.Status,
            sourceModelObjectId = result.Drawing.SourceModelObjectId,
            sourceModelObjectKind = result.Drawing.SourceModelObjectKind
        });
    }

    private static IEnumerable<object> MapBasicDrawings(
        IEnumerable<DrawingInfo> drawings,
        bool includeStatus = false)
    {
        if (!includeStatus)
        {
            return drawings.Select(d => (object)new
            {
                guid = d.Guid,
                name = d.Name,
                mark = d.Mark,
                title1 = d.Title1,
                title2 = d.Title2,
                title3 = d.Title3,
                type = d.Type
            });
        }

        return drawings.Select(d => (object)new
        {
            guid = d.Guid,
            name = d.Name,
            mark = d.Mark,
            title1 = d.Title1,
            title2 = d.Title2,
            title3 = d.Title3,
            type = d.Type,
            drawingType = d.DrawingType,
            status = d.Status,
            sourceModelObjectId = d.SourceModelObjectId,
            sourceModelObjectGuid = d.SourceModelObjectGuid,
            sourceModelObjectKind = d.SourceModelObjectKind,
            isLocked = d.IsLocked,
            isIssued = d.IsIssued,
            isIssuedButModified = d.IsIssuedButModified,
            isFrozen = d.IsFrozen,
            isReadyForIssue = d.IsReadyForIssue,
            isLockedBy = d.IsLockedBy,
            isReadyForIssueBy = d.IsReadyForIssueBy,
            creationDate = d.CreationDate,
            modificationDate = d.ModificationDate,
            issuingDate = d.IssuingDate,
            outputDate = d.OutputDate
        });
    }
}
