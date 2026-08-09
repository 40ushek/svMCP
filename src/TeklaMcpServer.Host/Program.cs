using System;
using System.IO;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Host;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var model = new Model();
        ApplyTeklaChannelFixes();

        if (!model.GetConnectionStatus())
        {
            Console.WriteLine("Not connected to Tekla Structures. Open a model and try again.");
            Console.ReadLine();
            return;
        }

        var info = model.GetInfo();
        Console.WriteLine($"Connected: {info.ModelName}  ({info.ModelPath})");

        SolidContacts.ContactProbe.Run();
        Console.ReadLine();
        return;

        var docManResult = new TeklaMcpServer.Api.Drawing.TeklaDrawingQueryApi().GetSelectedDrawingsInDocumentManager();
        if (!docManResult.Success)
        {
            Console.WriteLine($"Error: {docManResult.Error}");
        }
        else
        {
            Console.WriteLine($"Selected in Document Manager: {docManResult.Drawings.Count}");
            foreach (var d in docManResult.Drawings)
                Console.WriteLine($"  - {d.Type} {d.Mark} {d.Name} [{d.Guid}]");
        }
    }

    private static bool ShouldRunRestrictionBoxProbe(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--restriction-box-probe", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void RunRestrictionBoxProbe()
    {
        try
        {
            new DrawingViewRestrictionBoxProbe().Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Restriction box probe failed: {ex.Message}");
        }
    }

    private static void ApplyTeklaChannelFixes()
    {
        var apiVersion = typeof(Model).Assembly.GetName().Version;
        if (apiVersion == null || apiVersion.Major < 2025)
            ApplyIpcChannelFix();
        // TS2025: running from extensions folder loads correct DLLs — no fix needed
    }

    private static void ApplyIpcChannelFix()
    {
        try
        {
            _ = typeof(Model);
            _ = typeof(DrawingHandler);

            var dir = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location) ?? string.Empty;
            foreach (var dll in Directory.GetFiles(dir, "Tekla.Structures.*Internal*.dll"))
                try { System.Reflection.Assembly.LoadFrom(dll); } catch { }

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
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
                            var val = f.GetValue(null)?.ToString() ?? string.Empty;
                            if (val.StartsWith("Tekla.Structures.") && val.Contains("-:"))
                                f.SetValue(null, val.Replace("-:", "-Console:"));
                        }
                        catch { }
                    }
            }
        }
        catch { }
    }
}
