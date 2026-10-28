using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Debounced project search. Scope is Assets only (AssetDatabase.FindAssets). Results are flat ContentItems
    /// (folders first, natural order); building them never loads an asset.
    /// </summary>
    public class SearchController
    {
        public const int DebounceMs = 200;

        readonly VisualElement _host;
        IVisualElementScheduledItem _pending;
        string _query = "";

        public event Action<IReadOnlyList<ContentItem>> ResultsReady;
        /// <summary>Raised when the query became empty: the owner shows the current folder again.</summary>
        public event Action Cleared;

        public SearchController(VisualElement host) { _host = host; }

        public string Query => _query;
        public bool IsActive { get; private set; }

        /// <summary>Sets the query. Non-empty: results arrive after the debounce. Empty/whitespace: clears at once.</summary>
        public void SetQuery(string text)
        {
            text = text?.Trim() ?? "";
            _query = text;
            _pending?.Pause();
            if (text.Length == 0)
            {
                bool was = IsActive;
                IsActive = false;
                if (was) Cleared?.Invoke();
                return;
            }
            IsActive = true;
            if (_pending == null) _pending = _host.schedule.Execute(RunNow);
            _pending.ExecuteLater(DebounceMs);
        }

        /// <summary>Runs the current query now (no debounce) and raises ResultsReady. No-op if inactive.</summary>
        public void RunNow()
        {
            _pending?.Pause();
            if (!IsActive) return;
            ResultsReady?.Invoke(Search(_query));
        }

        /// <summary>Drops the query without raising Cleared (the owner is already switching view).</summary>
        public void Reset()
        {
            _pending?.Pause();
            _query = "";
            IsActive = false;
        }

        // ---- data ----

        public static List<ContentItem> Search(string query)
        {
            var guids = AssetDatabase.FindAssets(query, new[] { "Assets" });
            var paths = new List<string>(guids.Length);
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
            }
            return FromPaths(paths);
        }

        /// <summary>Items for explicit instance ids (Select Dependencies); sub-assets collapse to their file.</summary>
        public static List<ContentItem> FromInstanceIds(IEnumerable<int> ids)
        {
            var seen = new HashSet<string>();
            var paths = new List<string>();
            foreach (int id in ids)
            {
                string p = AssetDatabase.GetAssetPath(id);
                if (string.IsNullOrEmpty(p) || !(p.StartsWith("Assets/", StringComparison.Ordinal) || p.StartsWith("Packages/", StringComparison.Ordinal))) continue;
                if (seen.Add(p)) paths.Add(p);
            }
            return FromPaths(paths);
        }

        static List<ContentItem> FromPaths(List<string> paths)
        {
            var rows = new List<(string file, ContentItem item)>(paths.Count);
            foreach (var p in paths)
            {
                bool folder = AssetDatabase.IsValidFolder(p);
                string file = p.Substring(p.LastIndexOf('/') + 1);
                rows.Add((file, new ContentItem
                {
                    Path = p,
                    Guid = AssetDatabase.AssetPathToGUID(p),
                    IsFolder = folder,
                    Name = folder ? file : Path.GetFileNameWithoutExtension(file),
                }));
            }
            rows.Sort((a, b) =>
            {
                if (a.item.IsFolder != b.item.IsFolder) return a.item.IsFolder ? -1 : 1;
                int c = EditorUtility.NaturalCompare(a.file, b.file);
                return c != 0 ? c : string.CompareOrdinal(a.item.Path, b.item.Path);
            });
            var result = new List<ContentItem>(rows.Count);
            foreach (var r in rows) result.Add(r.item);
            return result;
        }
    }
}
