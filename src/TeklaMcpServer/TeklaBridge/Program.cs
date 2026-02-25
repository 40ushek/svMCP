using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaBridge;

// Must be STA for Tekla API (COM/IPC compatibility)
internal class Program
{
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

            var log = new System.Text.StringBuilder();
            int fixedCount = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types) {
                    if (!t.Name.Contains("Remoter")) continue;
                    var f = t.GetField("ChannelName",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (f == null) continue;
                    try {
                        var val = f.GetValue(null)?.ToString() ?? "";
                        if (val.Contains("-:")) {
                            var corrected = val.Replace("-:", "-Console:");
                            f.SetValue(null, corrected);
                            log.AppendLine($"FIXED {t.FullName}: {val} -> {corrected}");
                            fixedCount++;
                        } else {
                            log.AppendLine($"OK    {t.FullName}: {val}");
                        }
                    } catch (Exception ex2) {
                        log.AppendLine($"ERR   {t.FullName}: {ex2.Message}");
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

            switch (command)
            {
                case "get_selected_properties":
                {
                    var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
                    var objs = selector.GetSelectedObjects();
                    var results = new System.Collections.Generic.List<object>();
                    while (objs.MoveNext())
                    {
                        if (objs.Current is Tekla.Structures.Model.Part part)
                        {
                            double weight = 0;
                            part.GetReportProperty("WEIGHT", ref weight);
                            results.Add(new
                            {
                                guid = part.Identifier.GUID.ToString(),
                                name = part.Name,
                                profile = part.Profile.ProfileString,
                                material = part.Material.MaterialString,
                                @class = part.Class,
                                finish = part.Finish,
                                weight = Math.Round(weight, 3)
                            });
                        }
                    }
                    realOut.WriteLine(JsonSerializer.Serialize(results));
                    break;
                }

                case "select_by_class":
                {
                    if (args.Length < 2) { realOut.WriteLine("{\"error\":\"Missing class number\"}"); return; }
                    var className = args[1];
                    var allObjs = model.GetModelObjectSelector().GetAllObjects();
                    var toSelect = new ArrayList();
                    while (allObjs.MoveNext())
                    {
                        if (allObjs.Current is Tekla.Structures.Model.Part p && p.Class == className)
                            toSelect.Add(p);
                    }
                    new Tekla.Structures.Model.UI.ModelObjectSelector().Select(toSelect);
                    realOut.WriteLine(JsonSerializer.Serialize(new { count = toSelect.Count, @class = className }));
                    break;
                }

                case "get_selected_weight":
                {
                    var sel = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
                    double totalWeight = 0;
                    int count = 0;
                    while (sel.MoveNext())
                    {
                        if (sel.Current is Tekla.Structures.Model.Part pt)
                        {
                            double w = 0;
                            pt.GetReportProperty("WEIGHT", ref w);
                            totalWeight += w;
                            count++;
                        }
                    }
                    realOut.WriteLine(JsonSerializer.Serialize(new { totalWeight = Math.Round(totalWeight, 2), count }));
                    break;
                }

                case "list_drawings":
                {
                    var drawingHandler = new DrawingHandler();
                    var drawingEnumerator = drawingHandler.GetDrawings();
                    var drawings = new List<object>();

                    while (drawingEnumerator.MoveNext())
                    {
                        var drawing = drawingEnumerator.Current;
                        drawings.Add(new
                        {
                            guid = drawing.GetIdentifier().GUID.ToString(),
                            name = drawing.Name,
                            mark = drawing.Mark,
                            type = drawing.GetType().Name
                        });
                    }

                    realOut.WriteLine(JsonSerializer.Serialize(drawings));
                    break;
                }

                case "find_drawings":
                {
                    var nameContains = args.Length > 1 ? args[1] : string.Empty;
                    var markContains = args.Length > 2 ? args[2] : string.Empty;

                    if (string.IsNullOrWhiteSpace(nameContains) && string.IsNullOrWhiteSpace(markContains))
                    {
                        realOut.WriteLine("{\"error\":\"Provide at least one filter: nameContains or markContains\"}");
                        return;
                    }

                    var drawingHandler = new DrawingHandler();
                    var drawingEnumerator = drawingHandler.GetDrawings();
                    var drawings = new List<object>();

                    while (drawingEnumerator.MoveNext())
                    {
                        var drawing = drawingEnumerator.Current;
                        if (!ContainsIgnoreCase(drawing.Name, nameContains))
                            continue;

                        if (!ContainsIgnoreCase(drawing.Mark, markContains))
                            continue;

                        drawings.Add(new
                        {
                            guid = drawing.GetIdentifier().GUID.ToString(),
                            name = drawing.Name,
                            mark = drawing.Mark,
                            type = drawing.GetType().Name
                        });
                    }

                    realOut.WriteLine(JsonSerializer.Serialize(drawings));
                    break;
                }

                case "export_drawings_pdf":
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                    {
                        realOut.WriteLine("{\"error\":\"Missing drawing GUID list (comma-separated)\"}");
                        return;
                    }

                    var requestedGuids = args[1]
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (requestedGuids.Count == 0)
                    {
                        realOut.WriteLine("{\"error\":\"No valid drawing GUIDs provided\"}");
                        return;
                    }

                    var modelInfo = model.GetInfo();
                    var outputDir = (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
                        ? args[2]
                        : Path.Combine(modelInfo.ModelPath, "PlotFiles");

                    Directory.CreateDirectory(outputDir);

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
                        var drawing = drawingEnumerator.Current;
                        var guid = drawing.GetIdentifier().GUID.ToString();
                        if (!requestedGuids.Contains(guid))
                            continue;

                        foundGuids.Add(guid);

                        var fileName = $"{SanitizeFileName(drawing.Name)}_{SanitizeFileName(drawing.Mark)}.pdf";
                        var filePath = Path.Combine(outputDir, fileName);

                        if (drawingHandler.PrintDrawing(drawing, printAttributes, filePath))
                            exportedFiles.Add(filePath);
                        else
                            failedToExport.Add(guid);
                    }

                    var missingGuids = requestedGuids
                        .Where(g => !foundGuids.Contains(g))
                        .ToList();

                    realOut.WriteLine(JsonSerializer.Serialize(new
                    {
                        exportedCount = exportedFiles.Count,
                        exportedFiles,
                        failedToExport,
                        missingGuids,
                        outputDirectory = outputDir
                    }));
                    break;
                }

                default:
                    realOut.WriteLine($"{{\"error\":\"Unknown command: {command}\"}}");
                    break;
            }
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
