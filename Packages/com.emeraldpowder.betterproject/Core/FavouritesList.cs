using System.Collections.Generic;

namespace BetterProject.Core
{
    public sealed class FavouriteEntry
    {
        public AssetRef Ref { get; }
        public string Name { get; }

        public FavouriteEntry(AssetRef r, string name)
        {
            Ref = r;
            Name = name ?? "";
        }
    }

    public sealed class FavouritesList
    {
        readonly List<FavouriteEntry> _items = new List<FavouriteEntry>();

        public IReadOnlyList<FavouriteEntry> Items => _items;

        /// <summary>Appends. Returns false (no change) if the ref is already present.</summary>
        public bool Add(AssetRef r, string name)
        {
            if (IndexOf(r) >= 0) return false;
            _items.Add(new FavouriteEntry(r, name));
            return true;
        }

        public bool Remove(AssetRef r)
        {
            int i = IndexOf(r);
            if (i < 0) return false;
            _items.RemoveAt(i);
            return true;
        }

        int IndexOf(AssetRef r)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Ref.Equals(r)) return i;
            return -1;
        }
    }
}
