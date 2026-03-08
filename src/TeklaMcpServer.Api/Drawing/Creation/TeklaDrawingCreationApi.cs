using System;
using System.IO;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed class TeklaDrawingCreationApi : IDrawingCreationApi
{
    private readonly Model _model;

    public TeklaDrawingCreationApi(Model model) => _model = model;

    public GaDrawingCreationResult CreateGaDrawing(string viewName, string drawingProperties, bool openDrawing)
    {
        var result = new GaDrawingCreationResult
        {
            ViewName = viewName ?? string.Empty,
            DrawingProperties = string.IsNullOrWhiteSpace(drawingProperties) ? "standard" : drawingProperties,
            OpenDrawing = openDrawing
        };

        if (!CreateGaDrawingViaMacro(result.ViewName, drawingProperties, openDrawing, out var error))
        {
            result.Created = false;
            result.ErrorDetails = error;
            return result;
        }

        result.Created = true;
        return result;
    }

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

    private static bool CreateGaDrawingViaMacro(string viewName, string gaAttribute, bool openGaDrawing, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(viewName)) { error = "View name is required."; return false; }

        string macroDirs = string.Empty;
        if (!TeklaStructuresSettings.GetAdvancedOption("XS_MACRO_DIRECTORY", ref macroDirs))
        {
            error = "XS_MACRO_DIRECTORY is not defined.";
            return false;
        }

        string? modelingDir = null;
        foreach (var path in macroDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cleanPath = path.Trim();
            var subDir = Path.Combine(cleanPath, "modeling");
            if (Directory.Exists(subDir)) { modelingDir = subDir; break; }
            if (Directory.Exists(cleanPath)) { modelingDir = cleanPath; break; }
        }

        if (modelingDir == null) { error = "Valid modeling macro directory not found."; return false; }

        var macroName = $"_tmp_ga_{Guid.NewGuid():N}.cs";
        var macroPath = Path.Combine(modelingDir, macroName);
        var safeViewName = viewName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var attrLine = string.IsNullOrWhiteSpace(gaAttribute) ? string.Empty
            : $"            akit.ValueChange(\"Create GA-drawing\", \"dia_attr_name\", \"{gaAttribute}\");{Environment.NewLine}";
        var openFlag = openGaDrawing ? "1" : "0";

        var macroSource =
$@"
            namespace Tekla.Technology.Akit.UserScript
            {{
                public sealed class Script
                {{
                    public static void Run(Tekla.Technology.Akit.IScript akit)
                    {{
                        akit.Callback(""acmd_create_dim_general_assembly_drawing"", """", ""main_frame"");
{attrLine}            akit.ListSelect(""Create GA-drawing"", ""dia_view_name_list"", ""{safeViewName}"");
                        akit.ValueChange(""Create GA-drawing"", ""dia_creation_mode"", ""0"");
                        akit.ValueChange(""Create GA-drawing"", ""dia_open_drawing"", ""{openFlag}"");
                        akit.PushButton(""Pushbutton_127"", ""Create GA-drawing"");
                    }}
                }}
            }}";

        File.WriteAllText(macroPath, macroSource);
        try
        {
            if (!Tekla.Structures.Model.Operations.Operation.RunMacro(macroName))
            {
                error = "RunMacro returned false.";
                return false;
            }

            var timeout = DateTime.Now.AddSeconds(30);
            while (Tekla.Structures.Model.Operations.Operation.IsMacroRunning())
            {
                if (DateTime.Now > timeout) { error = "Macro timeout exceeded."; return false; }
                System.Threading.Thread.Sleep(100);
            }

            return true;
        }
        finally
        {
            if (File.Exists(macroPath)) File.Delete(macroPath);
        }
    }
}
