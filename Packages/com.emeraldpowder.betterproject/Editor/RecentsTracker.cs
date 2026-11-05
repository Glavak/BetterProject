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
            // Edits made by Undo.RecordObject + direct field writes (custom inspectors, Odin, ...) never reach
            // postprocessModifications: Unity reports them only as object change events.
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (BetterProjectSettings.instance.RecentsOnSceneOpened && !string.IsNullOrEmpty(scene.path))
                RecentsStore.Push(scene.path);
        }

        // Runs on every property change: no asset loads, one store call (and so at most one save) per callback.
        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!BetterProjectSettings.instance.RecentsOnAssetChanged || !InEditWindow(out var focused, out var over))
                return modifications; // cheap early out

            s_Paths.Clear();
            foreach (var m in modifications)
            {
                var target = m.currentValue?.target;
                if (target == null || !EditorUtility.IsPersistent(target)) continue;
                AddPath(AssetDatabase.GetAssetPath(target), focused, over); // sub-assets resolve to the main asset's path
            }
            if (s_Paths.Count > 0) RecentsStore.PushRange(s_Paths);
            return modifications;
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!BetterProjectSettings.instance.RecentsOnAssetChanged || !InEditWindow(out var focused, out var over)) return;

            s_Paths.Clear();
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var e);
                AddPath(AssetDatabase.GUIDToAssetPath(e.guid), focused, over);
            }
            if (s_Paths.Count > 0) RecentsStore.PushRange(s_Paths); // already on top (seen by Undo first): no save
        }

        static bool InEditWindow(out string focusedType, out string mouseOverType)
        {
            var focused = EditorWindow.focusedWindow;
            var over = EditorWindow.mouseOverWindow;
            focusedType = focused != null ? focused.GetType().FullName : null;
            mouseOverType = over != null ? over.GetType().FullName : null;
            return RecentsFilter.IsEditContext(focusedType, mouseOverType);
        }

        static void AddPath(string path, string focusedType, string mouseOverType)
        {
            if (string.IsNullOrEmpty(path) || s_Paths.Contains(path)) return;
            if (RecentsFilter.ShouldRecordEdit(focusedType, mouseOverType, path)) s_Paths.Add(path);
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
