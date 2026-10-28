using System;
using System.Collections.Generic;

namespace BetterProject.Core
{
    /// <summary>Most-recent-first list of at most <see cref="Limit"/> unique refs.</summary>
    public sealed class RecentsList
    {
        public const int DefaultLimit = 10;

        readonly List<AssetRef> _items = new List<AssetRef>();

        public RecentsList(int limit = DefaultLimit)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be at least 1.");
            Limit = limit;
        }

        /// <summary>Maximum number of items; pushing beyond it drops the oldest.</summary>
        public int Limit { get; private set; }

        public IReadOnlyList<AssetRef> Items => _items;

        /// <summary>Changes the limit, dropping the oldest items beyond it. True if items were dropped.</summary>
        public bool SetLimit(int limit)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be at least 1.");
            Limit = limit;
            if (_items.Count <= limit) return false;
            _items.RemoveRange(limit, _items.Count - limit);
            return true;
        }

        public bool Push(AssetRef r)
        {
            if (_items.Count > 0 && _items[0].Equals(r)) return false;
            _items.Remove(r);
            _items.Insert(0, r);
            if (_items.Count > Limit) _items.RemoveRange(Limit, _items.Count - Limit);
            return true;
        }

        /// <summary>
        /// Pushes the refs in order (the last one ends on top). True only if the list actually changed: re-pushing refs
        /// that end up in the same order (e.g. the same multi-selection edited again) returns false.
        /// </summary>
        public bool PushRange(IEnumerable<AssetRef> refs)
        {
            if (refs == null) throw new ArgumentNullException(nameof(refs));
            var before = _items.ToArray();
            bool pushed = false;
            foreach (var r in refs) pushed |= Push(r);
            if (!pushed || before.Length != _items.Count) return pushed;
            for (int i = 0; i < before.Length; i++)
                if (!before[i].Equals(_items[i])) return true;
            return false;
        }

        public bool Remove(AssetRef r) => _items.Remove(r);

        public bool Clear()
        {
            if (_items.Count == 0) return false;
            _items.Clear();
            return true;
        }

        public int RemoveWhere(Func<AssetRef, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return _items.RemoveAll(r => predicate(r));
        }
    }
}
