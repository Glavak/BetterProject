using System;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using Object = UnityEngine.Object;

namespace BetterProject.Editor
{
    /// <summary>Converts between Unity objects and GUID + local id references (stable across rename / move).</summary>
    public static class AssetRefUtil
    {
        /// <summary>
        /// Reference to a persistent asset, folder or sub-asset. LocalId is 0 for main assets and folders.
        /// Scene objects and other non-asset objects fail.
        /// </summary>
        public static bool TryFromObject(Object o, out AssetRef r)
        {
            r = default;
            if (o == null || !EditorUtility.IsPersistent(o) || !AssetDatabase.Contains(o)) return false;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long localId)) return false;
            if (string.IsNullOrEmpty(guid)) return false;
            if (AssetDatabase.IsMainAsset(o) || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o))) localId = 0;
            try { r = new AssetRef(guid, localId); }
            catch (ArgumentException) { return false; }
            return true;
        }

        /// <summary>Name captured when a favourite is added: object name, plus the file extension for main assets.</summary>
        public static string NameOf(Object o, AssetRef r)
        {
            if (o == null) return "";
            if (r.IsSubAsset) return o.name;
            string path = AssetDatabase.GetAssetPath(o);
            if (AssetDatabase.IsValidFolder(path)) return o.name;
            return o.name + Path.GetExtension(path);
        }

        /// <summary>Current asset path of the reference, or null if the asset no longer exists.</summary>
        public static string PathOf(AssetRef r)
        {
            string path = AssetDatabase.GUIDToAssetPath(r.Guid);
            if (string.IsNullOrEmpty(path)) return null;
            if (AssetDatabase.GetMainAssetTypeAtPath(path) == null && !AssetDatabase.IsValidFolder(path)) return null;
            return path;
        }

        /// <summary>The referenced object (loads it), or null if it no longer exists.</summary>
        public static Object Resolve(AssetRef r)
        {
            string path = PathOf(r);
            if (path == null) return null;
            if (r.LocalId == 0) return AssetDatabase.LoadMainAssetAtPath(path);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long lid) && lid == r.LocalId) return o;
            }
            return null;
        }
    }
}
