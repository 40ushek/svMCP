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

        if (ShouldRunViewRelatedObjectsProbe(args))
        {
            RunViewRelatedObjectsProbe();
            return;
        }

        if (ShouldRunDimensionPresentationProbe(args))
        {
            RunDimensionPresentationProbe();
            return;
        }

        if (ShouldRunViewStitchProbe(args))
        {
            RunViewStitchProbe(args);
            return;
        }

        if (ShouldRunViewFoldProbe(args))
        {
            RunViewFoldProbe(args);
            return;
        }

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

    private static bool ShouldRunViewRelatedObjectsProbe(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--view-related-objects-probe", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ShouldRunDimensionPresentationProbe(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--dimension-presentation-probe", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void RunDimensionPresentationProbe()
    {
        try
        {
            new DimensionPresentationProbe().Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dimension presentation probe failed: {ex}");
            Environment.ExitCode = 1;
        }
    }

    private static bool ShouldRunViewStitchProbe(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--view-stitch-probe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--view-stitch-probe-selected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--view-stitch-apply-selected", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ShouldRunViewFoldProbe(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--view-fold-probe-selected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--view-fold-apply-selected", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void RunViewRelatedObjectsProbe()
    {
        try
        {
            new DrawingViewRelatedObjectsProbe().Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"View related-objects probe failed: {ex.Message}");
        }
    }

    private static void RunViewStitchProbe(string[] args)
    {
        try
        {
            new ViewStitchTransformProbe().Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"View stitch probe failed: {ex.Message}");
        }
    }

    private static void RunViewFoldProbe(string[] args)
    {
        try
        {
            var applyRequested = args.Any(arg => string.Equals(arg, "--view-fold-apply-selected", StringComparison.OrdinalIgnoreCase));
            new ViewFoldTransformProbe().Run(applyRequested);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"View fold probe failed: {ex.Message}");
        }
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
