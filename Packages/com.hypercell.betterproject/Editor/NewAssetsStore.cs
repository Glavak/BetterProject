using System;
using System.Collections.Generic;
using System.IO;
using BetterProject.Core;
using UnityEditor;

namespace BetterProject.Editor
{
    /// <summary>
    /// Personal list of newly added assets (UserSettings/BetterProjectNewAssets.json, not committed), newest first, at
    /// most the "Max new assets" setting. Shown by the optional "New assets" section of the left panel.
    /// </summary>
    public static class NewAssetsStore
    {
        public const string FilePath = "UserSettings/BetterProjectNewAssets.json";

        static readonly RefHistoryStore s_Store =
            new RefHistoryStore(FilePath, "newAssets", () => BetterProjectSettings.instance.MaxNewAssets);

        /// <summary>Raised after the list changed.</summary>
        public static event Action Changed
        {
            add => s_Store.Changed += value;
            remove => s_Store.Changed -= value;
        }

        public static RecentsList List => s_Store.List;

        public static void PushRange(IReadOnlyList<string> assetPaths) => s_Store.PushRange(assetPaths);

        public static void Remove(AssetRef r) => s_Store.Remove(r);

        public static void Clear() => s_Store.Clear();
    }

    /// <summary>
    /// Feeds <see cref="NewAssetsStore"/>: an imported asset under Assets/ is new when its .meta file was just created
    /// (a reimport keeps the old .meta). Covers created, duplicated, copied-in and pulled files; not folders or moves.
    /// </summary>
    class NewAssetsTracker : AssetPostprocessor
    {
        // Import runs right after the .meta is written; allow for a slow import of a large batch.
        static readonly TimeSpan NewWindow = TimeSpan.FromMinutes(2);
        static readonly List<string> s_Paths = new List<string>();

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Length == 0) return;
            s_Paths.Clear();
            var moved = new HashSet<string>(movedAssets);
            var now = DateTime.UtcNow;
            foreach (var path in importedAssets)
            {
                if (moved.Contains(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (AssetDatabase.IsValidFolder(path) || s_Paths.Contains(path)) continue;
                if (!IsNew(path + ".meta", now)) continue;
                s_Paths.Add(path);
            }
            if (s_Paths.Count > 0) NewAssetsStore.PushRange(s_Paths); // the last one ends on top
        }

        static bool IsNew(string metaPath, DateTime now)
        {
            try
            {
                if (!File.Exists(metaPath)) return false;
                return now - File.GetCreationTimeUtc(metaPath) <= NewWindow;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }
    }
}
