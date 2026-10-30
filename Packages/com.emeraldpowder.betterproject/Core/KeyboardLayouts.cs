using System.Collections.Generic;
using System.Text;

namespace BetterProject.Core
{
    /// <summary>
    /// Retypes a query typed in the wrong keyboard layout into US layout ("руддщ" -> "hello").
    /// Mapping text: one line per US key; first code point is the key, then a space, then one column per layout
    /// (column j = what that key types in layout j, space = no mapping). Columns are aligned by code point, not char.
    /// </summary>
    public sealed class KeyboardLayouts
    {
        readonly List<Dictionary<int, char>> _layouts = new List<Dictionary<int, char>>();

        public int LayoutCount => _layouts.Count;

        public static KeyboardLayouts Parse(string text)
        {
            var result = new KeyboardLayouts();
            if (string.IsNullOrEmpty(text)) return result;
            var columns = new List<Dictionary<int, char>>();
            foreach (var rawLine in text.Split('\n'))
            {
                var cps = CodePoints(rawLine.TrimEnd('\r'));
                if (cps.Count < 3 || cps[0] > 0xFFFF) continue;
                char key = (char)cps[0];
                for (int i = 2; i < cps.Count; i++)
                {
                    int col = i - 2;
                    while (columns.Count <= col) columns.Add(new Dictionary<int, char>());
                    int cp = cps[i];
                    if (cp == ' ' || cp == key) continue;
                    if (!columns[col].ContainsKey(cp)) columns[col][cp] = key;
                }
            }
            foreach (var c in columns)
                if (c.Count > 0) result._layouts.Add(c);
            return result;
        }

        /// <summary>
        /// The query (lower-cased) retyped through every layout, in column order. Skips layouts that can't type some
        /// non-ASCII char of the query (half-converted junk like "hellщ"), and results that equal the lower-cased
        /// query (nothing replaced) or an earlier result. Unmapped ASCII (digits, ':', ...) is kept as is.
        /// </summary>
        public List<string> Variants(string query)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(query)) return result;
            string lower = query.ToLowerInvariant();
            var cps = CodePoints(lower);
            var sb = new StringBuilder(lower.Length);
            foreach (var layout in _layouts)
            {
                sb.Clear();
                bool foreign = false;
                foreach (int cp in cps)
                {
                    if (layout.TryGetValue(cp, out char us)) sb.Append(us);
                    else if (cp > 0x7F) { foreign = true; break; }
                    else sb.Append((char)cp);
                }
                if (foreign) continue;
                string v = sb.ToString();
                if (v != lower && !result.Contains(v)) result.Add(v);
            }
            return result;
        }

        static List<int> CodePoints(string s)
        {
            var list = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    list.Add(char.ConvertToUtf32(s[i], s[i + 1]));
                    i++;
                }
                else list.Add(s[i]);
            }
            return list;
        }
    }
}
