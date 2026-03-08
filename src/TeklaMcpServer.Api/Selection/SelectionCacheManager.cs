using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Selection;

public class SelectionCacheManager : ISelectionCacheManager
{
    private sealed class IdEntry
    {
        public List<int> Ids { get; set; } = new List<int>();

        public DateTime StoredAtUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, IdEntry> _idsBySelection = new();

    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(30);

    public string? CreateSelection(IEnumerable<int> ids)
    {
        if (ids == null)
            return null;

        var entry = new IdEntry
        {
            Ids = ids.Distinct().ToList(),
            StoredAtUtc = DateTime.UtcNow
        };

        var selectionId = Guid.NewGuid().ToString("N");
        _idsBySelection[selectionId] = entry;
        CleanupExpired();
        return selectionId;
    }

    public bool TryGetIdsBySelectionId(string? selectionId, out List<int> ids)
    {
        ids = new List<int>();
        if (string.IsNullOrWhiteSpace(selectionId))
            return false;

        var key = selectionId!;
        if (!_idsBySelection.TryGetValue(key, out var entry))
            return false;

        if (DateTime.UtcNow - entry.StoredAtUtc > _defaultTtl)
        {
            _idsBySelection.TryRemove(key, out _);
            return false;
        }

        ids = entry.Ids;
        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _idsBySelection
            .Where(kvp => now - kvp.Value.StoredAtUtc > _defaultTtl)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            _idsBySelection.TryRemove(key, out _);
    }
}
