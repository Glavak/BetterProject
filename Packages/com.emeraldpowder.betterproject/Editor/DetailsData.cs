using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterProject.Editor
{
    /// <summary>
    /// Cell texts of the details view, computed on first request per item and column (only bound rows ask), cached
    /// until <see cref="Invalidate"/> (project changed). Only the Info column loads assets.
    /// </summary>
    static class DetailsData
    {
        static readonly Dictionary<string, string[]> s_Cache = new Dictionary<string, string[]>();
        static readonly int ColumnCount = Enum.GetValues(typeof(DetailsColumn)).Length;

        static readonly HashSet<string> ModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".max", ".ma", ".mb", ".dxf", ".gltf", ".glb" };

        public static void Invalidate() => s_Cache.Clear();

        /// <summary>Text of <paramref name="column"/> for the item (Name is not handled here).</summary>
        public static string Get(ContentItem item, DetailsColumn column)
        {
            if (string.IsNullOrEmpty(item.Guid) || column == DetailsColumn.Name) return ""; // pending placeholder
            string key = item.IsSubAsset ? item.Path + "#" + item.InstanceId : item.Path;
            if (!s_Cache.TryGetValue(key, out var row))
            {
                row = new string[ColumnCount];
                s_Cache[key] = row;
            }
            ref string cell = ref row[(int)column];
            if (cell != null) return cell;
            try { cell = Compute(item, column) ?? ""; }
            catch (Exception) { cell = ""; } // file gone / locked mid-refresh: show nothing
            return cell;
        }

        static string Compute(ContentItem item, DetailsColumn column)
        {
            switch (column)
            {
                case DetailsColumn.Type: return TypeOf(item);
                case DetailsColumn.Size:
                    if (item.IsFolder || item.IsSubAsset) return "";
                    var file = new FileInfo(FileUtil.GetPhysicalPath(item.Path));
                    return file.Exists ? EditorUtility.FormatBytes(file.Length) : "";
                case DetailsColumn.Modified:
                {
                    if (item.IsSubAsset) return "";
                    string disk = FileUtil.GetPhysicalPath(item.Path);
                    DateTime? t = item.IsFolder
                        ? Directory.Exists(disk) ? Directory.GetLastWriteTime(disk) : (DateTime?)null
                        : File.Exists(disk) ? File.GetLastWriteTime(disk) : (DateTime?)null;
                    return t?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }
                case DetailsColumn.Labels:
                    if (item.IsSubAsset || !GUID.TryParse(item.Guid, out var guid)) return "";
                    return string.Join(", ", AssetDatabase.GetLabels(guid));
                case DetailsColumn.Info: return InfoOf(item);
                default: return "";
            }
        }

        static string TypeOf(ContentItem item)
        {
            if (item.IsFolder) return "Folder";
            if (item.IsSubAsset)
            {
                var sub = EditorUtility.InstanceIDToObject(item.InstanceId);
                return sub != null ? ObjectNames.NicifyVariableName(sub.GetType().Name) : "";
            }
            string ext = Path.GetExtension(item.Path);
            if (string.Equals(ext, ".prefab", StringComparison.OrdinalIgnoreCase)) return "Prefab";
            if (ModelExtensions.Contains(ext)) return "Model";
            var type = AssetDatabase.GetMainAssetTypeAtPath(item.Path); // from the asset database, no load
            if (type == null) return "";
            if (type == typeof(SceneAsset)) return "Scene";
            if (type == typeof(MonoScript)) return "Script";
            if (type == typeof(DefaultAsset)) return ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " File" : "File";
            return ObjectNames.NicifyVariableName(type.Name);
        }

        // Texture / sprite: pixel size; audio: length; material: shader. Loads the asset (bound rows only, once).
        static string InfoOf(ContentItem item)
        {
            if (item.IsFolder) return "";
            if (item.IsSubAsset)
            {
                var sub = EditorUtility.InstanceIDToObject(item.InstanceId);
                return sub != null ? InfoOf(sub) : "";
            }
            var type = AssetDatabase.GetMainAssetTypeAtPath(item.Path);
            if (type == null) return "";
            if (typeof(Texture).IsAssignableFrom(type)) return InfoOf(AssetDatabase.LoadAssetAtPath<Texture>(item.Path));
            if (typeof(AudioClip).IsAssignableFrom(type)) return InfoOf(AssetDatabase.LoadAssetAtPath<AudioClip>(item.Path));
            if (typeof(Material).IsAssignableFrom(type)) return InfoOf(AssetDatabase.LoadAssetAtPath<Material>(item.Path));
            return "";
        }

        static string InfoOf(Object obj)
        {
            switch (obj)
            {
                case Texture t: return Dimensions(t.width, t.height);
                case Sprite s: return Dimensions(Mathf.RoundToInt(s.rect.width), Mathf.RoundToInt(s.rect.height));
                case AudioClip a: return DetailsColumns.FormatDuration(a.length);
                case Material m: return m.shader != null ? m.shader.name : "";
                default: return "";
            }
        }

        static string Dimensions(int w, int h) => w.ToString(CultureInfo.InvariantCulture) + "×" + h.ToString(CultureInfo.InvariantCulture);
    }
}
