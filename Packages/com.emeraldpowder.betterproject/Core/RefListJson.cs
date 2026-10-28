using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterProject.Core
{
    /// <summary>
    /// Reads/writes the shared favourites and recents files. Output is byte-stable
    /// (LF, 2-space indent, one entry per line, trailing newline) to avoid diff noise in git.
    /// Readers throw FormatException on any malformed input.
    /// </summary>
    public static class RefListJson
    {
        const int Version = 1;

        public static string WriteFavourites(FavouritesList list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            var sb = Begin("favourites", list.Items.Count);
            for (int i = 0; i < list.Items.Count; i++)
            {
                var e = list.Items[i];
                sb.Append("    { ");
                AppendRef(sb, e.Ref);
                sb.Append(", \"name\": ");
                AppendString(sb, e.Name);
                sb.Append(" }");
                sb.Append(i < list.Items.Count - 1 ? ",\n" : "\n");
            }
            return End(sb, list.Items.Count);
        }

        /// <param name="key">Array key in the file: "recents" for Recents; other lists of the same shape use their own.</param>
        public static string WriteRecents(RecentsList list, string key = "recents")
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            var sb = Begin(key, list.Items.Count);
            for (int i = 0; i < list.Items.Count; i++)
            {
                sb.Append("    { ");
                AppendRef(sb, list.Items[i]);
                sb.Append(" }");
                sb.Append(i < list.Items.Count - 1 ? ",\n" : "\n");
            }
            return End(sb, list.Items.Count);
        }

        public static FavouritesList ReadFavourites(string json)
        {
            var result = new FavouritesList();
            foreach (var o in ReadEntries(json, "favourites"))
            {
                var r = ParseRef(o);
                string name = "";
                if (o.TryGetValue("name", out var n))
                {
                    if (!(n is string ns)) throw new FormatException("'name' must be a string.");
                    name = ns;
                }
                result.Add(r, name); // duplicates: first wins
            }
            return result;
        }

        /// <param name="limit">Keeps the first (most recent) <paramref name="limit"/> entries.</param>
        /// <param name="key">Array key in the file (see <see cref="WriteRecents"/>).</param>
        public static RecentsList ReadRecents(string json, int limit = RecentsList.DefaultLimit, string key = "recents")
        {
            var refs = new List<AssetRef>();
            foreach (var o in ReadEntries(json, key))
            {
                var r = ParseRef(o);
                if (!refs.Contains(r)) refs.Add(r);
            }
            var result = new RecentsList(limit);
            int n = Math.Min(refs.Count, result.Limit);
            // Items are most-recent-first; push oldest first so order is preserved.
            for (int i = n - 1; i >= 0; i--) result.Push(refs[i]);
            return result;
        }

        static StringBuilder Begin(string key, int count)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": ").Append(Version).Append(",\n  \"").Append(key).Append("\": [");
            if (count > 0) sb.Append('\n');
            return sb;
        }

        static string End(StringBuilder sb, int count)
        {
            sb.Append(count > 0 ? "  ]\n}\n" : "]\n}\n");
            return sb.ToString();
        }

        static void AppendRef(StringBuilder sb, AssetRef r)
        {
            sb.Append("\"guid\": \"").Append(r.Guid).Append("\", \"localId\": \"")
              .Append(r.LocalId.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static List<Dictionary<string, object>> ReadEntries(string json, string key)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>
                ?? throw new FormatException("Root must be an object.");
            if (!root.TryGetValue("version", out var v) || !(v is double dv) || dv != Version)
                throw new FormatException("Missing or unsupported 'version'.");
            if (!root.TryGetValue(key, out var arr) || !(arr is List<object> list))
                throw new FormatException("Missing '" + key + "' array.");
            var result = new List<Dictionary<string, object>>(list.Count);
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> d))
                    throw new FormatException("Entries must be objects.");
                result.Add(d);
            }
            return result;
        }

        static AssetRef ParseRef(Dictionary<string, object> o)
        {
            if (!o.TryGetValue("guid", out var g) || !(g is string guid))
                throw new FormatException("Entry 'guid' must be a string.");
            if (!o.TryGetValue("localId", out var l) || !(l is string lid))
                throw new FormatException("Entry 'localId' must be a string.");
            long localId;
            try { localId = long.Parse(lid, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            { throw new FormatException("Entry 'localId' is not a valid integer.", e); }
            try { return new AssetRef(guid, localId); }
            catch (ArgumentException e) { throw new FormatException("Entry 'guid' is invalid.", e); }
        }
    }
}
