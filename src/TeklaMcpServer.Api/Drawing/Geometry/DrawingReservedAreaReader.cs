using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Common.Geometry;
using TeklaMcpServer.Api.Diagnostics;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.DrawingPresentationModel;
using Tekla.Structures.DrawingPresentationModelInterface;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

internal static class DrawingReservedAreaReader
{
    private const double MinObstacleSize = 1.0;
    private const double FullSheetCoverageRatio = 0.95;

    public static IReadOnlyList<ReservedRect> Read(
        Tekla.Structures.Drawing.Drawing drawing,
        double margin,
        double titleBlockHeight,
        IReadOnlyCollection<int>? excludeViewIds = null,
        IReadOnlyList<LayoutTableGeometryInfo>? preloadedTables = null)
    {
        var reserved = new List<ReservedRect>();
        var size = drawing.Layout.SheetSize;
        var usableMinX = margin;
        var usableMinY = margin;
        var usableMaxX = size.Width - margin;
        var usableMaxY = size.Height - margin;

        if (usableMaxX <= usableMinX || usableMaxY <= usableMinY)
            return reserved;

        if (titleBlockHeight > 0)
        {
            var manualTop = Clamp(usableMinY + titleBlockHeight, usableMinY, usableMaxY);
            if (manualTop - usableMinY >= MinObstacleSize)
                reserved.Add(new ReservedRect(usableMinX, usableMinY, usableMaxX, manualTop));
        }

        AddLayoutTableReservedAreas(reserved, usableMinX, usableMinY, usableMaxX, usableMaxY,
            preloadedTables ?? ReadLayoutTableGeometries());

        var sheet = drawing.GetSheet();
        var sheetId = sheet.GetIdentifier().ID;
        var objects = sheet.GetAllObjects();
        while (objects.MoveNext())
        {
            if (objects.Current is not DrawingObject drawingObject)
                continue;

            var owner = drawingObject.GetView();
            if (owner == null || owner.GetIdentifier().ID != sheetId)
                continue;

            if (drawingObject is ViewBase)
            {
                if (drawingObject is not Tekla.Structures.Drawing.View contentView)
                    continue;
                if (excludeViewIds != null && excludeViewIds.Contains(contentView.GetIdentifier().ID))
                    continue;
            }

            if (drawingObject is not IAxisAlignedBoundingBox bounded)
                continue;

            var box = bounded.GetAxisAlignedBoundingBox();
            if (box == null)
                continue;

            var minX = Clamp(box.MinPoint.X, usableMinX, usableMaxX);
            var minY = Clamp(box.MinPoint.Y, usableMinY, usableMaxY);
            var maxX = Clamp(box.MaxPoint.X, usableMinX, usableMaxX);
            var maxY = Clamp(box.MaxPoint.Y, usableMinY, usableMaxY);

            if (maxX - minX < MinObstacleSize || maxY - minY < MinObstacleSize)
                continue;

            var widthRatio = (maxX - minX) / (usableMaxX - usableMinX);
            var heightRatio = (maxY - minY) / (usableMaxY - usableMinY);
            if (widthRatio >= FullSheetCoverageRatio && heightRatio >= FullSheetCoverageRatio)
                continue;

            reserved.Add(new ReservedRect(minX, minY, maxX, maxY));
        }

        return MergeOverlaps(reserved);
    }

    /// <summary>
    /// Reads sheet margin and layout table geometries in a single LayoutManager.OpenEditor() call.
    /// Use this instead of calling TryReadSheetMargin() and ReadLayoutTableGeometries() separately.
    /// </summary>
    /// <summary>
    /// Reads sheet margin and layout table geometries.
    ///
    /// TWO-PHASE ARCHITECTURE — do not merge into one LayoutManager.OpenEditor() block:
    ///
    /// Phase 1 (metadata): LayoutManager.OpenEditor() → GetMarginsAndSpaces() + GetCurrentTables()
    ///                      → LayoutManager.CloseEditor()   ← MUST close before Phase 2
    ///
    /// Phase 2 (geometry):  new PresentationConnection()    ← opens Layout Editor internally
    ///                      → GetObjectPresentation() per table
    ///                      → LayoutManager.CloseEditor() in finally
    ///
    /// Why two phases? PresentationConnection opens the Layout Editor itself as a side effect.
    /// If the editor is already open (from Phase 1), PresentationConnection throws
    /// "Unable to connect to TeklaStructures process". Closing the editor between phases fixes this.
    ///
    /// DEPLOYMENT NOTE: PresentationConnection (DrawingPresentationModelInterface.dll) requires
    /// Tekla.Structures.GrpcContracts.dll at load time. This DLL is NOT pulled in by Tekla NuGet
    /// packages and must be copied manually from C:\TeklaStructures\{ver}\bin\ to the bridge folder.
    /// Missing DLL symptom: FileNotFoundException on first PresentationConnection constructor call.
    /// </summary>
    internal static (double? SheetMargin, IReadOnlyList<LayoutTableGeometryInfo> Tables) ReadLayoutInfo()
    {
        // Phase 1: read margins and table IDs in one editor-open pass.
        // Must be closed BEFORE creating PresentationConnection — PresentationConnection
        // opens the Layout Editor itself as a side effect, and fails if it is already open.
        double? sheetMargin = null;
        List<int>? tableIds = null;
        try
        {
            LayoutManager.OpenEditor();
            try
            {
                try
                {
                    double top = 0, bottom = 0, left = 0, right = 0;
                    TableLayout.GetMarginsAndSpaces(out top, out bottom, out left, out right);
                    // Use min: GetMarginsAndSpaces returns both edge margins AND inter-table spacing.
                    // Spacing can be 100+ mm (e.g. parts-list column width), so Math.Max would give
                    // a wrongly large margin that would block most of the usable sheet area.
                    // Math.Min reliably picks the actual sheet-edge margin (typically 5–10 mm).
                    var m = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
                    sheetMargin = m > 0 ? m : (double?)null;
                }
                catch { }

                try { tableIds = TableLayout.GetCurrentTables(); }
                catch (Exception ex)
                {
                    PerfTrace.Write("api-view", "reserved_tables_skip", 0, $"reason=get_current_tables_failed error={ex.GetType().Name}");
                    return (sheetMargin, Array.Empty<LayoutTableGeometryInfo>());
                }
            }
            finally
            {
                try { LayoutManager.CloseEditor(); } catch { }
            }
        }
        catch (Exception ex)
        {
            PerfTrace.Write("api-view", "reserved_tables_skip", 0, $"reason=open_editor_failed error={ex.GetType().Name}");
            return (null, Array.Empty<LayoutTableGeometryInfo>());
        }

        if (tableIds == null || tableIds.Count == 0)
            return (sheetMargin, Array.Empty<LayoutTableGeometryInfo>());

        // Phase 2: read table geometry via PresentationConnection.
        // PresentationConnection (DrawingPresentationModelInterface.Connection) opens the Layout
        // Editor internally as a side effect. The editor MUST be closed (Phase 1 finally) before
        // this call — otherwise throws "Unable to connect to TeklaStructures process".
        //
        // Runtime dependency: Tekla.Structures.GrpcContracts.dll must be present next to
        // TeklaBridge.exe. It is not included in Tekla NuGet packages.
        // Source: C:\TeklaStructures\{ver}\bin\Tekla.Structures.GrpcContracts.dll
        var result = new List<LayoutTableGeometryInfo>();
        try
        {
            using var connection = new PresentationConnection();
            foreach (var tableId in tableIds)
            {
                var lt = new LayoutTable { Id = tableId };
                var ltSelected = lt.Select();
                var overlapWithViews = ltSelected && lt.OverlapVithViews;
                var tableName = ltSelected ? (lt.Name ?? "") : "";
                var segment = connection.Service.GetObjectPresentation(tableId);
                var info = BuildLayoutTableGeometryInfo(tableId, tableName, segment, overlapWithViews);
                result.Add(info);
                PerfTrace.Write(
                    "api-view",
                    "reserved_table_geometry",
                    0,
                    info.HasGeometry && info.Bounds != null
                        ? $"tableId={info.TableId} name={info.Name} overlap={info.OverlapWithViews} hasGeometry=true minX={info.Bounds.MinX:F1} minY={info.Bounds.MinY:F1} maxX={info.Bounds.MaxX:F1} maxY={info.Bounds.MaxY:F1}"
                        : $"tableId={info.TableId} name={info.Name} overlap={info.OverlapWithViews} hasGeometry=false");
            }
        }
        catch (Exception ex)
        {
            PerfTrace.Write("api-view", "reserved_tables_skip", 0, $"reason=presentation_model_failed error={ex.GetType().Name}");
            return (sheetMargin, Array.Empty<LayoutTableGeometryInfo>());
        }
        finally
        {
            try { LayoutManager.CloseEditor(); } catch { }
        }

        PerfTrace.Write("api-view", "reserved_tables_summary", 0,
            $"count={result.Count} reserved={result.Count(x => x.HasGeometry && !x.OverlapWithViews)}");
        return (sheetMargin, result);
    }

    /// <summary>
    /// Reads the drawing sheet margins from TableLayout.GetMarginsAndSpaces().
    /// Returns null if the call fails (caller should use a default).
    /// Prefer ReadLayoutInfo() when table geometries are also needed, to avoid a second editor open.
    /// </summary>
    public static double? TryReadSheetMargin() => ReadLayoutInfo().SheetMargin;

    private static void AddLayoutTableReservedAreas(
        List<ReservedRect> reserved,
        double usableMinX,
        double usableMinY,
        double usableMaxX,
        double usableMaxY,
        IReadOnlyList<LayoutTableGeometryInfo> tableGeometries)
    {
        foreach (var table in tableGeometries)
        {
            if (table.OverlapWithViews)
                continue;

            if (!table.HasGeometry || table.Bounds == null)
                continue;

            var minX = Clamp(table.Bounds.MinX, usableMinX, usableMaxX);
            var minY = Clamp(table.Bounds.MinY, usableMinY, usableMaxY);
            var maxX = Clamp(table.Bounds.MaxX, usableMinX, usableMaxX);
            var maxY = Clamp(table.Bounds.MaxY, usableMinY, usableMaxY);

            if (maxX - minX < MinObstacleSize || maxY - minY < MinObstacleSize)
                continue;

            var widthRatio = (maxX - minX) / (usableMaxX - usableMinX);
            var heightRatio = (maxY - minY) / (usableMaxY - usableMinY);
            // Use OR: canvas markers from an A0 template clamped onto a smaller sheet
            // produce a rect that spans the full width but not the full height.
            // Skipping on either dimension prevents blocking the entire usable area.
            if (widthRatio >= FullSheetCoverageRatio || heightRatio >= FullSheetCoverageRatio)
                continue;

            reserved.Add(new ReservedRect(minX, minY, maxX, maxY));
        }
    }

    internal static IReadOnlyList<LayoutTableGeometryInfo> ReadLayoutTableGeometries()
        => ReadLayoutInfo().Tables;

    internal static LayoutTableGeometryInfo BuildLayoutTableGeometryInfo(
        int tableId, string name, Segment? segment, bool overlapWithViews = false)
    {
        if (!TryGetSegmentBounds(segment, out var bounds))
        {
            return new LayoutTableGeometryInfo
            {
                TableId = tableId,
                Name = name,
                OverlapWithViews = overlapWithViews,
                HasGeometry = false
            };
        }

        return new LayoutTableGeometryInfo
        {
            TableId = tableId,
            Name = name,
            OverlapWithViews = overlapWithViews,
            HasGeometry = true,
            Bounds = bounds
        };
    }

    /// <summary>
    /// Gets table bounds from Tekla canvas marker primitives.
    /// This marker-based path is the canonical svMCP contract for layout-table bounds:
    /// `Primitives[0]` = min-corner marker, `Primitives[2]` = max-corner marker.
    /// Generic primitive accumulation is only a safety fallback.
    /// </summary>
    internal static bool TryGetSegmentBounds(Segment? segment, out ReservedRect bounds)
    {
        if (segment == null) { bounds = new ReservedRect(0, 0, 0, 0); return false; }

        // Canonical path: canvas marker primitives emitted by the Tekla template engine.
        // Primitives[0] = LinePrimitive -> canvas min corner
        // Primitives[2] = LinePrimitive -> canvas max corner
        if (TryGetCanvasBounds(segment, out bounds))
            return true;

        // Safety fallback only. Do not treat this as the primary source of truth.
        var acc = new BoundsAccumulator();
        AccumulatePrimitiveBounds(segment, ref acc);
        if (!acc.HasValue) { bounds = new ReservedRect(0, 0, 0, 0); return false; }
        bounds = new ReservedRect(acc.MinX, acc.MinY, acc.MaxX, acc.MaxY);
        return true;
    }

    private static bool TryGetCanvasBounds(Segment segment, out ReservedRect bounds)
    {
        bounds = new ReservedRect(0, 0, 0, 0);
        var primitives = segment.Primitives;
        if (primitives == null || primitives.Count < 3)
            return false;

        if (primitives[0] is not LinePrimitive minLine ||
            primitives[2] is not LinePrimitive maxLine)
            return false;

        var minX = minLine.StartPoint.X;
        var minY = minLine.StartPoint.Y;
        var maxX = maxLine.StartPoint.X;
        var maxY = maxLine.StartPoint.Y;

        if (maxX <= minX || maxY <= minY)
            return false;

        bounds = new ReservedRect(minX, minY, maxX, maxY);
        return true;
    }

    private static IReadOnlyList<ReservedRect> MergeOverlaps(List<ReservedRect> source)
    {
        if (source.Count <= 1)
            return source;

        var pending = source.OrderBy(r => r.MinX).ThenBy(r => r.MinY).ToList();
        var merged = new List<ReservedRect>();
        foreach (var rect in pending)
        {
            var current = rect;
            var mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (var i = merged.Count - 1; i >= 0; i--)
                {
                    if (!Intersects(current, merged[i])) continue;
                    current = new ReservedRect(
                        System.Math.Min(current.MinX, merged[i].MinX),
                        System.Math.Min(current.MinY, merged[i].MinY),
                        System.Math.Max(current.MaxX, merged[i].MaxX),
                        System.Math.Max(current.MaxY, merged[i].MaxY));
                    merged.RemoveAt(i);
                    mergedAny = true;
                }
            }
            merged.Add(current);
        }
        return merged;
    }

    private static bool Intersects(ReservedRect a, ReservedRect b) =>
        a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;

    private static void AccumulatePrimitiveBounds(PrimitiveBase primitive, ref BoundsAccumulator acc)
    {
        switch (primitive)
        {
            case Segment seg:
                foreach (var c in seg.Primitives) AccumulatePrimitiveBounds(c, ref acc);
                return;
            case PrimitiveGroup grp:
                foreach (var c in grp.Primitives) AccumulatePrimitiveBounds(c, ref acc);
                return;
            case LinePrimitive line:
                Include(ref acc, line.StartPoint);
                Include(ref acc, line.EndPoint);
                return;
            case PathPrimitive path:
                foreach (var s in path.Segments) AccumulatePathable(s, ref acc);
                return;
            case LoopPrimitive loop:
                foreach (var s in loop.Segments) AccumulatePathable(s, ref acc);
                return;
            case PolygonPrimitive polygon:
                AccumulatePrimitiveBounds(polygon.OuterLoop, ref acc);
                if (polygon.InnerLoops != null)
                    foreach (var inner in polygon.InnerLoops) AccumulatePrimitiveBounds(inner, ref acc);
                return;
            case ArcPrimitive arc:
                IncludeArcBounds(ref acc, arc);
                return;
            case CirclePrimitive circle:
                Include(ref acc, circle.CenterPoint.X - circle.Radius, circle.CenterPoint.Y - circle.Radius);
                Include(ref acc, circle.CenterPoint.X + circle.Radius, circle.CenterPoint.Y + circle.Radius);
                return;
            case PointPrimitive point:
                Include(ref acc, point.Position);
                return;
            case BitmapPrimitive bitmap:
                IncludeRotatedBox(ref acc, bitmap.Position, bitmap.Width, bitmap.Height, bitmap.Angle.Radians);
                return;
            case SymbolPrimitive symbol:
                IncludeRotatedBox(ref acc, symbol.Position, symbol.Width, symbol.Height, symbol.Angle);
                return;
            case TextPrimitive text:
                IncludeEstimatedTextBox(ref acc, text);
                return;
        }
    }

    private static void AccumulatePathable(IPathable pathable, ref BoundsAccumulator acc)
    {
        switch (pathable)
        {
            case ArcPrimitive arc: IncludeArcBounds(ref acc, arc); break;
            case PathPrimitive path:
                foreach (var s in path.Segments) AccumulatePathable(s, ref acc);
                break;
            default:
                Include(ref acc, pathable.StartPoint);
                Include(ref acc, pathable.EndPoint);
                break;
        }
    }

    private static void IncludeArcBounds(ref BoundsAccumulator acc, ArcPrimitive arc)
    {
        var geometry = arc.GetArc();
        Include(ref acc, arc.StartPoint);
        Include(ref acc, arc.EndPoint);
        var start = NormalizeRadians(geometry.StartAngle.Radians);
        var end = NormalizeRadians(start + geometry.DeltaAngle.Radians);
        var r = geometry.Circle.Radius;
        var c = geometry.Circle.Center;
        foreach (var angle in new[] { 0.0, Math.PI / 2, Math.PI, Math.PI * 1.5 })
        {
            var a = NormalizeRadians(angle);
            if (AngleWithinSweep(a, start, end))
                Include(ref acc, c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
    }

    private static bool AngleWithinSweep(double angle, double start, double end)
    {
        while (end < start) end += Math.PI * 2;
        while (angle < start) angle += Math.PI * 2;
        return angle <= end;
    }

    private static double NormalizeRadians(double a)
    {
        var full = Math.PI * 2;
        a %= full;
        return a < 0 ? a + full : a;
    }

    private static void IncludeEstimatedTextBox(ref BoundsAccumulator acc, TextPrimitive text)
    {
        var width = Math.Max(text.Height, text.Text?.Length > 0
            ? text.Text.Length * text.Height * Math.Max(text.Proportion, 0.5)
            : text.Height);
        IncludeRotatedBox(ref acc, text.Position, width, text.Height, text.Angle);
    }

    private static void IncludeRotatedBox(ref BoundsAccumulator acc, Vector2 o, double w, double h, double angle)
    {
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        Include(ref acc, o.X, o.Y);
        Include(ref acc, o.X + w * cos, o.Y + w * sin);
        Include(ref acc, o.X - h * sin, o.Y + h * cos);
        Include(ref acc, o.X + w * cos - h * sin, o.Y + w * sin + h * cos);
    }

    private static void Include(ref BoundsAccumulator acc, Vector2 p) => Include(ref acc, p.X, p.Y);

    private static void Include(ref BoundsAccumulator acc, double x, double y)
    {
        if (!acc.HasValue)
        {
            acc = new BoundsAccumulator { HasValue = true, MinX = x, MinY = y, MaxX = x, MaxY = y };
            return;
        }
        if (x < acc.MinX) acc.MinX = x;
        if (y < acc.MinY) acc.MinY = y;
        if (x > acc.MaxX) acc.MaxX = x;
        if (y > acc.MaxY) acc.MaxY = y;
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;

    private struct BoundsAccumulator
    {
        public bool HasValue;
        public double MinX, MinY, MaxX, MaxY;
    }
}

public sealed class LayoutTableGeometryInfo
{
    public int    TableId         { get; set; }
    public string Name            { get; set; } = "";
    public bool   OverlapWithViews { get; set; }
    public bool   HasGeometry     { get; set; }
    public ReservedRect? Bounds { get; set; }
}

