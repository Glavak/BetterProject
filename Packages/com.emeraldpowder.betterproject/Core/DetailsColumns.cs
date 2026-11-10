using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterProject.Core
{
    public enum DetailsColumn { Name, Type, Size, Modified, Labels, Info }

    /// <summary>
    /// Columns of the details (table) view: their order, which are shown and their widths. Name is always shown.
    /// Saved as a string like "name:200,type:110,-size:70" ('-' = hidden), tolerant of unknown and missing columns.
    /// </summary>
    public sealed class DetailsColumns
    {
        public const float MinWidth = 30f;
        public const float MaxWidth = 800f;

        public struct Column
        {
            public DetailsColumn Id;
            public bool Visible;
            public float Width;
        }

        static readonly Column[] Defaults =
        {
            new Column { Id = DetailsColumn.Name, Visible = true, Width = 200 },
            new Column { Id = DetailsColumn.Type, Visible = false, Width = 100 },
            new Column { Id = DetailsColumn.Size, Visible = true, Width = 70 },
            new Column { Id = DetailsColumn.Modified, Visible = false, Width = 115 },
            new Column { Id = DetailsColumn.Labels, Visible = false, Width = 110 },
            new Column { Id = DetailsColumn.Info, Visible = true, Width = 80 },
        };

        readonly List<Column> _columns = new List<Column>();

        public DetailsColumns() => _columns.AddRange(Defaults);

        /// <summary>All columns, shown or not, in display order.</summary>
        public IReadOnlyList<Column> All => _columns;

        public List<Column> Visible()
        {
            var result = new List<Column>();
            foreach (var c in _columns) if (c.Visible) result.Add(c);
            return result;
        }

        public bool IsVisible(DetailsColumn id) => _columns[IndexOf(id)].Visible;

        public float WidthOf(DetailsColumn id) => _columns[IndexOf(id)].Width;

        /// <summary>Shows or hides a column; Name cannot be hidden. Returns true if anything changed.</summary>
        public bool SetVisible(DetailsColumn id, bool visible)
        {
            if (id == DetailsColumn.Name) return false;
            int i = IndexOf(id);
            var c = _columns[i];
            if (c.Visible == visible) return false;
            c.Visible = visible;
            _columns[i] = c;
            return true;
        }

        /// <summary>Sets a column's width, clamped to MinWidth..MaxWidth. Returns true if it changed.</summary>
        public bool SetWidth(DetailsColumn id, float width)
        {
            int i = IndexOf(id);
            var c = _columns[i];
            float w = ClampWidth(width);
            if (c.Width == w) return false;
            c.Width = w;
            _columns[i] = c;
            return true;
        }

        /// <summary>
        /// Moves a shown column to gap <paramref name="visibleGap"/> between the shown columns (0 = before the first,
        /// count = after the last). Hidden columns keep their place relative to the others. Returns true if the order changed.
        /// </summary>
        public bool MoveTo(DetailsColumn id, int visibleGap)
        {
            int from = IndexOf(id);
            if (!_columns[from].Visible) return false;
            var visible = Visible();
            visibleGap = Math.Max(0, Math.Min(visibleGap, visible.Count));
            int self = visible.FindIndex(c => c.Id == id);
            if (visibleGap == self || visibleGap == self + 1) return false; // the gaps on either side of itself
            var moved = _columns[from];
            _columns.RemoveAt(from);
            // Before the shown column at that gap, or right after the last shown one.
            int at = visibleGap < visible.Count
                ? IndexIn(_columns, visible[visibleGap].Id)
                : IndexIn(_columns, visible[visible.Count - 1].Id) + 1;
            _columns.Insert(at, moved);
            return true;
        }

        public void Reset()
        {
            _columns.Clear();
            _columns.AddRange(Defaults);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var c in _columns)
            {
                if (sb.Length > 0) sb.Append(',');
                if (!c.Visible) sb.Append('-');
                sb.Append(Key(c.Id)).Append(':').Append(c.Width.ToString("0.#", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Reads <see cref="Serialize"/> output. Unknown entries are skipped, missing columns are added at the end.</summary>
        public static DetailsColumns Parse(string text)
        {
            var result = new DetailsColumns();
            if (string.IsNullOrEmpty(text)) return result;
            var parsed = new List<Column>();
            foreach (var part in text.Split(','))
            {
                string p = part.Trim();
                bool visible = true;
                if (p.StartsWith("-", StringComparison.Ordinal)) { visible = false; p = p.Substring(1); }
                int colon = p.IndexOf(':');
                string key = colon < 0 ? p : p.Substring(0, colon);
                if (!TryFromKey(key, out var id) || parsed.Exists(c => c.Id == id)) continue;
                float width = Defaults[(int)id].Width;
                if (colon >= 0 && float.TryParse(p.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
                    width = ClampWidth(w);
                parsed.Add(new Column { Id = id, Visible = visible || id == DetailsColumn.Name, Width = width });
            }
            foreach (var d in Defaults) if (!parsed.Exists(c => c.Id == d.Id)) parsed.Add(d);
            result._columns.Clear();
            result._columns.AddRange(parsed);
            return result;
        }

        public static string Title(DetailsColumn id)
        {
            switch (id)
            {
                case DetailsColumn.Name: return "Name";
                case DetailsColumn.Type: return "Type";
                case DetailsColumn.Size: return "Size";
                case DetailsColumn.Modified: return "Date Modified";
                case DetailsColumn.Labels: return "Labels";
                default: return "Info";
            }
        }

        public static string Key(DetailsColumn id) => id.ToString().ToLowerInvariant();

        static bool TryFromKey(string key, out DetailsColumn id)
        {
            foreach (DetailsColumn c in Enum.GetValues(typeof(DetailsColumn)))
                if (Key(c) == key) { id = c; return true; }
            id = default;
            return false;
        }

        static float ClampWidth(float w) => float.IsNaN(w) ? MinWidth : Math.Max(MinWidth, Math.Min(MaxWidth, w));

        int IndexOf(DetailsColumn id) => IndexIn(_columns, id);

        static int IndexIn(List<Column> list, DetailsColumn id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        /// <summary>"0.53 s" under a minute, then "m:ss", then "h:mm:ss".</summary>
        public static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return "";
            if (seconds < 60) return seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            long total = (long)Math.Round(seconds);
            long h = total / 3600, m = total / 60 % 60, s = total % 60;
            return h > 0
                ? h.ToString(CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture)
                : m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
