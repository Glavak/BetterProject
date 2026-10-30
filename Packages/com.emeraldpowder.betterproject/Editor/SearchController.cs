using System;
using System.Collections.Generic;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
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

        /// <summary>Few results or a long query: also search the query retyped from other keyboard layouts.</summary>
        const int LayoutFallbackMaxResults = 3;
        const int LayoutFallbackMinLength = 5;

        static KeyboardLayouts s_Layouts;

        public static List<ContentItem> Search(string query)
        {
            var seen = new HashSet<string>();
            var paths = new List<string>();
            CollectPaths(query, seen, paths);
            var result = FromPaths(paths);
            if (result.Count >= LayoutFallbackMaxResults && query.Length < LayoutFallbackMinLength) return result;

            var extra = new List<string>();
            foreach (var variant in Layouts.Variants(query)) CollectPaths(variant, seen, extra);
            result.AddRange(FromPaths(extra));
            return result;
        }

        static void CollectPaths(string query, HashSet<string> seen, List<string> paths)
        {
            foreach (var g in AssetDatabase.FindAssets(query, new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(p) && seen.Add(p)) paths.Add(p);
            }
        }

        /// <summary>The package's BetterProjectKeyboardLayouts.txt, found by name (install location varies).</summary>
        static KeyboardLayouts Layouts
        {
            get
            {
                if (s_Layouts != null) return s_Layouts;
                string text = null;
                foreach (var guid in AssetDatabase.FindAssets("BetterProjectKeyboardLayouts t:TextAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/BetterProjectKeyboardLayouts.txt", StringComparison.Ordinal)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (asset != null) { text = asset.text; break; }
                }
                s_Layouts = KeyboardLayouts.Parse(text);
                return s_Layouts;
            }
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
