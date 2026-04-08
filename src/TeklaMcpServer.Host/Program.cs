using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Host;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ViewTest.CheckView();

        //var dr = new TeklaMcpServer.Api.Drawing.TeklaDrawingPartGeometryApi(new Model());
        //var g = dr.GetAllPartsGeometryInView(3700);

        ApplyTeklaChannelFixes();

        var model = new Model();
        //var part = model.SelectModelObject(new Tekla.Structures.Identifier("fd029a2f-2668-4fb1-beb7-745d94860d72"));
        //int d = 0;
        //var s = string.Empty;
        //part.GetReportProperty("HISTORY.TOUCHED", ref s);
        //part.GetReportProperty("HISTORY.MODIFIED", ref s);


        if (!model.GetConnectionStatus())
        {
            Console.WriteLine("Not connected to Tekla Structures. Open a model and try again.");
            Console.ReadLine();
            return;
        }

        var info = model.GetInfo();
        Console.WriteLine($"Connected: {info.ModelName}  ({info.ModelPath})");

        var drawingHandler = new DrawingHandler();
        var activeDrawing = drawingHandler.GetActiveDrawing();
        if (activeDrawing == null)
        {
            Console.WriteLine("No active drawing. Open a drawing in Tekla and try again.");
            Console.ReadLine();
            return;
        }

        Console.WriteLine($"Drawing: {activeDrawing.Name}");

        var mark = MarkSelector.GetSelected(drawingHandler);
        if (mark == null)
        {
            Console.WriteLine("No mark selected. Select a mark in the drawing and try again.");
            Console.ReadLine();
            return;
        }

        Console.WriteLine($"Mark type: {mark.GetType().Name}  InsertionPoint: {mark.InsertionPoint}");
        MarkBoxDrawer.DrawBoundingBox(mark, activeDrawing);

        Console.ReadLine();
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
