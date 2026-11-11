using System.Collections.Generic;

namespace BetterProject.Core
{
    /// <summary>An asset reference (<see cref="Ref"/>) or, when <see cref="Query"/> is set, a saved search.</summary>
    public sealed class FavouriteEntry
    {
        public AssetRef Ref { get; }
        public string Name { get; }
        /// <summary>Search text of a saved search; null for asset entries.</summary>
        public string Query { get; }
        public bool IsSearch => Query != null;

        public FavouriteEntry(AssetRef r, string name)
        {
            Ref = r;
            Name = name ?? "";
        }

        FavouriteEntry(string query)
        {
            Query = query;
            Name = SearchName(query);
        }

        internal static FavouriteEntry Search(string query) => new FavouriteEntry(query);

        /// <summary>
        /// Label of a saved search: the query itself, or for a lone type filter "All" + the type pluralized
        /// ("t:Scene" -> "All Scenes", "t:UnityEngine.Mesh" -> "All Meshes").
        /// </summary>
        public static string SearchName(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 3 || (query[0] != 't' && query[0] != 'T') || query[1] != ':') return query;
            string type = query.Substring(2).Trim();
            int dot = type.LastIndexOf('.');
            if (dot >= 0) type = type.Substring(dot + 1);
            if (type.Length == 0 || !IsIdentifier(type)) return query;
            return "All " + char.ToUpperInvariant(type[0]) + Plural(type.Substring(1));
        }

        static bool IsIdentifier(string s)
        {
            if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        static string Plural(string rest)
        {
            if (rest.Length == 0) return "s";
            string lower = rest.ToLowerInvariant();
            if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh"))
                return rest + "es";
            if (lower.EndsWith("y") && rest.Length >= 2 && "aeiou".IndexOf(lower[lower.Length - 2]) < 0)
                return rest.Substring(0, rest.Length - 1) + "ies";
            return rest + "s";
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

        /// <summary>Appends a saved search (trimmed). False (no change) if empty or already present.</summary>
        public bool AddSearch(string query)
        {
            query = query?.Trim() ?? "";
            if (query.Length == 0 || IndexOfSearch(query) >= 0) return false;
            _items.Add(FavouriteEntry.Search(query));
            return true;
        }

        public bool Remove(AssetRef r)
        {
            int i = IndexOf(r);
            if (i < 0) return false;
            _items.RemoveAt(i);
            return true;
        }

        public bool RemoveSearch(string query)
        {
            int i = IndexOfSearch(query?.Trim());
            if (i < 0) return false;
            _items.RemoveAt(i);
            return true;
        }

        public bool Contains(AssetRef r) => IndexOf(r) >= 0;
        public bool ContainsSearch(string query) => IndexOfSearch(query?.Trim()) >= 0;

        int IndexOf(AssetRef r)
        {
            for (int i = 0; i < _items.Count; i++)
                if (!_items[i].IsSearch && _items[i].Ref.Equals(r)) return i;
            return -1;
        }

        int IndexOfSearch(string query)
        {
            if (string.IsNullOrEmpty(query)) return -1;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Query == query) return i;
            return -1;
        }
    }
}
