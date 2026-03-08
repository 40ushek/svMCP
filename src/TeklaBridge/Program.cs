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
            // TS2021: nt/ layout; TS2025+: bin/ directly under root
            var ntDir = Path.Combine(teklaRoot, "nt");
            Environment.SetEnvironmentVariable("XS_DIR", Directory.Exists(ntDir) ? ntDir : teklaRoot);
            var teklaPath = Directory.Exists(Path.Combine(teklaRoot, "nt", "bin"))
                ? Path.Combine(teklaRoot, "nt", "bin")
                : Path.Combine(teklaRoot, "bin");
            if (Directory.Exists(teklaPath))
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (currentPath.IndexOf(teklaPath, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", teklaPath + ";" + currentPath);
            }
        }

        // FIX: Tekla 2021 — stdout-pipe makes API omit "Console" from channel name. Patch via reflection.
        // FIX: Tekla 2025+ — NuGet package has channel name "...Console:2025.0.0.0" but the
        //   actual installed Tekla uses a build-specific version like "...Console:2025.0.52577.0".
        //   Read the real version from the installed DLL and patch our NuGet DLL's static fields.
        var apiVersion = typeof(Model).Assembly.GetName().Version;
        if (apiVersion == null || apiVersion.Major < 2025)
            ApplyIpcChannelFix();
        else
            ApplyTs2025ChannelVersionFix(teklaRoot);

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
            if (!Directory.Exists(Path.Combine(dir, "nt", "bin")) &&
                !Directory.Exists(Path.Combine(dir, "bin"))) continue;
            if (bestVersion == null || ver > bestVersion) { bestVersion = ver; bestPath = dir; }
        }

        return bestPath;
    }

    private static string? ResolveTeklaRoot()
    {
        // Always prefer the installation whose version matches the compiled API version.
        var apiMajor = typeof(Model).Assembly.GetName().Version?.Major;
        var matching = DetectTeklaRootForMajor(apiMajor);
        if (!string.IsNullOrWhiteSpace(matching))
            return matching;

        // Fall back to XS_SYSTEM env var (may point to a different version — accepted as last resort).
        var fromEnv = Environment.GetEnvironmentVariable("XS_SYSTEM");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

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
            if (!Directory.Exists(Path.Combine(dir, "nt", "bin")) &&
                !Directory.Exists(Path.Combine(dir, "bin")))
                continue;

            if (bestVersion == null || ver > bestVersion)
            {
                bestVersion = ver;
                bestPath = dir;
            }
        }

        return bestPath;
    }

    /// <summary>
    /// TS2025+: NuGet DLLs have channel names like "Tekla.Structures.Model-TeklaStructures-Console:2025.0.0.0"
    /// but the actual installed Tekla builds them as "...2025.0.52577.0" (build-specific patch).
    /// Scan the installed DLL for the real version string and patch our loaded assemblies.
    /// </summary>
    private static void ApplyTs2025ChannelVersionFix(string? teklaRoot)
    {
        try
        {
            if (string.IsNullOrEmpty(teklaRoot)) return;
            var binDir = Directory.Exists(Path.Combine(teklaRoot, "nt", "bin"))
                ? Path.Combine(teklaRoot, "nt", "bin")
                : Path.Combine(teklaRoot, "bin");

            // Find the real channel name from the installed Model DLL binary
            var installedVersion = ReadChannelVersionFromDll(
                Path.Combine(binDir, "Tekla.Structures.Model.dll"),
                "Tekla.Structures.Model-TeklaStructures-Console:");

            if (installedVersion == null)
            {
                File.WriteAllText(@"C:\temp\tekla_channel.txt", "TS2025 fix: could not find channel version in installed DLL");
                return;
            }

            // Patch all Tekla static string fields: replace "...Console:2025.X.0.0" → "...Console:installedVersion"
            var log = new System.Text.StringBuilder();
            int fixedCount = 0;
            var flags = System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic;

            _ = typeof(Model);
            _ = typeof(DrawingHandler);

            // Force static constructors on all Tekla types so ChannelName fields are initialized
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Tekla.Structures")) continue;
                Type[] initTypes;
                try { initTypes = asm.GetTypes(); } catch { continue; }
                foreach (var t in initTypes)
                    try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); } catch { }
            }

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
                            if (!val.StartsWith("Tekla.Structures.") || !val.Contains("-Console:")) continue;
                            // Replace the version suffix
                            var colonIdx = val.LastIndexOf(':');
                            if (colonIdx < 0) continue;
                            var currentVer = val.Substring(colonIdx + 1);
                            if (currentVer == installedVersion) continue; // already correct
                            var corrected = val.Substring(0, colonIdx + 1) + installedVersion;
                            f.SetValue(null, corrected);
                            log.AppendLine($"FIXED {t.FullName}.{f.Name}: {val} -> {corrected}");
                            fixedCount++;
                        }
                        catch { }
                    }
            }

            File.WriteAllText(@"C:\temp\tekla_channel.txt",
                $"TS2025 fix: installedVersion={installedVersion}, fixed {fixedCount} channel(s):\n{log}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\temp\tekla_channel.txt", "TS2025 channel fix error: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads the channel version suffix from a Tekla DLL's FileVersion resource.
    /// The channel name embeds the FileVersion (e.g. "2025.0.52577.0"), not the AssemblyVersion.
    /// Uses FileVersionInfo which reads the PE VERSIONINFO resource without loading the assembly.
    /// </summary>
    private static string? ReadChannelVersionFromDll(string dllPath, string _channelPrefix)
    {
        if (!File.Exists(dllPath)) return null;
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath);
            // FileVersion is "2025.0.52577.0" — matches what Tekla uses in channel names
            var ver = fvi.FileVersion?.Trim();
            if (!string.IsNullOrEmpty(ver) && ver.Contains('.'))
                return ver;
        }
        catch { }
        return null;
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
