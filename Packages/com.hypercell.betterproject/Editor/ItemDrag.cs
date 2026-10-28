using System;
using System.Collections.Generic;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;

namespace BetterProject.Editor
{
    /// <summary>Drag out of the window (objects + paths) and drop-to-move onto a folder.</summary>
    public static class ItemDrag
    {
        public static readonly Color DropColor = new Color(0.24f, 0.48f, 0.9f, 0.55f);
        public static Color SelectedColor => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f, 1f) : new Color(0.24f, 0.48f, 0.90f, 1f);

        /// <summary>
        /// Starts a drag of the items. objectReferences holds every item's object (sub-assets included, so they can be
        /// dropped into fields); paths holds main-asset paths only, so a sub-asset never moves its parent asset.
        /// </summary>
        public static void StartDrag(IReadOnlyList<ContentItem> items)
        {
            var objects = new List<UnityEngine.Object>();
            var paths = new List<string>();
            var seen = new HashSet<string>();
            string title = null;
            foreach (var item in items)
            {
                if (item.Guid == null) continue; // pending-create placeholder
                var obj = item.InstanceId != 0 ? EditorUtility.InstanceIDToObject(item.InstanceId) : null;
                if (obj == null && !item.IsSubAsset) obj = AssetDatabase.LoadMainAssetAtPath(item.Path);
                if (obj == null) continue;
                objects.Add(obj);
                if (!item.IsSubAsset && seen.Add(item.Path)) paths.Add(item.Path);
                if (title == null) title = item.Name;
            }
            if (objects.Count == 0) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objects.ToArray();
            DragAndDrop.paths = paths.ToArray();
            DragAndDrop.StartDrag(objects.Count == 1 ? title : objects.Count + " items");
        }

        /// <summary>
        /// Reads the drag and sets visualMode: Move if project assets can move into the folder, Copy if files from outside
        /// the project (Finder / Explorer) can be imported into it or Hierarchy objects saved as prefabs there, else
        /// Rejected. With perform, does it. Returns true if the drop is accepted.
        /// </summary>
        public static bool HandleDropOnFolder(string folderPath, bool perform)
        {
            var projectPaths = new List<string>();
            var outsidePaths = new List<string>();
            SplitPaths(DragAndDrop.paths, projectPaths, outsidePaths);
            var movable = MovablePaths(folderPath, projectPaths);
            bool writable = !string.IsNullOrEmpty(folderPath)
                            && AssetDatabase.IsValidFolder(folderPath) && AssetsContextMenu.IsWritableFolder(folderPath);
            bool import = outsidePaths.Count > 0 && writable;
            var sceneObjects = writable ? SceneGameObjects(DragAndDrop.objectReferences) : new List<GameObject>();
            if (movable.Count == 0 && !import && sceneObjects.Count == 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return false;
            }
            DragAndDrop.visualMode = movable.Count > 0 ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
            if (!perform) return true;
            if (sceneObjects.Count > 0)
            {
                // Hierarchy objects dropped on a folder become prefabs (only such objects are dragged from there).
                DragAndDrop.AcceptDrag();
                CreatePrefabs(folderPath, sceneObjects);
                return true;
            }

            DragAndDrop.AcceptDrag();
            if (movable.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var src in movable)
                    {
                        string error = AssetDatabase.MoveAsset(src, folderPath + "/" + Path.GetFileName(src));
                        if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[Better Project] Could not move '" + src + "': " + error);
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
            }
            if (import) ImportOutsideFiles(folderPath, outsidePaths);
            return true;
        }

        /// <summary>GameObjects from an open scene (or Prefab Mode) in the dragged objects; assets are not.</summary>
        static List<GameObject> SceneGameObjects(UnityEngine.Object[] objects)
        {
            var result = new List<GameObject>();
            if (objects == null) return result;
            foreach (var o in objects)
                if (o is GameObject go && !EditorUtility.IsPersistent(go) && !result.Contains(go)) result.Add(go);
            return result;
        }

        /// <summary>
        /// Saves each GameObject as a prefab in the folder (unique name from the object's name) and connects the scene
        /// object to it, like dropping into Unity's Project window. A prefab instance root becomes a Prefab Variant of
        /// its prefab. The new prefabs are selected.
        /// </summary>
        static void CreatePrefabs(string folderPath, List<GameObject> objects)
        {
            var created = new List<UnityEngine.Object>();
            foreach (var go in objects)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/" + FileNameFor(go.name) + ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction, out bool success);
                if (success && prefab != null) created.Add(prefab);
                else Debug.LogWarning("[Better Project] Could not create a prefab from '" + go.name + "'.");
            }
            if (created.Count > 0) Selection.objects = created.ToArray();
        }

        // GameObject names may hold characters a file name can't ('/', ':' …).
        static string FileNameFor(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name) sb.Append(c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return s.Length > 0 ? s : "New Prefab";
        }

        /// <summary>
        /// Project paths ("Assets/…", or absolute paths inside this project, made relative) vs. existing files and
        /// folders outside the project. ".meta" files are never imported on their own.
        /// </summary>
        static void SplitPaths(IReadOnlyList<string> paths, List<string> project, List<string> outside)
        {
            if (paths == null) return;
            string root = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var raw in paths)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                if (!Path.IsPathRooted(raw)) { project.Add(raw); continue; }
                string full = Path.GetFullPath(raw).Replace('\\', '/').TrimEnd('/');
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { project.Add(full.Substring(root.Length)); continue; }
                if (full.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full) || Directory.Exists(full)) outside.Add(full);
            }
        }

        // Copies files / folders from outside the project into the folder (unique names, like Unity's own drop),
        // imports them and selects what was added.
        static void ImportOutsideFiles(string folderPath, List<string> sources)
        {
            var added = new List<string>();
            foreach (var src in sources)
            {
                string dest = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/" + Path.GetFileName(src));
                try
                {
                    FileUtil.CopyFileOrDirectory(src, dest);
                    added.Add(dest);
                }
                catch (Exception e) { Debug.LogWarning("[Better Project] Could not copy '" + src + "': " + e.Message); }
            }
            if (added.Count == 0) return;
            AssetDatabase.Refresh();
            var objects = new List<UnityEngine.Object>();
            foreach (var p in added)
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(p);
                if (obj != null) objects.Add(obj);
            }
            if (objects.Count > 0) Selection.objects = objects.ToArray();
        }

        static bool ContainsIgnoreCase(List<string> list, string p)
        {
            foreach (var q in list) if (string.Equals(p, q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Paths that may be moved into the folder: not roots / read-only package content, not into itself.</summary>
        public static List<string> MovablePaths(string folderPath, IReadOnlyList<string> paths)
        {
            var result = new List<string>();
            if (paths == null || paths.Count == 0) return result;
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) return result;
            if (!AssetsContextMenu.IsWritableFolder(folderPath)) return result;

            var candidates = new List<string>();
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || ContainsIgnoreCase(candidates, p)) continue;
                if (!AssetsContextMenu.IsModifiable(p) || !AssetPaths.CanMoveInto(p, folderPath)) continue;
                candidates.Add(p);
            }
            // A path inside another dragged folder moves along with it.
            foreach (var p in candidates)
            {
                bool nested = false;
                foreach (var q in candidates)
                    if (!string.Equals(p, q, StringComparison.OrdinalIgnoreCase) && p.StartsWith(q + "/", StringComparison.OrdinalIgnoreCase)) { nested = true; break; }
                if (!nested) result.Add(p);
            }
            return result;
        }
    }
}
