using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;

namespace TeklaBridge;

// Must be STA for Tekla API (COM/IPC compatibility)
internal partial class Program
{
    private sealed class DrawingPropertyFilter
    {
        public string Property { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private static bool ContainsIgnoreCase(string? source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return !string.IsNullOrWhiteSpace(source)
            && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }

    private static List<int> ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new List<int>();

        return csv
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => int.TryParse(x, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();
    }

    private static List<DrawingPropertyFilter> ParseDrawingFilters(string filtersJson)
    {
        var result = new List<DrawingPropertyFilter>();
        if (string.IsNullOrWhiteSpace(filtersJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(filtersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var property = item.TryGetProperty("property", out var p)
                    ? (p.GetString() ?? string.Empty)
                    : string.Empty;
                var value = item.TryGetProperty("value", out var v)
                    ? (v.GetString() ?? string.Empty)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(property))
                    result.Add(new DrawingPropertyFilter { Property = property, Value = value });
            }
        }
        catch
        {
            // ignored, caller validates result
        }

        return result;
    }

    private static Type? ResolveDrawingType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return null;

        return Type.GetType($"Tekla.Structures.Drawing.{objectType}, Tekla.Structures.Drawing", false, true);
    }

    private static string GetMarkType(Mark mark, Model model)
    {
        var associatedObjects = mark.GetRelatedObjects();
        foreach (object associated in associatedObjects)
        {
            if (associated is not Tekla.Structures.Drawing.ModelObject drawingModelObject)
                continue;

            var modelObject = model.SelectModelObject(drawingModelObject.ModelIdentifier);
            if (modelObject == null)
                continue;

            if (modelObject is Tekla.Structures.Model.Part) return "Part Mark";
            if (modelObject is BoltGroup) return "Bolt Mark";
            if (modelObject is RebarGroup || modelObject is SingleRebar) return "Reinforcement Mark";
            if (modelObject is Tekla.Structures.Model.Weld) return "Weld Mark";
            if (modelObject is Assembly) return "Assembly Mark";
            if (modelObject is Tekla.Structures.Model.Connection) return "Connection Mark";
        }

        return "Unknown Mark Type";
    }

    private static PropertyElement? CreateMarkPropertyElement(string attributeName)
    {
        switch ((attributeName ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "PART_POS":
            case "PARTPOSITION":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition());
            case "PROFILE":
            case "PART_PROFILE":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile());
            case "MATERIAL":
            case "PART_MATERIAL":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material());
            case "ASSEMBLY_POS":
            case "PART_PREFIX":
            case "ASSEMBLYPOSITION":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition());
            case "NAME":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name());
            case "CLASS":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class());
            case "SIZE":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size());
            case "CAMBER":
                return new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber());
            default:
                return null;
        }
    }

    private static bool CreateGaDrawingViaMacro(string viewName, string gaAttribute, bool openGaDrawing, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(viewName))
        {
            error = "View name is required.";
            return false;
        }

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
            if (Directory.Exists(subDir))
            {
                modelingDir = subDir;
                break;
            }

            if (Directory.Exists(cleanPath))
            {
                modelingDir = cleanPath;
                break;
            }
        }

        if (modelingDir == null)
        {
            error = "Valid modeling macro directory not found.";
            return false;
        }

        var macroName = $"_tmp_ga_{Guid.NewGuid():N}.cs";
        var macroPath = Path.Combine(modelingDir, macroName);
        var safeViewName = viewName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var attrLine = string.IsNullOrWhiteSpace(gaAttribute)
            ? string.Empty
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
                if (DateTime.Now > timeout)
                {
                    error = "Macro timeout exceeded.";
                    return false;
                }

                System.Threading.Thread.Sleep(100);
            }

            return true;
        }
        finally
        {
            if (File.Exists(macroPath))
                File.Delete(macroPath);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Set Tekla 2021 environment
        var teklaRoot = @"C:\TeklaStructures\2021.0";
        var teklaPath = teklaRoot + @"\nt\bin";

        if (System.IO.Directory.Exists(teklaRoot))
        {
            Environment.SetEnvironmentVariable("XS_SYSTEM", teklaRoot);
            Environment.SetEnvironmentVariable("XS_DIR", teklaRoot + @"\nt");
        }

        if (System.IO.Directory.Exists(teklaPath))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (currentPath.IndexOf(teklaPath, StringComparison.OrdinalIgnoreCase) < 0)
                Environment.SetEnvironmentVariable("PATH", teklaPath + ";" + currentPath);
        }

        // FIX: Tekla computes ChannelName based on stdout type.
        // When stdout is a pipe (redirected by MCP server), it omits "Console" from channel names,
        // causing IPC connection failures. Fix ALL Tekla Remoter channel names via reflection.
        try {
            // Touch assemblies to ensure they're loaded before we scan
            _ = typeof(Tekla.Structures.Model.Model);
            _ = typeof(Tekla.Structures.Drawing.DrawingHandler);

            // Internal assemblies (DrawingInternal, TeklaStructuresInternal, etc.) are NOT
            // auto-loaded — force-load all Tekla.Structures.*Internal*.dll from the same directory
            try {
                var dir = Path.GetDirectoryName(typeof(Tekla.Structures.Drawing.DrawingHandler).Assembly.Location) ?? "";
                foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll")) {
                    try { System.Reflection.Assembly.LoadFrom(dll); } catch { }
                }
            } catch { }

            var log = new System.Text.StringBuilder();
            int fixedCount = 0;
            var bindingFlags = System.Reflection.BindingFlags.Static |
                               System.Reflection.BindingFlags.Public |
                               System.Reflection.BindingFlags.NonPublic;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types) {
                    // Search ALL static string fields for broken channel name pattern,
                    // regardless of class/field name — Drawing API may use different names
                    foreach (var f in t.GetFields(bindingFlags)) {
                        if (f.FieldType != typeof(string)) continue;
                        try {
                            var val = f.GetValue(null)?.ToString() ?? "";
                            if (val.StartsWith("Tekla.Structures.") && val.Contains("-:")) {
                                var corrected = val.Replace("-:", "-Console:");
                                f.SetValue(null, corrected);
                                log.AppendLine($"FIXED {t.FullName}.{f.Name}: {val} -> {corrected}");
                                fixedCount++;
                            }
                        } catch { }
                    }
                }
            }
            System.IO.File.WriteAllText(@"C:\temp\tekla_channel.txt",
                $"Fixed {fixedCount} channel(s):\n{log}");
        } catch (Exception ex) {
            System.IO.File.WriteAllText(@"C:\temp\tekla_channel.txt", "ChannelName fix error: " + ex.Message);
        }

        if (args.Length == 0)
        {
            Console.WriteLine("{\"error\":\"No command specified\"}");
            return;
        }

        var command = args[0];

        // Capture Tekla's internal diagnostics (it writes to Console.Out during API calls)
        // so they don't pollute our JSON output. We'll include them in error responses.
        var realOut = Console.Out;
        var teklaLog = new System.IO.StringWriter();
        Console.SetOut(teklaLog);

        try
        {
            var model = new Model();

            if (command == "check_connection")
            {
                if (model.GetConnectionStatus())
                {
                    var info = model.GetInfo();
                    realOut.WriteLine(JsonSerializer.Serialize(new
                    {
                        status = "connected",
                        modelName = info.ModelName,
                        modelPath = info.ModelPath
                    }));
                }
                else
                {
                    var diag = teklaLog.ToString().Trim();
                    var result = JsonSerializer.Serialize(new
                    {
                        error = "Not connected to Tekla Structures",
                        xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                        xs_dir = Environment.GetEnvironmentVariable("XS_DIR"),
                        teklaLog = diag
                    });
                    realOut.WriteLine(result);
                    System.IO.File.WriteAllText(@"C:\temp\teklabridge_log.txt", result);
                }
                return;
            }

            if (!model.GetConnectionStatus())
            {
                var diag = teklaLog.ToString().Trim();
                realOut.WriteLine(JsonSerializer.Serialize(new
                {
                    error = "Not connected to Tekla Structures",
                    xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                    teklaLog = diag
                }));
                return;
            }

            var handled = TryHandleModelCommand(command, args, model, realOut)
                || TryHandleDrawingCommand(command, args, model, realOut);

            if (!handled)
                realOut.WriteLine($"{{\"error\":\"Unknown command: {command}\"}}");
        }
        catch (Exception ex)
        {
            var diag = teklaLog.ToString().Trim();
            var result = JsonSerializer.Serialize(new
            {
                error = ex.Message,
                type = ex.GetType().Name,
                xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                teklaLog = diag
            });
            realOut.WriteLine(result);
            System.IO.File.WriteAllText(@"C:\temp\teklabridge_log.txt", result);
        }
    }
}
