using System;
using System.IO;
using System.Text;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterProject.Editor
{
    /// <summary>
    /// Shared favourites, stored in a committed file (ProjectSettings/BetterProjectFavourites.json).
    /// The file is written only when the list changes, and never while it is unreadable (e.g. merge conflict markers),
    /// so a broken file is left untouched for the user to fix.
    /// </summary>
    public static class FavouritesStore
    {
        public const string FilePath = "ProjectSettings/BetterProjectFavourites.json";
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        static FavouritesList s_List = new FavouritesList();
        static bool s_Loaded;
        static bool s_LoadFailed;
        static string s_LoadError;
        static DateTime s_Stamp;
        static long s_Length = -1;

        /// <summary>Raised after the list or the load state changed (add, remove, external edit picked up).</summary>
        public static event Action Changed;

        public static FavouritesList List { get { EnsureLoaded(); return s_List; } }
        public static bool LoadFailed { get { EnsureLoaded(); return s_LoadFailed; } }
        /// <summary>Why the file could not be read (null when LoadFailed is false).</summary>
        public static string LoadError { get { EnsureLoaded(); return s_LoadError; } }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.projectChanged += ReloadIfChanged;
            EditorApplication.focusChanged += focused => { if (focused) ReloadIfChanged(); };
        }

        /// <summary>Adds the asset / folder / sub-asset. False if not an asset, already present, or the file is unreadable.</summary>
        public static bool Add(Object o)
        {
            ReloadIfChanged();
            EnsureLoaded();
            if (!AssetRefUtil.TryFromObject(o, out var r)) return false;
            if (s_LoadFailed)
            {
                Debug.LogWarning("[Better Project] Favourite not added: " + FilePath + " is unreadable (" + s_LoadError + "). Fix the file first.");
                return false;
            }
            if (!s_List.Add(r, AssetRefUtil.NameOf(o, r))) return false;
            Save();
            Changed?.Invoke();
            return true;
        }

        public static bool Contains(AssetRef r)
        {
            foreach (var e in List.Items) if (e.Ref.Equals(r)) return true;
            return false;
        }

        public static bool Remove(AssetRef r)
        {
            ReloadIfChanged();
            EnsureLoaded();
            if (s_LoadFailed)
            {
                Debug.LogWarning("[Better Project] Favourite not removed: " + FilePath + " is unreadable (" + s_LoadError + "). Fix the file first.");
                return false;
            }
            if (!s_List.Remove(r)) return false;
            Save();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Re-reads the file if it changed on disk since it was last read or written (external edit, VCS update).</summary>
        public static void ReloadIfChanged()
        {
            if (!s_Loaded) return; // nothing read yet: the first access loads
            ReadStamp(out var stamp, out long length);
            if (stamp == s_Stamp && length == s_Length) return;
            Load();
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (!s_Loaded) Load();
        }

        static void ReadStamp(out DateTime stamp, out long length)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists) { stamp = DateTime.MinValue; length = -1; return; }
                stamp = info.LastWriteTimeUtc;
                length = info.Length;
            }
            catch (Exception) { stamp = DateTime.MinValue; length = -1; }
        }

        static void Load()
        {
            s_Loaded = true;
            ReadStamp(out s_Stamp, out s_Length);
            s_LoadFailed = false;
            s_LoadError = null;
            if (s_Length < 0) { s_List = new FavouritesList(); return; } // no file yet: created on first add
            try
            {
                string json = File.ReadAllText(FilePath, Utf8NoBom); // strips a BOM (editors on Windows may add one)
                s_List = RefListJson.ReadFavourites(json);
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                s_List = new FavouritesList();
                s_LoadFailed = true;
                s_LoadError = e.Message;
            }
        }

        static void Save()
        {
            if (s_LoadFailed) return; // never overwrite a file the user has to fix
            string text = RefListJson.WriteFavourites(s_List);
            try
            {
                string current = File.Exists(FilePath) ? File.ReadAllText(FilePath, Utf8NoBom) : null;
                if (current != text) File.WriteAllText(FilePath, text, Utf8NoBom);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Better Project] Could not write " + FilePath + ": " + e.Message);
            }
            ReadStamp(out s_Stamp, out s_Length);
        }
    }
}
