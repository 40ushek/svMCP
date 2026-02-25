using System;
using System.Collections;
using System.Text.Json;
using Tekla.Structures.Model;

namespace TeklaBridge;

// Must be STA for Tekla API (COM/IPC compatibility)
internal class Program
{
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
        // When stdout is a pipe (redirected by MCP server), it becomes "Tekla.Structures.Model-:version"
        // instead of "Tekla.Structures.Model-Console:version" which Tekla server creates.
        // Override via reflection before new Model() triggers IPC connection.
        try {
            var assembly = typeof(Tekla.Structures.Model.Model).Assembly;
            var remoterType = assembly.GetType("Tekla.Structures.ModelInternal.Remoter");
            var f = remoterType?.GetField("ChannelName",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (f != null) {
                var version = assembly.GetName().Version?.ToString() ?? "2021.0.0.0";
                var correctName = $"Tekla.Structures.Model-Console:{version}";
                f.SetValue(null, correctName);
                System.IO.File.WriteAllText(@"C:\temp\tekla_channel.txt",
                    $"ChannelName fixed to: {correctName}\n");
            }
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
                        if (objs.Current is Part part)
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
                        if (allObjs.Current is Part p && p.Class == className)
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
                        if (sel.Current is Part pt)
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
