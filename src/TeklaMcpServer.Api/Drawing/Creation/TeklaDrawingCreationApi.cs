using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingCreationApi : IDrawingCreationApi
{
    private readonly Model _model;

    public TeklaDrawingCreationApi(Model model) => _model = model;

    public DrawingCreationResult CreateSinglePartDrawing(int modelObjectId, string drawingProperties, bool openDrawing)
    {
        var identifier = GetExistingModelObjectIdentifier(modelObjectId);
        CloseActiveDrawingIfNeeded();

        var drawing = string.IsNullOrWhiteSpace(drawingProperties)
            ? new SinglePartDrawing(identifier)
            : new SinglePartDrawing(identifier, drawingProperties);

        if (!drawing.Insert())
            throw new InvalidOperationException("Failed to create single part drawing.");

        return FinalizeCreation(drawing, "SinglePart", modelObjectId, drawingProperties, openDrawing);
    }

    public DrawingCreationResult CreateAssemblyDrawing(int modelObjectId, string drawingProperties, bool openDrawing)
    {
        var identifier = GetExistingModelObjectIdentifier(modelObjectId);
        CloseActiveDrawingIfNeeded();

        var drawing = string.IsNullOrWhiteSpace(drawingProperties)
            ? new AssemblyDrawing(identifier)
            : new AssemblyDrawing(identifier, drawingProperties);

        if (!drawing.Insert())
            throw new InvalidOperationException("Failed to create assembly drawing.");

        return FinalizeCreation(drawing, "Assembly", modelObjectId, drawingProperties, openDrawing);
    }

    private Identifier GetExistingModelObjectIdentifier(int modelObjectId)
    {
        var identifier = new Identifier(modelObjectId);
        var modelObject = _model.SelectModelObject(identifier);
        if (modelObject == null)
            throw new InvalidOperationException($"Model object with ID {modelObjectId} was not found.");

        return identifier;
    }

    private static void CloseActiveDrawingIfNeeded()
    {
        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing != null)
            drawingHandler.CloseActiveDrawing();
    }

    private static DrawingCreationResult FinalizeCreation(
        Tekla.Structures.Drawing.Drawing drawing,
        string drawingType,
        int modelObjectId,
        string drawingProperties,
        bool openDrawing)
    {
        var drawingHandler = new DrawingHandler();
        var opened = false;
        if (openDrawing)
            opened = drawingHandler.SetActiveDrawing(drawing);

        return new DrawingCreationResult
        {
            Created = true,
            Opened = opened,
            DrawingId = drawing.GetIdentifier().ID,
            DrawingType = drawingType,
            ModelObjectId = modelObjectId,
            DrawingProperties = string.IsNullOrWhiteSpace(drawingProperties) ? "standard" : drawingProperties
        };
    }
}
