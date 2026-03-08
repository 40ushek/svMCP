using System.Linq;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool TryHandleMarkCommands(string command, string[] args)
    {
        TeklaDrawingMarkApi? api = null;
        TeklaDrawingMarkApi GetMarkApi() => api ??= new TeklaDrawingMarkApi(_model);

        switch (command)
        {
            case "arrange_marks":
                return HandleArrangeMarks(GetMarkApi(), args);

            case "create_part_marks":
                return HandleCreatePartMarks(GetMarkApi(), args);

            case "delete_all_marks":
                return HandleDeleteAllMarks(GetMarkApi());

            case "resolve_mark_overlaps":
                return HandleResolveMarkOverlaps(GetMarkApi(), args);

            case "get_drawing_marks":
                return HandleGetDrawingMarks(GetMarkApi(), args);

            default:
                return false;
        }
    }

    private bool HandleArrangeMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseArrangeMarksGap(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.ArrangeMarks(parseResult.Value);
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleCreatePartMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var parseRequest = DrawingCommandParsers.ParseCreatePartMarksRequest(args);
        var result = api.CreatePartMarks(
            parseRequest.ContentAttributesCsv,
            parseRequest.MarkAttributesFile,
            parseRequest.FrameType,
            parseRequest.ArrowheadType);
        WriteCreatePartMarksResult(result);
        return true;
    }

    private bool HandleDeleteAllMarks(TeklaDrawingMarkApi api)
    {
        if (!EnsureActiveDrawingShort())
        {
            return true;
        }

        var result = api.DeleteAllMarks();
        WriteDeleteAllMarksResult(result);
        return true;
    }

    private bool HandleResolveMarkOverlaps(TeklaDrawingMarkApi api, string[] args)
    {
        var parseResult = DrawingCommandParsers.ParseResolveMarkOverlapsMargin(args);
        if (!parseResult.IsValid)
        {
            WriteError(parseResult.Error);
            return true;
        }

        var result = api.ResolveMarkOverlaps(parseResult.Value);
        WriteMarkArrangementResult(result);
        return true;
    }

    private bool HandleGetDrawingMarks(TeklaDrawingMarkApi api, string[] args)
    {
        var viewId = DrawingCommandParsers.ParseOptionalViewId(args);
        var result = api.GetMarks(viewId);
        WriteGetMarksResult(result);
        return true;
    }

    private void WriteMarkArrangementResult(ResolveMarksResult result)
    {
        WriteJson(new
        {
            marksMovedCount = result.MarksMovedCount,
            movedIds = result.MovedIds,
            iterations = result.Iterations,
            remainingOverlaps = result.RemainingOverlaps
        });
    }

    private void WriteCreatePartMarksResult(CreateMarksResult result)
    {
        WriteJson(new
        {
            createdCount = result.CreatedCount,
            skippedCount = result.SkippedCount,
            createdMarkIds = result.CreatedMarkIds,
            attributesLoaded = result.AttributesLoaded
        });
    }

    private void WriteDeleteAllMarksResult(DeleteAllMarksResult result)
    {
        WriteJson(new
        {
            deletedCount = result.DeletedCount
        });
    }

    private void WriteGetMarksResult(GetMarksResult result)
    {
        WriteJson(new
        {
            total = result.Total,
            overlaps = result.Overlaps.Select(o => new { idA = o.IdA, idB = o.IdB }),
            marks = result.Marks.Select(m => new
            {
                id = m.Id,
                viewId = m.ViewId,
                modelId = m.ModelId,
                insertionX = m.InsertionX,
                insertionY = m.InsertionY,
                bbox = new { minX = m.BboxMinX, minY = m.BboxMinY, maxX = m.BboxMaxX, maxY = m.BboxMaxY },
                placingType = m.PlacingType,
                placingX = m.PlacingX,
                placingY = m.PlacingY,
                properties = m.Properties.Select(p => new { name = p.Name, value = p.Value })
            })
        });
    }
}
