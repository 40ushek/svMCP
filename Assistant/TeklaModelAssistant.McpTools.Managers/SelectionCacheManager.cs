using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TeklaModelAssistant.McpTools.Managers
{
	public class SelectionCacheManager : ISelectionCacheManager
	{
		private class IdEntry
		{
			public List<int> Ids { get; set; }

			public DateTime StoredAtUtc { get; set; }
		}

		private readonly ConcurrentDictionary<string, IdEntry> _idsBySelection = new ConcurrentDictionary<string, IdEntry>();

		private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(30.0);

		public string CreateSelection(IEnumerable<int> ids)
		{
			if (ids == null)
			{
				return null;
			}
			IdEntry entry = new IdEntry
			{
				Ids = ids.ToList(),
				StoredAtUtc = DateTime.UtcNow
			};
			string selectionId = Guid.NewGuid().ToString("N");
			_idsBySelection[selectionId] = entry;
			CleanupExpired();
			return selectionId;
		}

		public bool TryGetIdsBySelectionId(string selectionId, out List<int> ids)
		{
			ids = null;
			if (string.IsNullOrWhiteSpace(selectionId))
			{
				return false;
			}
			if (_idsBySelection.TryGetValue(selectionId, out var entry))
			{
				if (DateTime.UtcNow - entry.StoredAtUtc <= _defaultTtl)
				{
					ids = entry.Ids;
					return true;
				}
				_idsBySelection.TryRemove(selectionId, out var _);
			}
			return false;
		}

		private void CleanupExpired()
		{
			DateTime now = DateTime.UtcNow;
			List<string> expiredKeys = (from kvp in _idsBySelection
				where now - kvp.Value.StoredAtUtc > _defaultTtl
				select kvp.Key).ToList();
			foreach (string key in expiredKeys)
			{
				_idsBySelection.TryRemove(key, out var _);
			}
		}
	}
}
