using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;

namespace BetterProject.Editor
{
    /// <summary>
    /// A personal most-recent-first list of main assets (LocalId 0) in a JSON file under UserSettings (not committed).
    /// The size limit comes from settings and follows them live. An unreadable file is silently reset to empty. Saved on
    /// every change, and only when something changed. Deleted assets are dropped on load and on projectChanged.
    /// Backs <see cref="RecentsStore"/> and <see cref="NewAssetsStore"/>.
    /// </summary>
    public sealed class RefHistoryStore
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly string _filePath;
        readonly string _key;
        readonly Func<int> _limit;
        RecentsList _list;
        string _fileText; // last text read from or written to the file (null = no file / unreadable)

        /// <summary>Raised after the list changed.</summary>
        public event Action Changed;

        /// <param name="filePath">Project-relative file path.</param>
        /// <param name="key">JSON array key in the file.</param>
        /// <param name="limit">Current maximum number of entries (read on load and whenever settings change).</param>
        public RefHistoryStore(string filePath, string key, Func<int> limit)
        {
            _filePath = filePath;
            _key = key;
            _limit = limit;
            EditorApplication.projectChanged += OnProjectChanged;
            BetterProjectSettings.Changed += ApplyLimit;
        }

        public string FilePath => _filePath;

        public RecentsList List { get { EnsureLoaded(); return _list; } }

        /// <summary>Pushes the main assets at these paths in order (the last one ends on top), with at most one save.
        /// Paths without a GUID and folders are ignored.</summary>
        public void PushRange(IReadOnlyList<string> assetPaths)
        {
            if (assetPaths == null) return;
            EnsureLoaded();
            var refs = new List<AssetRef>(assetPaths.Count);
            foreach (var path in assetPaths)
                if (TryMainRef(path, out var r)) refs.Add(r);
            // Only a real order change commits: re-editing the same multi-selection every frame stays a no-op.
            if (refs.Count > 0 && _list.PushRange(refs)) Commit();
        }

        public void Remove(AssetRef r)
        {
            EnsureLoaded();
            if (_list.Remove(r)) Commit();
        }

        public void Clear()
        {
            EnsureLoaded();
            if (_list.Clear()) Commit();
        }

        int Limit => Math.Max(1, _limit());

        void ApplyLimit()
        {
            if (_list == null) return; // the load applies it
            if (_list.SetLimit(Limit)) Commit();
        }

        static bool TryMainRef(string path, out AssetRef r)
        {
            r = default;
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return false;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return false;
            try { r = new AssetRef(guid, 0); }
            catch (ArgumentException) { return false; }
            return true;
        }

        void OnProjectChanged()
        {
            if (_list == null) return; // not loaded yet: the load prunes
            if (PruneMissing() > 0) Commit();
        }

        int PruneMissing() => _list.RemoveWhere(r => AssetRefUtil.PathOf(r) == null);

        void EnsureLoaded()
        {
            if (_list != null) return;
            _list = Read();
            if (PruneMissing() > 0) Save();
        }

        RecentsList Read()
        {
            _fileText = null;
            try
            {
                if (!File.Exists(_filePath)) return new RecentsList(Limit);
                string json = File.ReadAllText(_filePath, Utf8NoBom); // strips a BOM
                var list = RefListJson.ReadRecents(json, Limit, _key);
                _fileText = json;
                return list;
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                return new RecentsList(Limit); // personal data: start over; the next change overwrites the file
            }
        }

        void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        void Save()
        {
            string text = RefListJson.WriteRecents(_list, _key);
            try
            {
                if (text == _fileText) return; // unchanged since last read / write
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                File.WriteAllText(_filePath, text, Utf8NoBom);
                _fileText = text;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Better Project] Could not write " + _filePath + ": " + e.Message);
            }
        }
    }
}
