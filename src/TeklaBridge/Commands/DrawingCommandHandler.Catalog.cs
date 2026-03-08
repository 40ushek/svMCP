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
            WriteDrawingFailure("Failed to open drawing", result.RequestedGuid);
            return true;
        }

        WriteOpenedDrawing(
            result.RequestedGuid,
            result.Drawing.Name,
            result.Drawing.Mark,
            result.Drawing.Type);
        return true;
    }

    private bool HandleCloseDrawing(TeklaDrawingQueryApi api)
    {
        var result = api.CloseActiveDrawing();
        if (!result.HasActiveDrawing)
        {
            WriteNoActiveDrawingError();
            return true;
        }

        if (!result.Closed)
        {
            WriteDrawingFailure(
                "Failed to close active drawing",
                result.Drawing.Guid,
                result.Drawing.Name,
                result.Drawing.Mark,
                result.Drawing.Type);
            return true;
        }

        WriteClosedDrawing(
            result.Drawing.Guid,
            result.Drawing.Name,
            result.Drawing.Mark,
            result.Drawing.Type);
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
        string? type)
    {
        WriteJson(new
        {
            error,
            guid,
            name,
            mark,
            type
        });
    }

    private void WriteOpenedDrawing(string? guid, string? name, string? mark, string? type)
    {
        WriteJson(new
        {
            opened = true,
            guid,
            name,
            mark,
            type
        });
    }

    private void WriteClosedDrawing(string? guid, string? name, string? mark, string? type)
    {
        WriteJson(new
        {
            closed = true,
            guid,
            name,
            mark,
            type
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

    private void WriteNoActiveDrawingError()
    {
        WriteRawJson(NoActiveDrawingErrorJson);
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
                type = d.Type
            });
        }

        return drawings.Select(d => (object)new
        {
            guid = d.Guid,
            name = d.Name,
            mark = d.Mark,
            type = d.Type,
            status = d.Status
        });
    }
}
