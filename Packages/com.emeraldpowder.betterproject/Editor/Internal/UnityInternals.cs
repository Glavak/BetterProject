using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterProject.Editor.Internal
{
    /// <summary>
    /// The only place that reflects into UnityEditor internals. Every member returns a failure value
    /// (false / null) instead of throwing, and logs at most one warning per member per session.
    /// Reference: UnityCsReference 6000.2 — ProjectBrowser.cs, ProjectWindowUtil.cs, CreateAssetUtility.cs,
    /// ObjectListArea.cs (ObjectListAreaState), RenameOverlay.cs.
    /// </summary>
    internal static class UnityInternals
    {
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly HashSet<string> s_Warned = new HashSet<string>();

        static void Warn(string member, Exception e = null)
        {
            if (!s_Warned.Add(member)) return;
            if (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            Debug.LogWarning("[Better Project] Unity internal '" + member + "' is unavailable; the feature that uses it is degraded."
                             + (e != null ? " (" + e.GetType().Name + ": " + e.Message + ")\n" + e.StackTrace : ""));
        }

        // ---------------------------------------------------------------- reflection handles

        static bool s_Resolved;
        static Type s_PbType;                     // UnityEditor.ProjectBrowser (internal)
        static FieldInfo s_LastInteracted;        // ProjectBrowser.s_LastInteractedProjectBrowser (static)
        static MethodInfo s_PbInit;               // ProjectBrowser.Init()
        static MethodInfo s_PbShowFolderContents; // ProjectBrowser.ShowFolderContents(int, bool)
        static FieldInfo s_PbListAreaState;       // ProjectBrowser.m_ListAreaState
        static FieldInfo s_StateCreateUtil;       // ObjectListAreaState.m_CreateAssetUtility
        static FieldInfo s_StateRenameOverlay;    // ObjectListAreaState.m_RenameOverlay
        static FieldInfo s_StateNewAssetIndex;    // ObjectListAreaState.m_NewAssetIndexInList
        static MethodInfo s_CauIsCreating;        // CreateAssetUtility.IsCreatingNewAsset()
        static MethodInfo s_CauClear;             // CreateAssetUtility.Clear()
        static FieldInfo s_CauEndAction, s_CauInstanceId, s_CauPath, s_CauIcon, s_CauResourceFile;
        static MethodInfo s_OverlayClear;         // RenameOverlay.Clear()
        static MethodInfo s_OverlayIsRenaming;    // RenameOverlay.IsRenaming()
        static PropertyInfo s_OverlayUserData;    // RenameOverlay.userData (instance id)

        static bool Resolve()
        {
            if (s_Resolved) return s_PbType != null;
            s_Resolved = true;
            const string member = "ProjectBrowser create/rename members";
            try
            {
                var asm = typeof(EditorWindow).Assembly;
                var pb = asm.GetType("UnityEditor.ProjectBrowser");
                var stateType = asm.GetType("UnityEditor.ObjectListAreaState");
                var cauType = asm.GetType("UnityEditor.CreateAssetUtility");
                if (pb == null || stateType == null || cauType == null) { Warn(member); return false; }

                s_LastInteracted = pb.GetField("s_LastInteractedProjectBrowser", AnyStatic);
                s_PbInit = pb.GetMethod("Init", AnyInstance, null, Type.EmptyTypes, null);
                s_PbShowFolderContents = pb.GetMethod("ShowFolderContents", AnyInstance, null, new[] { typeof(int), typeof(bool) }, null);
                s_PbListAreaState = pb.GetField("m_ListAreaState", AnyInstance);
                s_StateCreateUtil = stateType.GetField("m_CreateAssetUtility", AnyInstance);
                s_StateRenameOverlay = stateType.GetField("m_RenameOverlay", AnyInstance);
                s_StateNewAssetIndex = stateType.GetField("m_NewAssetIndexInList", AnyInstance);
                s_CauIsCreating = cauType.GetMethod("IsCreatingNewAsset", AnyInstance, null, Type.EmptyTypes, null);
                s_CauClear = cauType.GetMethod("Clear", AnyInstance, null, Type.EmptyTypes, null);
                s_CauEndAction = cauType.GetField("m_EndAction", AnyInstance);
                s_CauInstanceId = cauType.GetField("m_InstanceID", AnyInstance);
                s_CauPath = cauType.GetField("m_Path", AnyInstance);
                s_CauIcon = cauType.GetField("m_Icon", AnyInstance);
                s_CauResourceFile = cauType.GetField("m_ResourceFile", AnyInstance);
                var overlayType = s_StateRenameOverlay?.FieldType;
                s_OverlayClear = overlayType?.GetMethod("Clear", AnyInstance, null, Type.EmptyTypes, null);
                s_OverlayIsRenaming = overlayType?.GetMethod("IsRenaming", AnyInstance, null, Type.EmptyTypes, null);
                s_OverlayUserData = overlayType?.GetProperty("userData", AnyInstance);

                if (s_LastInteracted == null || s_LastInteracted.FieldType != pb || s_PbInit == null || s_PbShowFolderContents == null
                    || s_PbListAreaState == null || s_StateCreateUtil == null || s_StateRenameOverlay == null
                    || s_StateNewAssetIndex == null || s_StateNewAssetIndex.FieldType != typeof(int)
                    || s_CauIsCreating == null || s_CauIsCreating.ReturnType != typeof(bool) || s_CauClear == null
                    || s_CauEndAction == null || s_CauInstanceId == null || s_CauInstanceId.FieldType != typeof(int)
                    || s_CauPath == null || s_CauPath.FieldType != typeof(string) || s_CauIcon == null
                    || s_CauResourceFile == null || s_CauResourceFile.FieldType != typeof(string)
                    || s_OverlayClear == null || s_OverlayIsRenaming == null || s_OverlayIsRenaming.ReturnType != typeof(bool)
                    || s_OverlayUserData == null || s_OverlayUserData.PropertyType != typeof(int))
                {
                    Warn(member);
                    return false;
                }
                s_PbType = pb;
                return true;
            }
            catch (Exception e)
            {
                Warn(member, e);
                return false;
            }
        }

        // ---------------------------------------------------------------- create-in-folder proxy

        /// <summary>
        /// A hidden, never-shown ProjectBrowser installed as the "last interacted project browser" while a menu command
        /// runs. ProjectWindowUtil.StartNameEditingIfProjectWindowExists (used by every Assets/Create item) then hands the
        /// pending creation to it — instead of to the user's Project window (wrong folder, rename there) or, with no
        /// Project window open, creating immediately in Assets/ without rename.
        /// </summary>
        /// <summary>Object name that marks our hidden proxies (used to find leftovers after a domain reload).</summary>
        const string ProxyName = "BetterProject.CreateProxy";
        /// <summary>Object name of the hidden ping listener (see <see cref="TryInstallPingListener"/>).</summary>
        const string PingListenerName = "BetterProject.PingListener";

        // Hidden browsers created in this domain. Any other one carrying our names is a leftover of a domain reload.
        static readonly HashSet<Object> s_Live = new HashSet<Object>();

        static bool IsOurs(Object browser) => browser.name == ProxyName || browser.name == PingListenerName;

        public sealed class CreateProxy
        {
            internal EditorWindow Proxy;
            internal Object Previous; // the user's last-interacted ProjectBrowser (may be null)
            internal int[] SavedExpanded;   // shared expanded-folders state as last accepted (see RestoreExpanded)
            /// <summary>Folder the proxy was last pointed at / seen browsing; a different active folder means Unity moved it.</summary>
            public string ShownFolder { get; internal set; }
            /// <summary>The folder the user saw when the menu was opened (where the asset must land).</summary>
            public string Folder { get; internal set; }
        }

        /// <summary>A creation captured from the proxy, not yet performed.</summary>
        public struct PendingCreate
        {
            public EndNameEditAction EndAction;
            public int InstanceId;
            public string DefaultName;   // file name without extension, as Unity proposed it
            public string Extension;     // including the dot; empty for folders
            public Texture2D Icon;
            public string ResourceFile;
        }

        /// <summary>
        /// Creates a hidden ProjectBrowser browsing <paramref name="folder"/> and makes it the last-interacted one.
        /// Returns null on failure (Unity's default behaviour then applies). Uninstall with <see cref="TryUninstallCreateProxy"/>.
        /// </summary>
        public static CreateProxy TryInstallCreateProxy(string folder)
        {
            if (!Resolve()) return null;
            EditorWindow proxy = null;
            var handle = new CreateProxy { Folder = folder };
            try
            {
                handle.Previous = s_LastInteracted.GetValue(null) as Object;
                handle.SavedExpanded = InternalEditorUtility.expandedProjectWindowItems;
                proxy = (EditorWindow)ScriptableObject.CreateInstance(s_PbType); // its OnEnable makes it last-interacted
                // HideAndDontSave: never saved with the layout and not unloaded by UnloadUnusedAssets while installed.
                // It does survive domain reloads, hence the ProxyName marker and TryDestroyOrphanProxies.
                proxy.hideFlags = HideFlags.HideAndDontSave;
                proxy.name = ProxyName;
                s_Live.Add(proxy);
                handle.Proxy = proxy;
                s_PbInit.Invoke(proxy, null);
                // A folder that can't hold assets (the virtual "Packages" root) still needs a valid folder in the proxy;
                // the caller refuses creations whose Folder is not a valid folder.
                string shown = AssetDatabase.IsValidFolder(folder) ? folder : "Assets";
                var folderObj = AssetDatabase.LoadMainAssetAtPath(shown);
                if (folderObj == null) throw new InvalidOperationException("folder not loaded: " + shown);
                handle.ShownFolder = shown;
                s_PbShowFolderContents.Invoke(proxy, new object[] { folderObj.GetInstanceID(), false });
                s_LastInteracted.SetValue(null, proxy);
                // Init frames the selection in the proxy's tree, which writes the shared expanded state: undo that now.
                RestoreExpanded(handle);
                return handle;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser proxy", e);
                TryUninstallCreateProxy(handle);
                return null;
            }
        }

        /// <summary>Points an installed proxy at another folder (the window navigated).</summary>
        public static bool TrySetProxyFolder(CreateProxy handle, string folder)
        {
            if (handle == null || handle.Proxy == null) return false;
            handle.Folder = folder;
            try
            {
                // Record what we show BEFORE showing it, so the navigation mirror never reacts to our own change.
                string shown = AssetDatabase.IsValidFolder(folder) ? folder : "Assets";
                handle.ShownFolder = shown;
                var folderObj = AssetDatabase.LoadMainAssetAtPath(shown);
                if (folderObj == null) return false;
                s_PbShowFolderContents.Invoke(handle.Proxy, new object[] { folderObj.GetInstanceID(), false });
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser.ShowFolderContents", e);
                return false;
            }
        }

        // ---- navigation mirror: Unity features that drive "the last interacted Project window" (Assets/Open on a
        // folder → OpenSelectedFolders, FrameObjectInProjectWindow, EditorGUIUtility.PingObject → FrameObject on every
        // ProjectBrowser) move the proxy's folder; the caller mirrors that into Better Project.
        // Select Dependencies (ShowSelectedObjectsInLastInteractedProjectBrowser → ShowObjectsInList) is read by
        // TryGetProxyObjectList below.

        static bool s_MirrorResolved;
        static FieldInfo s_PbSearchFilter;      // ProjectBrowser.m_SearchFilter
        static MethodInfo s_SfGetState;         // SearchFilter.GetState()
        static PropertyInfo s_SfFolders;        // SearchFilter.folders
        static FieldInfo s_PbListArea;          // ProjectBrowser.m_ListArea
        static MethodInfo s_LaInitSelection;    // ObjectListArea.InitSelection(int[])

        static bool ResolveMirror()
        {
            if (s_MirrorResolved) return s_SfFolders != null;
            s_MirrorResolved = true;
            if (!Resolve()) return false;
            try
            {
                s_PbSearchFilter = s_PbType.GetField("m_SearchFilter", AnyInstance);
                var sfType = s_PbSearchFilter?.FieldType;
                s_SfGetState = sfType?.GetMethod("GetState", AnyInstance, null, Type.EmptyTypes, null);
                var folders = sfType?.GetProperty("folders", AnyInstance);
                s_PbListArea = s_PbType.GetField("m_ListArea", AnyInstance);
                s_LaInitSelection = s_PbListArea?.FieldType.GetMethod("InitSelection", AnyInstance, null, new[] { typeof(int[]) }, null);
                if (s_SfGetState == null || !s_SfGetState.ReturnType.IsEnum || folders == null || folders.PropertyType != typeof(string[])
                    || s_LaInitSelection == null)
                {
                    Warn("ProjectBrowser navigation members");
                    return false;
                }
                s_SfFolders = folders;
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser navigation members", e);
                return false;
            }
        }

        static bool s_ObjListResolved;
        static FieldInfo s_LaLocalAssets;       // ObjectListArea.m_LocalAssets
        static FieldInfo s_LgHierarchy;         // ObjectListArea.LocalGroup.m_FilteredHierarchy
        static FieldInfo s_FhFilter;            // FilteredHierarchy.m_SearchFilter
        static FieldInfo s_FhResults;           // FilteredHierarchy.m_Results (FilterResult[])
        static FieldInfo s_FrInstanceId;        // FilteredHierarchy.FilterResult.instanceID

        static bool ResolveObjectList()
        {
            if (s_ObjListResolved) return s_FrInstanceId != null;
            s_ObjListResolved = true;
            if (!ResolveMirror()) return false;
            try
            {
                var laType = s_PbListArea.FieldType;
                s_LaLocalAssets = laType.GetField("m_LocalAssets", AnyInstance);
                s_LgHierarchy = s_LaLocalAssets?.FieldType.GetField("m_FilteredHierarchy", AnyInstance);
                var fhType = s_LgHierarchy?.FieldType;
                s_FhFilter = fhType?.GetField("m_SearchFilter", AnyInstance);
                s_FhResults = fhType?.GetField("m_Results", AnyInstance);
                var frType = s_FhResults?.FieldType.GetElementType();
                var id = frType?.GetField("instanceID", AnyInstance);
                if (s_LgHierarchy == null || s_FhFilter == null || s_FhFilter.FieldType != s_PbSearchFilter.FieldType
                    || frType == null || id == null || id.FieldType != typeof(int))
                {
                    Warn("ProjectBrowser object list members");
                    return false;
                }
                s_FrInstanceId = id;
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser object list members", e);
                return false;
            }
        }

        /// <summary>
        /// True if the proxy was put into "object list" mode (ProjectBrowser.ShowObjectsInList: Assets → Select Dependencies):
        /// its list area then shows explicit results from a filter with no folders, while a folder-browsing proxy always
        /// has the folder in its list filter. <paramref name="ids"/> are the listed instance ids. Failure-safe (false).
        /// Reset the proxy afterwards with <see cref="TrySetProxyFolder"/>.
        /// </summary>
        public static bool TryGetProxyObjectList(CreateProxy handle, out int[] ids)
        {
            ids = null;
            if (handle == null || handle.Proxy == null || !ResolveObjectList()) return false;
            try
            {
                var listArea = s_PbListArea.GetValue(handle.Proxy);
                var local = listArea == null ? null : s_LaLocalAssets.GetValue(listArea);
                var hierarchy = local == null ? null : s_LgHierarchy.GetValue(local);
                var filter = hierarchy == null ? null : s_FhFilter.GetValue(hierarchy);
                if (filter == null) return false;
                var folders = (string[])s_SfFolders.GetValue(filter);
                if (folders != null && folders.Length > 0) return false; // folder browsing
                var results = s_FhResults.GetValue(hierarchy) as Array;
                if (results == null || results.Length == 0) return false;
                var list = new List<int>(results.Length);
                foreach (var r in results) list.Add((int)s_FrInstanceId.GetValue(r));
                ids = list.ToArray();
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser object list state", e);
                return false;
            }
        }

        /// <summary>
        /// The folder Unity's own Project window is browsing (the last interacted one, else any open one; never our
        /// hidden proxy), if it is folder-browsing. Failure-safe (false).
        /// </summary>
        public static bool TryGetProjectWindowFolder(out string folder)
        {
            folder = null;
            if (!Resolve() || !ResolveMirror()) return false;
            try
            {
                var candidates = new List<Object>();
                if (s_LastInteracted.GetValue(null) is Object last && last != null) candidates.Add(last);
                foreach (var o in Resources.FindObjectsOfTypeAll(s_PbType)) if (o != null && !candidates.Contains(o)) candidates.Add(o);
                foreach (var browser in candidates)
                {
                    if (IsOurs(browser)) continue;
                    var filter = s_PbSearchFilter.GetValue(browser);
                    if (filter == null || s_SfGetState.Invoke(filter, null).ToString() != "FolderBrowsing") continue;
                    var folders = (string[])s_SfFolders.GetValue(filter);
                    if (folders == null || folders.Length == 0 || string.IsNullOrEmpty(folders[0])) continue;
                    folder = folders[0];
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Warn("Project window folder", e);
                return false;
            }
        }

        /// <summary>The folder the proxy is browsing, if it is folder-browsing (not searching).</summary>
        public static bool TryGetProxyFolder(CreateProxy handle, out string folder)
        {
            folder = null;
            if (handle == null || handle.Proxy == null || !ResolveMirror()) return false;
            try
            {
                var filter = s_PbSearchFilter.GetValue(handle.Proxy);
                if (filter == null) return false;
                if (s_SfGetState.Invoke(filter, null).ToString() != "FolderBrowsing") return false;
                var folders = (string[])s_SfFolders.GetValue(filter);
                if (folders == null || folders.Length == 0 || string.IsNullOrEmpty(folders[0])) return false;
                folder = folders[0];
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser navigation state", e);
                return false;
            }
        }

        /// <summary>
        /// Accepts a folder change Unity made in the proxy: records it as shown/target folder and accepts the current
        /// expanded-folders state (Unity revealed it, e.g. a ping, in real Project windows too).
        /// </summary>
        public static void AcceptProxyFolder(CreateProxy handle, string folder)
        {
            if (handle == null) return;
            handle.ShownFolder = folder;
            handle.Folder = folder;
            try { handle.SavedExpanded = InternalEditorUtility.expandedProjectWindowItems; }
            catch (Exception e) { Warn("InternalEditorUtility.expandedProjectWindowItems", e); }
        }

        /// <summary>
        /// Copies Unity's selection into the proxy's list selection. The proxy gets no OnSelectionChange (never shown),
        /// and Assets/Open on a folder (OpenSelectedFolders) reads that list selection.
        /// </summary>
        public static bool TrySyncProxySelection(CreateProxy handle)
        {
            if (handle == null || handle.Proxy == null || !ResolveMirror()) return false;
            try
            {
                var listArea = s_PbListArea.GetValue(handle.Proxy);
                if (listArea == null) return false;
                s_LaInitSelection.Invoke(listArea, new object[] { Selection.instanceIDs });
                return true;
            }
            catch (Exception e)
            {
                Warn("ObjectListArea.InitSelection", e);
                return false;
            }
        }

        static void RestoreExpanded(CreateProxy handle)
        {
            try
            {
                if (handle.SavedExpanded != null) InternalEditorUtility.expandedProjectWindowItems = handle.SavedExpanded;
            }
            catch (Exception e) { Warn("InternalEditorUtility.expandedProjectWindowItems", e); }
        }

        /// <summary>
        /// current = the proxy is still ProjectWindowUtil's last-interacted browser (false once a real Project window
        /// was interacted with, or the proxy is gone). Returns false if the state can't be read.
        /// </summary>
        public static bool TryIsProxyCurrent(CreateProxy handle, out bool current)
        {
            current = false;
            if (handle == null || handle.Proxy == null) return true;
            try
            {
                current = ReferenceEquals(s_LastInteracted.GetValue(null), handle.Proxy);
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser.s_LastInteractedProjectBrowser", e);
                return false;
            }
        }

        /// <summary>
        /// Destroys hidden proxies / ping listeners that outlived their handle (e.g. across a domain reload, where the
        /// native object survives and its OnEnable re-registers it as last-interacted). Hands "last interacted" to a
        /// real browser. The ones created in this domain are kept.
        /// </summary>
        public static bool TryDestroyOrphanProxies()
        {
            if (!Resolve()) return false;
            try
            {
                var all = Resources.FindObjectsOfTypeAll(s_PbType);
                Object realBrowser = null;
                foreach (var o in all)
                    if (o != null && !IsOurs(o)) { realBrowser = o; break; }
                foreach (var o in all)
                {
                    if (o == null || !IsOurs(o) || s_Live.Contains(o)) continue;
                    if (ReferenceEquals(s_LastInteracted.GetValue(null), o)) s_LastInteracted.SetValue(null, realBrowser);
                    Object.DestroyImmediate(o);
                }
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser orphan proxy cleanup", e);
                return false;
            }
        }

        /// <summary>If the proxy received a name-editing request, takes it (clearing the proxy's state) and returns true.</summary>
        public static bool TryTakePendingCreate(CreateProxy handle, out PendingCreate pending)
        {
            pending = default;
            if (handle == null || handle.Proxy == null) return false;
            try
            {
                var state = s_PbListAreaState.GetValue(handle.Proxy);
                if (state == null) return false;
                var cau = s_StateCreateUtil.GetValue(state);
                if (cau == null || !(bool)s_CauIsCreating.Invoke(cau, null)) return false;

                string path = (string)s_CauPath.GetValue(cau) ?? "";
                var p = new PendingCreate
                {
                    EndAction = s_CauEndAction.GetValue(cau) as EndNameEditAction,
                    InstanceId = (int)s_CauInstanceId.GetValue(cau),
                    DefaultName = System.IO.Path.GetFileNameWithoutExtension(path),
                    Extension = System.IO.Path.GetExtension(path),
                    Icon = s_CauIcon.GetValue(cau) as Texture2D,
                    ResourceFile = (string)s_CauResourceFile.GetValue(cau),
                };
                // Make the proxy forget it, so destroying the proxy never commits or cancels it.
                s_CauClear.Invoke(cau, null);
                var overlay = s_StateRenameOverlay.GetValue(state);
                if (overlay != null) s_OverlayClear.Invoke(overlay, null);
                s_StateNewAssetIndex.SetValue(state, -1);
                if (string.IsNullOrEmpty(p.DefaultName)) return false;
                pending = p;
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser proxy create state", e);
                pending = default;
                return false;
            }
        }

        /// <summary>
        /// If a menu command asked the proxy to rename (native "Assets/Rename" → ProjectBrowser.RenameSelectedAssets),
        /// takes the instance id being renamed (clearing the proxy's rename state) and returns true.
        /// </summary>
        public static bool TryTakePendingRename(CreateProxy handle, out int instanceId)
        {
            instanceId = 0;
            if (handle == null || handle.Proxy == null) return false;
            try
            {
                var state = s_PbListAreaState.GetValue(handle.Proxy);
                var overlay = state != null ? s_StateRenameOverlay.GetValue(state) : null;
                if (overlay == null || !(bool)s_OverlayIsRenaming.Invoke(overlay, null)) return false;
                instanceId = (int)s_OverlayUserData.GetValue(overlay);
                s_OverlayClear.Invoke(overlay, null);
                return instanceId != 0;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser proxy rename state", e);
                instanceId = 0;
                return false;
            }
        }

        /// <summary>
        /// Restores the previous last-interacted ProjectBrowser (unless another one took over) and destroys the proxy.
        /// <paramref name="restoreExpanded"/>: undo expanded-folder changes the proxy made since the last accepted state;
        /// pass false when a real Project window took over (the user may have expanded folders there since).
        /// </summary>
        public static bool TryUninstallCreateProxy(CreateProxy handle, bool restoreExpanded = true)
        {
            if (handle == null || handle.Proxy == null) return false;
            bool ok = true;
            var proxy = handle.Proxy;
            handle.Proxy = null;
            try
            {
                var current = s_LastInteracted.GetValue(null) as Object;
                if (ReferenceEquals(current, proxy) || current == null)
                    s_LastInteracted.SetValue(null, handle.Previous != null ? handle.Previous : null);
            }
            catch (Exception e) { Warn("ProjectBrowser.s_LastInteractedProjectBrowser", e); ok = false; }
            try
            {
                s_Live.Remove(proxy);
                if (proxy != null) Object.DestroyImmediate(proxy);
            }
            catch (Exception e) { Warn("ProjectBrowser proxy destroy", e); ok = false; }
            // The proxy reveals folders in its tree (e.g. when a create starts), which writes the shared expanded state.
            if (restoreExpanded) RestoreExpanded(handle);
            return ok;
        }

        /// <summary>
        /// Performs a captured creation at <paramref name="pathName"/> (made unique), like ProjectWindowUtil.EndNameEditAction,
        /// while the user's Project window is hidden from ProjectWindowUtil so the action's "frame created asset" calls
        /// don't navigate it away. Returns the created path, or null if the action failed.
        /// </summary>
        public static string TryCompleteCreate(PendingCreate p, string pathName)
        {
            if (p.EndAction == null) return null;
            object previous = null;
            bool swapped = false;
            if (Resolve())
            {
                try
                {
                    previous = s_LastInteracted.GetValue(null);
                    s_LastInteracted.SetValue(null, null);
                    swapped = true;
                }
                catch (Exception e) { Warn("ProjectBrowser.s_LastInteractedProjectBrowser", e); }
            }
            try
            {
                string unique = AssetDatabase.GenerateUniqueAssetPath(pathName);
                if (string.IsNullOrEmpty(unique)) return null;
                p.EndAction.Action(p.InstanceId, unique, p.ResourceFile);
                return unique;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
            finally
            {
                try { if (p.EndAction != null) p.EndAction.CleanUp(); }
                catch (Exception e) { Debug.LogException(e); }
                if (swapped)
                {
                    try
                    {
                        if (s_LastInteracted.GetValue(null) == null && previous is Object o && o != null)
                            s_LastInteracted.SetValue(null, previous);
                    }
                    catch (Exception e) { Warn("ProjectBrowser.s_LastInteractedProjectBrowser", e); }
                }
            }
        }

        /// <summary>Abandons a captured creation (EndNameEditAction.Cancelled + CleanUp).</summary>
        public static bool TryCancelCreate(PendingCreate p)
        {
            if (p.EndAction == null) return false;
            try
            {
                p.EndAction.Cancelled(p.InstanceId, "", p.ResourceFile);
                p.EndAction.CleanUp();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        // ---------------------------------------------------------------- ping listener

        // EditorGUIUtility.PingObject (clicking an object field or the prefab asset field in the Inspector, ...) calls
        // FrameObject(id, ping) on every ProjectBrowser in ProjectBrowser.s_ProjectBrowsers, and FrameObject records the
        // id in m_LastFramedID. A hidden, never-shown ProjectBrowser is in that list too: reading (and resetting) its
        // m_LastFramedID tells which object was pinged. Never shown, it gets no OnSelectionChange / OnGUI, the only
        // other paths that frame something.

        public sealed class PingListener
        {
            internal EditorWindow Browser;
        }

        static bool s_PingResolved;
        static FieldInfo s_PbLastFramedId;      // ProjectBrowser.m_LastFramedID

        static bool ResolvePing()
        {
            if (s_PingResolved) return s_PbLastFramedId != null;
            s_PingResolved = true;
            if (!Resolve()) return false;
            try
            {
                var f = s_PbType.GetField("m_LastFramedID", AnyInstance);
                if (f == null || f.FieldType != typeof(int)) { Warn("ProjectBrowser.m_LastFramedID"); return false; }
                s_PbLastFramedId = f;
                return true;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser.m_LastFramedID", e);
                return false;
            }
        }

        /// <summary>
        /// Creates the hidden ProjectBrowser that receives pings. Unlike the create proxy it never stays the
        /// last-interacted browser. Returns null on failure. Uninstall with <see cref="TryUninstallPingListener"/>.
        /// </summary>
        public static PingListener TryInstallPingListener()
        {
            if (!ResolvePing()) return null;
            var handle = new PingListener();
            EditorWindow browser = null;
            object previous = null;
            int[] savedExpanded = null;
            try
            {
                previous = s_LastInteracted.GetValue(null);
                savedExpanded = InternalEditorUtility.expandedProjectWindowItems;
                browser = (EditorWindow)ScriptableObject.CreateInstance(s_PbType); // its OnEnable makes it last-interacted
                browser.hideFlags = HideFlags.HideAndDontSave;
                browser.name = PingListenerName;
                s_Live.Add(browser);
                handle.Browser = browser;
                s_PbInit.Invoke(browser, null);
                s_PbLastFramedId.SetValue(browser, -1); // Init framed the selection
                return handle;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser ping listener", e);
                TryUninstallPingListener(handle);
                return null;
            }
            finally
            {
                try
                {
                    // Hand "last interacted" back (a destroyed previous browser compares equal to null: store null).
                    // After a failure the listener is gone and the field was cleared.
                    var current = s_LastInteracted.GetValue(null) as Object;
                    if (browser != null && (ReferenceEquals(current, browser) || current == null))
                        s_LastInteracted.SetValue(null, previous is Object o && o != null ? previous : null);
                    // Init frames the selection in the folder tree, which writes the shared expanded state.
                    if (savedExpanded != null) InternalEditorUtility.expandedProjectWindowItems = savedExpanded;
                }
                catch (Exception e) { Warn("ProjectBrowser ping listener", e); }
            }
        }

        /// <summary>If an object was pinged since the last call, returns its instance id (main asset or sub-asset).</summary>
        public static bool TryTakePing(PingListener handle, out int instanceId)
        {
            instanceId = 0;
            if (handle == null || handle.Browser == null || s_PbLastFramedId == null) return false;
            try
            {
                int id = (int)s_PbLastFramedId.GetValue(handle.Browser);
                if (id == -1) return false;
                s_PbLastFramedId.SetValue(handle.Browser, -1);
                instanceId = id;
                return id != 0;
            }
            catch (Exception e)
            {
                Warn("ProjectBrowser.m_LastFramedID", e);
                return false;
            }
        }

        public static void TryUninstallPingListener(PingListener handle)
        {
            if (handle == null || handle.Browser == null) return;
            var browser = handle.Browser;
            handle.Browser = null;
            s_Live.Remove(browser);
            try
            {
                if (ReferenceEquals(s_LastInteracted.GetValue(null), browser)) s_LastInteracted.SetValue(null, null);
                Object.DestroyImmediate(browser);
            }
            catch (Exception e) { Warn("ProjectBrowser ping listener destroy", e); }
        }

        // ---------------------------------------------------------------- main asset id without loading

        static bool s_MainIdResolved;
        static Func<string, int> s_MainIdFunc;    // AssetDatabase.GetMainAssetInstanceID(string) (internal)

        /// <summary>Instance id of the main asset at <paramref name="path"/> without loading it; 0 if unavailable.</summary>
        public static int GetMainAssetInstanceID(string path)
        {
            if (!s_MainIdResolved)
            {
                s_MainIdResolved = true;
                try
                {
                    var m = typeof(AssetDatabase).GetMethod("GetMainAssetInstanceID", AnyStatic, null, new[] { typeof(string) }, null);
                    if (m != null) s_MainIdFunc = (Func<string, int>)Delegate.CreateDelegate(typeof(Func<string, int>), m);
                    else Warn("AssetDatabase.GetMainAssetInstanceID");
                }
                catch (Exception e) { Warn("AssetDatabase.GetMainAssetInstanceID", e); }
            }
            if (s_MainIdFunc == null) return 0;
            try { return s_MainIdFunc(path); }
            catch (Exception e)
            {
                Warn("AssetDatabase.GetMainAssetInstanceID", e);
                return 0;
            }
        }
    }
}
