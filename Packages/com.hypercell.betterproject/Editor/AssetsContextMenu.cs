// Create-in-this-folder: chosen approach and why (Task 5 spike, Unity 6000.2).
//
// Every Assets/Create/... item ends in ProjectWindowUtil.StartNameEditingIfProjectWindowExists, which consults
// ProjectBrowser.s_LastInteractedProjectBrowser: if one exists it starts "name editing" in THAT window's current
// folder; if none exists it creates the asset immediately as Assets/<default name> with no rename at all.
//
// Chosen (brief's approach 1, via a proxy): a hidden, never-shown ProjectBrowser (Init()ed, browsing our current folder)
// is installed as s_LastInteractedProjectBrowser (UnityInternals.TryInstallCreateProxy). A Create command then hands its
// pending creation to the proxy (CreateAssetUtility state: EndNameEditAction, instance id, default name, icon, resource
// file). We take that state out (UnityInternals.TryTakePendingCreate) and run the name edit in our own InlineRename on
// a placeholder row. On commit we run the EndNameEditAction ourselves with <our folder>/<typed name><ext> — exactly
// what Unity does after its own rename — so the asset is created once, in the right folder with the right name (scripts
// get the typed class name). The user's default Project window is never touched (not navigated, no rename there).
// The proxy also catches the native "Assets/Rename" item (ProjectBrowser.RenameSelectedAssets).
//
// Install / uninstall (deterministic, no timers):
// - Installed when the context menu is opened (one proxy reused while installed; re-pointed when the window
//   navigates), so a slow or deferred menu command still lands in the folder the user sees.
// - Polled on EditorApplication.update while installed. Uninstalled when: a create/rename was captured; a real Project
//   window became last-interacted (s_LastInteractedProjectBrowser != proxy — nothing is redirected after that); the
//   owning window closes; AssemblyReloadEvents.beforeAssemblyReload. On load, leftover proxies (the native object
//   survives a domain reload and re-registers itself) are found by name and destroyed (TryDestroyOrphanProxies).
// - Navigation mirror: while installed, Unity features that drive "the last interacted Project window" move the
//   proxy instead (Assets/Open on a folder → OpenSelectedFolders, FrameObjectInProjectWindow, PingObject → FrameObject
//   on every ProjectBrowser). Each update compares the proxy's browsed folder with handle.ShownFolder (which our own
//   TrySetProxyFolder sets before navigating, so there is no feedback loop) and, if Unity moved it, navigates Better
//   Project there and reveals the selection. A ping therefore navigates Better Project to the pinged asset's folder,
//   as the default window does. Select Dependencies (ShowObjectsInList) is read from the proxy's list area (TryGetProxyObjectList) and shown in
//   Better Project as a results list; the proxy is then reset to the folder.
// - Proxy list selection is synced from Unity's selection before the menu opens (the proxy gets no OnSelectionChange).
//
// Naming outcomes (Unity's RenameOverlay semantics): Enter commits (empty → default name); focus loss, navigating away
// and closing the window commit the typed name; Esc cancels → EndNameEditAction.Cancelled, nothing is created;
// an assembly reload cancels (no asset writes during reload).
//
// Assets menu Delete: the native item calls ProjectBrowser.DeleteSelectedAssets(askIfSure) → (proxy has no tree
// selection) Unity Selection → ProjectWindowUtil.DeleteAssets, which shows Unity's own confirmation. A right-click
// first prunes Unity's selection to the items visible here, so it deletes exactly what the user sees.
//
// Rejected:
// - Setting the active folder on the user's real ProjectBrowser: navigates the default window and starts the rename
//   there; does nothing when the default window is closed (immediate creation in Assets/).
// - Approach 2 (AssetPostprocessor: catch assets imported ~1 s after the menu, MoveAsset into our folder, rename):
//   with the default window open Unity does not create anything until the user names it in THAT window; with it
//   closed, scripts are generated with the default class name (NewMonoBehaviourScript) and renaming the file
//   afterwards leaves class and file name mismatched.
// - Re-implementing Create/...: loses third-party Create items.

using System;
using System.Collections.Generic;
using System.Text;
using BetterProject.Core;
using BetterProject.Editor.Internal;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace BetterProject.Editor
{
    public static class AssetsContextMenu
    {
        static UnityInternals.CreateProxy s_Proxy;
        static BetterProjectWindow s_Window;

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            UnityInternals.TryDestroyOrphanProxies(null);
            EditorApplication.delayCall += () => UnityInternals.TryDestroyOrphanProxies(s_Proxy);
        }

        static void OnBeforeAssemblyReload() => Disarm();

        /// <summary>True while a proxy is installed for <paramref name="window"/>.</summary>
        public static bool IsInstalledFor(BetterProjectWindow window) => s_Proxy != null && s_Window == window;

        public static void Show(BetterProjectWindow window, Vector2 mousePosition)
        {
            if (window == null) return;
            if (!string.IsNullOrEmpty(window.CurrentFolder)) EnsureInstalled(window); // no folder shown: nothing to redirect to
            if (s_Proxy != null) UnityInternals.TrySyncProxySelection(s_Proxy); // for Assets/Open on a folder
            EditorUtility.DisplayPopupMenu(new Rect(mousePosition, Vector2.zero), "Assets/", null);
            Poll(); // most platforms run the chosen command before DisplayPopupMenu returns
        }

        static void EnsureInstalled(BetterProjectWindow window)
        {
            if (s_Proxy != null && s_Window != window) Disarm();
            if (s_Proxy != null && UnityInternals.TryIsProxyCurrent(s_Proxy, out bool current) && !current) Disarm();
            if (s_Proxy == null)
            {
                s_Proxy = UnityInternals.TryInstallCreateProxy(window.CurrentFolder);
                if (s_Proxy == null) return; // degraded: Unity's default behaviour (warning already logged)
            }
            else if (s_Proxy.Folder != window.CurrentFolder) UnityInternals.TrySetProxyFolder(s_Proxy, window.CurrentFolder);
            s_Window = window;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        /// <summary>The window navigated: keep an installed proxy pointing at what the user sees.</summary>
        public static void OnWindowFolderChanged(BetterProjectWindow window)
        {
            if (IsInstalledFor(window) && !string.IsNullOrEmpty(window.CurrentFolder) && s_Proxy.Folder != window.CurrentFolder)
                UnityInternals.TrySetProxyFolder(s_Proxy, window.CurrentFolder);
        }

        /// <summary>Uninstalls the proxy (only if it belongs to <paramref name="onlyFor"/>, when given).</summary>
        public static void Disarm(BetterProjectWindow onlyFor = null) => Disarm(onlyFor, true);

        static void Disarm(BetterProjectWindow onlyFor, bool restoreExpanded)
        {
            if (onlyFor != null && s_Window != onlyFor) return;
            EditorApplication.update -= OnUpdate;
            var proxy = s_Proxy;
            s_Proxy = null;
            s_Window = null;
            if (proxy != null) UnityInternals.TryUninstallCreateProxy(proxy, restoreExpanded);
        }

        static void OnUpdate()
        {
            if (s_Proxy == null) { EditorApplication.update -= OnUpdate; return; }
            if (Poll()) return;
            if (s_Window == null) { Disarm(); return; }
            // A real Project window took over (or state unreadable): stop redirecting. Keep the expanded-folder state —
            // the user may have changed it in that window.
            if (!UnityInternals.TryIsProxyCurrent(s_Proxy, out bool current) || !current) { Disarm(null, false); return; }
            MirrorProxyNavigation();
        }

        static void MirrorProxyNavigation()
        {
            // Select Dependencies: show the listed objects as a results list, then put the proxy back on the folder.
            if (UnityInternals.TryGetProxyObjectList(s_Proxy, out int[] ids))
            {
                var w = s_Window;
                UnityInternals.TrySetProxyFolder(s_Proxy, w.CurrentFolder);
                w.ShowObjectList(ids);
                return;
            }
            if (!UnityInternals.TryGetProxyFolder(s_Proxy, out string folder) || folder == s_Proxy.ShownFolder) return;
            var window = s_Window;
            UnityInternals.AcceptProxyFolder(s_Proxy, folder);
            window.NavigateTo(folder);
            // NavigateTo refuses invalid folders: keep the proxy on what the window really shows.
            if (window.CurrentFolder != folder) UnityInternals.TrySetProxyFolder(s_Proxy, window.CurrentFolder);
            window.RevealSelection();
        }

        /// <summary>Checks the proxy for a captured create/rename; returns true (and uninstalls) if one was handled.</summary>
        static bool Poll()
        {
            if (s_Proxy == null) return false;
            if (UnityInternals.TryTakePendingCreate(s_Proxy, out var pending))
            {
                string folder = s_Proxy.Folder;
                var window = s_Window;
                Disarm();
                DeliverCreate(window, folder, pending);
                return true;
            }
            if (UnityInternals.TryTakePendingRename(s_Proxy, out int id))
            {
                var window = s_Window;
                Disarm();
                string path = AssetDatabase.GetAssetPath(id);
                if (window != null && !string.IsNullOrEmpty(path)) window.BeginRename(path);
                return true;
            }
            return false;
        }

        static void DeliverCreate(BetterProjectWindow window, string folder, UnityInternals.PendingCreate pending)
        {
            if (!AssetDatabase.IsValidFolder(folder) || !IsWritableFolder(folder))
            {
                Debug.LogWarning("[Better Project] Cannot create assets in '" + folder + "'.");
                UnityInternals.TryCancelCreate(pending);
                return;
            }
            // Make the proposed name unique in OUR folder (Unity computed it relative to its own idea of the folder).
            string ext = pending.Extension ?? "";
            string unique = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + pending.DefaultName + ext);
            if (!string.IsNullOrEmpty(unique))
                pending.DefaultName = System.IO.Path.GetFileNameWithoutExtension(unique);

            if (window != null && window.CurrentFolder == folder)
                window.BeginCreate(pending);
            else
                CompleteCreate(folder, pending, pending.DefaultName); // not reachable in practice (folder is kept in sync)
        }

        /// <summary>Runs the captured creation as <paramref name="folder"/>/<paramref name="name"/>; returns the created path or null.</summary>
        internal static string CompleteCreate(string folder, UnityInternals.PendingCreate pending, string name)
        {
            if (string.IsNullOrEmpty(name)) name = pending.DefaultName;
            string ext = pending.Extension ?? "";
            string path = folder + "/" + name;
            if (ext.Length > 0 && !path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
            string created = UnityInternals.TryCompleteCreate(pending, path);
            if (created == null)
            {
                Debug.LogWarning("[Better Project] Could not create '" + path + "'.");
                return null;
            }
            var obj = AssetDatabase.LoadMainAssetAtPath(created);
            if (obj != null) Selection.activeObject = obj;
            return created;
        }

        internal static void CancelCreate(UnityInternals.PendingCreate pending) => UnityInternals.TryCancelCreate(pending);

        // ---------------------------------------------------------------- rename / delete / duplicate helpers

        /// <summary>True if the asset can be renamed / deleted / duplicated (not a root, not inside an immutable package).</summary>
        public static bool IsModifiable(string path)
        {
            if (!AssetPaths.IsProjectAssetPath(path) || path == "Assets") return false;
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var pkg = PackageInfo.FindForAssetPath(path);
                if (pkg == null) return false;
                if (path == pkg.assetPath) return false; // package root
                if (pkg.source != PackageSource.Embedded && pkg.source != PackageSource.Local) return false;
            }
            return true;
        }

        public static bool IsWritableFolder(string folder)
        {
            if (folder == "Assets") return true;
            if (!AssetPaths.IsProjectAssetPath(folder)) return false;
            if (!folder.StartsWith("Packages/", StringComparison.Ordinal)) return true;
            var pkg = PackageInfo.FindForAssetPath(folder);
            return pkg != null && (pkg.source == PackageSource.Embedded || pkg.source == PackageSource.Local);
        }

        /// <summary>
        /// Asks (unless the "Delete without confirmation" setting is on), then moves the assets to the OS trash.
        /// Returns true if anything was deleted.
        /// </summary>
        public static bool DeleteWithConfirmation(IReadOnlyList<string> paths)
        {
            var targets = new List<string>();
            foreach (var p in paths) if (IsModifiable(p)) targets.Add(p);
            if (targets.Count == 0) return false;

            if (!BetterProjectSettings.instance.DeleteWithoutConfirmation && !Confirm(targets)) return false;

            var failed = new List<string>();
            if (!AssetDatabase.MoveAssetsToTrash(targets.ToArray(), failed))
                Debug.LogWarning("[Better Project] Could not delete: " + string.Join(", ", failed));
            return failed.Count < targets.Count;
        }

        static bool Confirm(List<string> targets)
        {
            const int maxListed = 20;
            var sb = new StringBuilder();
            for (int i = 0; i < targets.Count && i < maxListed; i++) sb.AppendLine(targets[i]);
            if (targets.Count > maxListed) sb.AppendLine("... and " + (targets.Count - maxListed) + " more");
            sb.AppendLine();
            sb.Append("The assets will be moved to the trash.");
            string title = targets.Count == 1 ? "Delete selected asset?" : "Delete " + targets.Count + " selected assets?";
            return EditorUtility.DisplayDialog(title, sb.ToString(), "Delete", "Cancel");
        }

        /// <summary>Copies each asset next to itself under a unique name; returns the new paths.</summary>
        public static List<string> Duplicate(IReadOnlyList<string> paths)
        {
            var created = new List<string>();
            foreach (var p in paths)
            {
                if (!IsModifiable(p)) continue;
                string dest = AssetDatabase.GenerateUniqueAssetPath(p);
                if (string.IsNullOrEmpty(dest)) continue;
                if (AssetDatabase.CopyAsset(p, dest)) created.Add(dest);
                else Debug.LogWarning("[Better Project] Could not duplicate '" + p + "'.");
            }
            return created;
        }
    }
}
