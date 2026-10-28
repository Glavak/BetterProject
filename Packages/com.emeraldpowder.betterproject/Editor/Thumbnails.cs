using UnityEditor;
using UnityEngine;

namespace BetterProject.Editor
{
    /// <summary>Thumbnail lookup for content tiles (same sources as the default Project window).</summary>
    public static class Thumbnails
    {
        /// <summary>
        /// Large thumbnail for a tile: the asset preview when ready, otherwise the mini thumbnail.
        /// <paramref name="loading"/> is true while the preview is still being generated (ask again later).
        /// Needs <c>item.InstanceId</c> resolved for non-folder items; falls back to the file icon without it.
        /// </summary>
        public static Texture Get(ContentItem item, out bool loading)
        {
            loading = false;
            if (item.IsFolder) return FolderIcon(item);
            Object obj = item.InstanceId != 0 ? EditorUtility.InstanceIDToObject(item.InstanceId) : null;
            if (obj == null) return AssetDatabase.GetCachedIcon(item.Path);
            Texture preview = AssetPreview.GetAssetPreview(obj);
            loading = preview == null && AssetPreview.IsLoadingAssetPreview(item.InstanceId);
            return preview != null ? preview : AssetPreview.GetMiniThumbnail(obj);
        }

        /// <summary>Small icon for list mode (no preview requests).</summary>
        public static Texture Icon(ContentItem item)
        {
            if (item.IsSubAsset && item.InstanceId != 0)
            {
                var obj = EditorUtility.InstanceIDToObject(item.InstanceId);
                if (obj != null) return AssetPreview.GetMiniThumbnail(obj);
            }
            return AssetDatabase.GetCachedIcon(item.Path);
        }

        static Texture FolderIcon(ContentItem item)
        {
            var icon = AssetDatabase.GetCachedIcon(item.Path);
            return icon != null ? icon : EditorGUIUtility.IconContent("Folder Icon").image;
        }
    }
}
