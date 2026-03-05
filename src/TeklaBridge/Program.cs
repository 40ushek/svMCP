using System;
using System.IO;
using System.Text.Json;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using TeklaBridge.Commands;

namespace TeklaBridge;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Set Tekla environment:
        // 1) use XS_SYSTEM if already valid,
        // 2) otherwise prefer installation matching referenced Tekla API major version,
        // 3) fallback to latest installed version.
        var teklaRoot = ResolveTeklaRoot();

        if (!string.IsNullOrEmpty(teklaRoot) && Directory.Exists(teklaRoot))
        {
            Environment.SetEnvironmentVariable("XS_SYSTEM", teklaRoot);
            Environment.SetEnvironmentVariable("XS_DIR", teklaRoot + @"\nt");
            var teklaPath = teklaRoot + @"\nt\bin";
            if (Directory.Exists(teklaPath))
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (currentPath.IndexOf(teklaPath, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", teklaPath + ";" + currentPath);
            }
        }

        // FIX: Tekla computes ChannelName based on stdout type.
        // When stdout is a pipe (redirected by MCP server), it omits "Console" from channel names,
        // causing IPC connection failures. Fix ALL Tekla channel names via reflection.
        ApplyIpcChannelFix();

        if (args.Length == 0)
        {
            Console.WriteLine("{\"error\":\"No command specified\"}");
            return;
        }

        var command = args[0];

        // Capture Tekla's internal diagnostics (it writes to Console.Out during API calls)
        // so they don't pollute our JSON output.
        var realOut = Console.Out;
        var teklaLog = new StringWriter();
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
                    var result = JsonSerializer.Serialize(new
                    {
                        error = "Not connected to Tekla Structures",
                        xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                        xs_dir = Environment.GetEnvironmentVariable("XS_DIR"),
                        teklaLog = teklaLog.ToString().Trim()
                    });
                    realOut.WriteLine(result);
                    File.WriteAllText(@"C:\temp\teklabridge_log.txt", result);
                }
                return;
            }

            if (!model.GetConnectionStatus())
            {
                realOut.WriteLine(JsonSerializer.Serialize(new
                {
                    error = "Not connected to Tekla Structures",
                    xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                    teklaLog = teklaLog.ToString().Trim()
                }));
                return;
            }

            var dispatcher = new CommandDispatcher(model, realOut);
            if (!dispatcher.Dispatch(command, args))
                realOut.WriteLine($"{{\"error\":\"Unknown command: {command}\"}}");
        }
        catch (Exception ex)
        {
            var result = JsonSerializer.Serialize(new
            {
                error = ex.Message,
                type = ex.GetType().Name,
                xs_system = Environment.GetEnvironmentVariable("XS_SYSTEM"),
                teklaLog = teklaLog.ToString().Trim()
            });
            realOut.WriteLine(result);
            File.WriteAllText(@"C:\temp\teklabridge_log.txt", result);
        }
    }

    private static string? DetectTeklaRoot()
    {
        var baseDir = @"C:\TeklaStructures";
        if (!Directory.Exists(baseDir)) return null;

        Version? bestVersion = null;
        string? bestPath = null;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!Version.TryParse(name, out var ver)) continue;
            if (!Directory.Exists(Path.Combine(dir, "nt", "bin"))) continue;
            if (bestVersion == null || ver > bestVersion) { bestVersion = ver; bestPath = dir; }
        }

        return bestPath;
    }

    private static string? ResolveTeklaRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("XS_SYSTEM");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var apiMajor = typeof(Model).Assembly.GetName().Version?.Major;
        var matching = DetectTeklaRootForMajor(apiMajor);
        if (!string.IsNullOrWhiteSpace(matching))
            return matching;

        return DetectTeklaRoot();
    }

    private static string? DetectTeklaRootForMajor(int? major)
    {
        if (!major.HasValue)
            return null;

        var baseDir = @"C:\TeklaStructures";
        if (!Directory.Exists(baseDir))
            return null;

        Version? bestVersion = null;
        string? bestPath = null;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!Version.TryParse(name, out var ver))
                continue;
            if (ver.Major != major.Value)
                continue;
            if (!Directory.Exists(Path.Combine(dir, "nt", "bin")))
                continue;

            if (bestVersion == null || ver > bestVersion)
            {
                bestVersion = ver;
                bestPath = dir;
            }
        }

        return bestPath;
    }

    private static void ApplyIpcChannelFix()
    {
        try
        {
            _ = typeof(Model);
            _ = typeof(DrawingHandler);

            try
            {
                var dir = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location) ?? "";
                foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll"))
                    try { System.Reflection.Assembly.LoadFrom(dll); } catch { }
            }
            catch { }

            var log = new System.Text.StringBuilder();
            int fixedCount = 0;
            var flags = System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                    foreach (var f in t.GetFields(flags))
                    {
                        if (f.FieldType != typeof(string)) continue;
                        try
                        {
                            var val = f.GetValue(null)?.ToString() ?? "";
                            if (val.StartsWith("Tekla.Structures.") && val.Contains("-:"))
                            {
                                var corrected = val.Replace("-:", "-Console:");
                                f.SetValue(null, corrected);
                                log.AppendLine($"FIXED {t.FullName}.{f.Name}: {val} -> {corrected}");
                                fixedCount++;
                            }
                        }
                        catch { }
                    }
            }

            File.WriteAllText(@"C:\temp\tekla_channel.txt", $"Fixed {fixedCount} channel(s):\n{log}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\temp\tekla_channel.txt", "ChannelName fix error: " + ex.Message);
        }
    }
}
