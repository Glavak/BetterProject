using System;
using System.Collections.Generic;
using BetterProject.Core;

namespace BetterProject.Editor
{
    /// <summary>
    /// Personal recents (UserSettings/BetterProjectRecents.json, not committed), most recent first, at most the
    /// "Max recents" setting. See <see cref="RefHistoryStore"/> for loading / saving rules.
    /// </summary>
    public static class RecentsStore
    {
        public const string FilePath = "UserSettings/BetterProjectRecents.json";

        static readonly RefHistoryStore s_Store =
            new RefHistoryStore(FilePath, "recents", () => BetterProjectSettings.instance.MaxRecents);

        /// <summary>Raised after the list changed.</summary>
        public static event Action Changed
        {
            add => s_Store.Changed += value;
            remove => s_Store.Changed -= value;
        }

        public static RecentsList List => s_Store.List;

        /// <summary>Moves the main asset at this path to the top. Ignores paths without a GUID and folders.</summary>
        public static void Push(string assetPath) => PushRange(new[] { assetPath });

        /// <summary>Pushes the paths in order (the last one ends on top) with at most one save.</summary>
        public static void PushRange(IReadOnlyList<string> assetPaths) => s_Store.PushRange(assetPaths);

        public static void Remove(AssetRef r) => s_Store.Remove(r);

        public static void Clear() => s_Store.Clear();
    }
}
