using System.Collections.Generic;
using BetterProject.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace BetterProject.Editor
{
    /// <summary>
    /// Feeds <see cref="RecentsStore"/>: prefabs opened in Prefab Mode, scenes opened, and assets the user edits in the
    /// Inspector. Edits are detected through Undo (only user-driven, undoable changes reach it) and only count while the
    /// Inspector is the focused window, so tools, scripts, imports and Prefab Mode auto-save never add entries.
    /// Edits to scene objects never count (not persistent), so an edited scene is not added.
    /// Optionally also imported assets (<see cref="RecentsImportTracker"/>). Each trigger has a switch in settings.
    /// </summary>
    [InitializeOnLoad]
    static class RecentsTracker
    {
        static readonly List<string> s_Paths = new List<string>(4);

        static RecentsTracker()
        {
            PrefabStage.prefabStageOpened += stage =>
            {
                if (BetterProjectSettings.instance.RecentsOnPrefabOpened) RecentsStore.Push(stage.assetPath);
            };
            EditorSceneManager.sceneOpened += OnSceneOpened;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (BetterProjectSettings.instance.RecentsOnSceneOpened && !string.IsNullOrEmpty(scene.path))
                RecentsStore.Push(scene.path);
        }

        // Runs on every property change: no asset loads, one store call (and so at most one save) per callback.
        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!BetterProjectSettings.instance.RecentsOnAssetChanged) return modifications;
            var focused = EditorWindow.focusedWindow;
            string focusedType = focused != null ? focused.GetType().FullName : null;
            if (!RecentsFilter.IsEditWindow(focusedType)) return modifications; // cheap early out

            s_Paths.Clear();
            foreach (var m in modifications)
            {
                var target = m.currentValue?.target;
                if (target == null || !EditorUtility.IsPersistent(target)) continue;
                string path = AssetDatabase.GetAssetPath(target); // sub-assets resolve to the main asset's path
                if (s_Paths.Contains(path) || !RecentsFilter.ShouldRecordEdit(focusedType, path)) continue;
                s_Paths.Add(path);
            }
            if (s_Paths.Count > 0) RecentsStore.PushRange(s_Paths);
            return modifications;
        }
    }

    /// <summary>"Add to Recents when: asset imported": every imported or reimported asset under Assets/.</summary>
    class RecentsImportTracker : AssetPostprocessor
    {
        static readonly List<string> s_Paths = new List<string>();

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Length == 0 || !BetterProjectSettings.instance.RecentsOnAssetImported) return;
            s_Paths.Clear();
            var moved = new HashSet<string>(movedAssets); // a move is not an import
            foreach (var path in importedAssets)
            {
                if (moved.Contains(path) || !path.StartsWith("Assets/", System.StringComparison.Ordinal)) continue;
                if (AssetDatabase.IsValidFolder(path) || s_Paths.Contains(path)) continue;
                s_Paths.Add(path);
            }
            // The store keeps the newest 10; the last path ends on top.
            if (s_Paths.Count > 0) RecentsStore.PushRange(s_Paths);
        }
    }
}
