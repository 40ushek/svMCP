using System;
using PresentationConnection = Tekla.Structures.DrawingPresentationModelInterface.Connection;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One presentation connection per bridge process. Creating one costs seconds and the
/// bridge is persistent. Callers must not dispose it; after any failure they call <see cref="Reset"/>
/// so the next use connects again (for example after Tekla restarts).</summary>
internal static class DrawingPresentationConnections
{
    private static readonly object Lock = new();
    private static PresentationConnection? _connection;

    internal static PresentationConnection Get()
    {
        lock (Lock) return _connection ??= new PresentationConnection();
    }

    internal static PresentationConnection? TryGet()
    {
        try { return Get(); }
        catch { Reset(); return null; }
    }

    /// <summary>Reads one object's presentation. A failed call drops the cached connection and
    /// rethrows, so callers that swallow the exception still leave no broken connection behind.</summary>
    internal static Tekla.Structures.DrawingPresentationModel.Segment? GetPresentation(
        PresentationConnection connection, int objectId)
    {
        try { return connection.Service.GetObjectPresentation(objectId); }
        catch { Reset(); throw; }
    }

    internal static void Reset()
    {
        lock (Lock)
        {
            try { _connection?.Dispose(); } catch { }
            _connection = null;
        }
    }
}
