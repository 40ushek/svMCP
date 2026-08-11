using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Process-local cache for expensive model-solid reads used by drawing geometry APIs.
/// The bridge processes commands sequentially; the lock also keeps this safe if a caller
/// later introduces concurrent command dispatch.
/// </summary>
internal static class DrawingPartGeometryCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, List<PartInView>> Entries = new();

    internal static bool TryGetAll(
        Tekla.Structures.Drawing.Drawing drawing,
        View view,
        int viewId,
        out List<PartInView> results)
    {
        var key = BuildKey(drawing, view, viewId);
        if (key == null)
        {
            results = new();
            return false;
        }

        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out var cached))
            {
                results = new();
                return false;
            }

            results = cached.Select(static part => part.Clone()).ToList();
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_hit", 0,
            $"kind=all drawingId={drawing.GetIdentifier().ID} viewId={viewId} parts={results.Count}");
        return true;
    }

    internal static bool TryGetPart(
        Tekla.Structures.Drawing.Drawing drawing,
        View view,
        int viewId,
        int modelId,
        out PartInView result)
    {
        var key = BuildKey(drawing, view, viewId);
        if (key == null)
        {
            result = new();
            return false;
        }

        lock (Sync)
        {
            if (Entries.TryGetValue(key, out var all)
                && all.FirstOrDefault(part => part.ModelId == modelId) is { } fullPart)
            {
                result = fullPart.CloneGeometryOnly();
            }
            else if (Entries.TryGetValue(PartKey(key, modelId), out var single)
                && single.FirstOrDefault() is { } singlePart)
            {
                result = singlePart.Clone();
            }
            else
            {
                result = new();
                return false;
            }
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_hit", 0,
            $"kind=part drawingId={drawing.GetIdentifier().ID} viewId={viewId} modelId={modelId}");
        return true;
    }

    internal static void StoreAll(
        Tekla.Structures.Drawing.Drawing drawing,
        View view,
        int viewId,
        IReadOnlyList<PartInView> results)
    {
        var key = BuildKey(drawing, view, viewId);
        if (key == null)
            return;

        lock (Sync)
        {
            RemoveViewEntries(key);
            Entries[key] = results.Select(static part => part.Clone()).ToList();
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_store", 0,
            $"kind=all drawingId={drawing.GetIdentifier().ID} viewId={viewId} parts={results.Count}");
    }

    internal static void StorePart(
        Tekla.Structures.Drawing.Drawing drawing,
        View view,
        int viewId,
        PartInView result)
    {
        var key = BuildKey(drawing, view, viewId);
        if (key == null)
            return;

        lock (Sync)
        {
            // Keep a full-view snapshot authoritative. A single-part read must not add
            // a part which is absent from the visible-object enumeration.
            if (Entries.ContainsKey(key))
                return;

            RemoveViewEntries(key);
            Entries[PartKey(key, result.ModelId)] = new List<PartInView> { result.Clone() };
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_store", 0,
            $"kind=part drawingId={drawing.GetIdentifier().ID} viewId={viewId} modelId={result.ModelId}");
    }

    internal static void InvalidateAll()
    {
        lock (Sync)
            Entries.Clear();

        PerfTrace.Write("api-geometry", "parts_geometry_cache_invalidate", 0, "scope=all");
    }

    internal static void InvalidateDrawing(int drawingId)
    {
        lock (Sync)
        {
            foreach (var key in Entries.Keys.Where(key => HasPrefix(key, $"d={drawingId}|")).ToList())
                Entries.Remove(key);
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_invalidate", 0,
            $"scope=drawing drawingId={drawingId}");
    }

    internal static void InvalidateView(int drawingId, int viewId)
    {
        var prefix = $"d={drawingId}|v={viewId}|";
        lock (Sync)
        {
            foreach (var key in Entries.Keys.Where(key => HasPrefix(key, prefix)).ToList())
                Entries.Remove(key);
        }

        PerfTrace.Write("api-geometry", "parts_geometry_cache_invalidate", 0,
            $"scope=view drawingId={drawingId} viewId={viewId}");
    }

    private static string? BuildKey(
        Tekla.Structures.Drawing.Drawing drawing,
        View view,
        int viewId)
    {
        try
        {
            var drawingId = drawing.GetIdentifier().ID;
            var cs = view.ViewCoordinateSystem;
            if (drawingId <= 0 || viewId <= 0 || cs == null)
                return null;

            return string.Format(
                CultureInfo.InvariantCulture,
                "d={0}|v={1}|status={2}|type={3}|scale={4:R}|origin={5:R},{6:R}|csO={7:R},{8:R},{9:R}|csX={10:R},{11:R},{12:R}|csY={13:R},{14:R},{15:R}",
                drawingId,
                viewId,
                drawing.UpToDateStatus,
                view.ViewType,
                view.Attributes?.Scale ?? 0.0,
                view.Origin?.X ?? 0.0,
                view.Origin?.Y ?? 0.0,
                cs.Origin.X,
                cs.Origin.Y,
                cs.Origin.Z,
                cs.AxisX.X,
                cs.AxisX.Y,
                cs.AxisX.Z,
                cs.AxisY.X,
                cs.AxisY.Y,
                cs.AxisY.Z);
        }
        catch
        {
            // A transient Tekla object is not a safe cache key. The caller will read live.
            return null;
        }
    }

    private static string PartKey(string key, int modelId) => $"{key}|model={modelId}";

    private static void RemoveViewEntries(string key)
    {
        var prefix = ViewPrefix(key);
        foreach (var staleKey in Entries.Keys.Where(existing => HasPrefix(existing, prefix)).ToList())
            Entries.Remove(staleKey);
    }

    private static string ViewPrefix(string key)
    {
        var statusIndex = key.IndexOf("|status=", StringComparison.Ordinal);
        return statusIndex >= 0 ? key.Substring(0, statusIndex) + "|" : key + "|";
    }

    private static bool HasPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.Ordinal);
}
