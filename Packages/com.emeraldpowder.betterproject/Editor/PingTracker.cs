using System.Collections.Generic;
using BetterProject.Editor.Internal;
using UnityEditor;

namespace BetterProject.Editor
{
    /// <summary>
    /// Pings (EditorGUIUtility.PingObject: clicking an object field or the prefab asset field in the Inspector, ...)
    /// reach only Unity's Project windows. While a Better Project window is open, a hidden ProjectBrowser
    /// (UnityInternals.TryInstallPingListener) receives them too; each update it is asked for a new ping, and every open
    /// Better Project window shows the pinged asset like the default window does: opens its folder and flashes it.
    /// </summary>
    static class PingTracker
    {
        static readonly List<BetterProjectWindow> s_Windows = new List<BetterProjectWindow>();
        static UnityInternals.PingListener s_Listener;
        static bool s_Failed; // install failed (warning logged): don't retry for every window

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            // The native listener would survive the reload; the windows register again in CreateGUI.
            AssemblyReloadEvents.beforeAssemblyReload -= Uninstall;
            AssemblyReloadEvents.beforeAssemblyReload += Uninstall;
        }

        public static void Register(BetterProjectWindow window)
        {
            if (!s_Windows.Contains(window)) s_Windows.Add(window);
        }

        /// <summary>
        /// Installs the listener if a window is registered and it isn't installed yet. Call from OnGUI only: creating a
        /// ProjectBrowser needs the editor GUI skin, which CreateGUI (e.g. right after a domain reload) doesn't have.
        /// </summary>
        public static void EnsureInstalledFromOnGUI()
        {
            if (s_Listener != null || s_Failed || s_Windows.Count == 0) return;
            s_Listener = UnityInternals.TryInstallPingListener();
            if (s_Listener == null) { s_Failed = true; return; }
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        public static void Unregister(BetterProjectWindow window)
        {
            s_Windows.Remove(window);
            if (s_Windows.Count == 0) Uninstall();
        }

        static void Uninstall()
        {
            EditorApplication.update -= OnUpdate;
            var listener = s_Listener;
            s_Listener = null;
            UnityInternals.TryUninstallPingListener(listener);
        }

        static void OnUpdate()
        {
            if (!UnityInternals.TryTakePing(s_Listener, out int id)) return;
            string path = AssetDatabase.GetAssetPath(id); // sub-assets: the main asset's path; scene objects: none
            if (string.IsNullOrEmpty(path)) return;
            s_Windows.RemoveAll(w => w == null);
            foreach (var w in s_Windows.ToArray()) w.PingAsset(path);
        }
    }
}
