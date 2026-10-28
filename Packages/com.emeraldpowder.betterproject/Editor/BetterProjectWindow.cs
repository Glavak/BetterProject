using System.Collections.Generic;
using BetterProject.Core;
using BetterProject.Editor.Internal;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    public class BetterProjectWindow : EditorWindow
    {
        [SerializeField] string currentFolder = ""; // "" = no folder selected (see PickInitialFolder)
        [SerializeField] bool initialized;          // false only for a newly opened window
        [SerializeField] float splitterWidth = 220;
        [SerializeField] float iconSize; // 0 = list
        [SerializeField] List<string> expandedFolders = new List<string>();
        [SerializeField] string searchText = "";
        [SerializeField] bool favouritesCollapsed;
        [SerializeField] List<string> favouritesExpanded = new List<string>();
        [SerializeField] bool recentsCollapsed;
        [SerializeField] List<string> recentsExpanded = new List<string>();
        [SerializeField] bool newAssetsCollapsed;
        [SerializeField] List<string> newAssetsExpanded = new List<string>();

        FolderTree _tree;
        AssetRefSection _favourites;
        AssetRefSection _recents;
        AssetRefSection _newAssets;
        ContentView _content;
        Breadcrumbs _breadcrumbs;
        VisualElement _left;
        VisualElement _right;
        bool _refreshPending;
        SearchController _search;
        ToolbarSearchField _searchField;
        Button _objectsButton;
        int[] _objectIds; // "Selected objects" results (Select Dependencies); null = not in that mode
        // Several folders selected in the tree: the right panel shows all their contents. Null = just currentFolder
        // (which is always the primary, i.e. last clicked, folder and the one breadcrumbs / Create use).
        List<string> _folders;

        public string CurrentFolder => currentFolder;

        [MenuItem("Window/General/Better Project")]
        static void Open()
        {
            var w = CreateWindow<BetterProjectWindow>();
            w.titleContent = new GUIContent("Better Project");
            w.Show();
        }

        void CreateGUI()
        {
            titleContent = new GUIContent("Better Project");
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            if (Theme.StyleSheet != null) root.styleSheets.Add(Theme.StyleSheet);

            // Toolbar: search field (persisted text) + the "Selected objects" chip shown for object-list results.
            var toolbar = new Toolbar();
            _searchField = new ToolbarSearchField { name = "search" };
            _searchField.style.flexGrow = 1;
            _searchField.style.flexShrink = 1;
            _searchField.style.minWidth = 40; // the theme's default min width keeps narrow windows from fitting the toolbar
            _searchField.SetValueWithoutNotify(searchText ?? "");
            _searchField.RegisterValueChangedCallback(e =>
            {
                searchText = e.newValue ?? "";
                bool wasObjects = _objectIds != null;
                if (wasObjects) { _objectIds = null; UpdateObjectsChip(); }
                _search.SetQuery(searchText);
                // Left "Selected objects" with a query that trims to empty: no search will replace the stale
                // object-list results, so show the folder now (SetQuery raises Cleared only when a search was active).
                if (wasObjects && !_search.IsActive) { ShowCurrentFolder(); _content.SyncFromUnitySelection(); }
            });
            // Esc clears the field (and leaves it when already empty). Trickle-down so the inner text field never sees it.
            _searchField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation();
                if (!string.IsNullOrEmpty(_searchField.value)) _searchField.value = "";
                else if (_objectIds != null) { ClearSearch(); ShowCurrentFolder(); _content.SyncFromUnitySelection(); }
                else { _searchField.Blur(); _content?.Focus(); }
            }, TrickleDown.TrickleDown);
            _objectsButton = new Button(() => { ClearSearch(); ShowCurrentFolder(); _content.SyncFromUnitySelection(); })
            {
                name = "objects-chip",
                tooltip = "Showing the objects listed by Select Dependencies. Click to return to the folder.",
            };
            _objectsButton.style.display = DisplayStyle.None;
            toolbar.Add(_objectsButton);
            toolbar.Add(_searchField);
            var settingsButton = new ToolbarButton(BetterProjectSettingsProvider.Open)
            {
                name = "settings",
                tooltip = "Better Project settings",
            };
            settingsButton.style.flexShrink = 0;
            settingsButton.style.justifyContent = Justify.Center;
            var gear = new Image { image = EditorGUIUtility.IconContent("SettingsIcon").image, scaleMode = ScaleMode.ScaleToFit };
            gear.style.width = 16;
            gear.style.height = 16;
            settingsButton.Add(gear);
            toolbar.Add(settingsButton);
            root.Add(toolbar);
            _search = new SearchController(root);
            _search.ResultsReady += OnSearchResults;
            _search.Cleared += () => { if (_content != null) { ShowCurrentFolder(); _content.SyncFromUnitySelection(); } };

            // Split view
            if (expandedFolders == null) expandedFolders = new List<string>();
            _tree = new FolderTree(expandedFolders);
            _content = new ContentView { IconSize = iconSize };
            var split = new TwoPaneSplitView(0, Mathf.Max(100f, splitterWidth), TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            // Left panel = Favourites, Recents and (optional) New assets sections above the folder tree, all at full
            // height in one ScrollView: a single scrollbar for the whole panel.
            var left = new ScrollView(ScrollViewMode.Vertical);
            _left = left;
            left.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            left.verticalScroller.valueChanged += _ => FullNameOverlay.Hide();
            left.style.minWidth = 100;
            var sections = new VisualElement { name = "sections" };
            sections.style.flexDirection = FlexDirection.Column;
            sections.style.flexShrink = 0;
            _favourites = CreateFavouritesSection();
            sections.Add(_favourites);
            _recents = CreateRecentsSection();
            sections.Add(_recents);
            _newAssets = CreateNewAssetsSection();
            sections.Add(_newAssets);
            left.Add(sections);
            left.Add(_tree);
            split.Add(left);
            // Right panel = breadcrumb bar above the content view.
            _breadcrumbs = new Breadcrumbs();
            _breadcrumbs.Navigate += NavigateTo;
            var right = _right = new VisualElement();
            right.style.flexDirection = FlexDirection.Column;
            right.style.minWidth = 100;
            right.style.backgroundColor = Theme.ContentBackground;
            right.Add(_breadcrumbs);
            _content.style.flexGrow = 1;
            right.Add(_content);
            split.Add(right);
            ApplySettings();
            left.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = e.newRect.width;
                if (w > 0 && Mathf.Abs(w - splitterWidth) > 0.5f) splitterWidth = w;
            });
            root.Add(split);

            // Bottom bar
            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.justifyContent = Justify.FlexEnd;
            bottom.style.height = 22;
            bottom.style.flexShrink = 0; // the split above grows; without this a tall panel squeezes the bar
            bottom.style.alignItems = Align.Center;
            var slider = new Slider(0, 96) { value = ContentView.SnapIconSize(iconSize) };
            slider.style.width = 100;
            slider.style.marginRight = 6;
            slider.style.marginTop = slider.style.marginBottom = 0;
            slider.RegisterValueChangedCallback(e =>
            {
                float snapped = ContentView.SnapIconSize(e.newValue); // (0, 32) snaps to 32
                if (!Mathf.Approximately(snapped, e.newValue)) slider.SetValueWithoutNotify(snapped);
                iconSize = snapped;
                _content.IconSize = snapped;
            });
            bottom.Add(slider);
            root.Add(bottom);

            _tree.FoldersSelected += OnTreeFoldersSelected;
            _content.ItemOpened += OnItemOpened;
            // Sections highlight only what was clicked in them: a press anywhere else drops that highlight.
            _tree.RegisterCallback<PointerDownEvent>(_ => ClearSectionSelection(null), TrickleDown.TrickleDown);
            _content.RegisterCallback<PointerDownEvent>(_ => ClearSectionSelection(null), TrickleDown.TrickleDown);
            _favourites.RowSelected += () => ClearSectionSelection(_favourites);
            _recents.RowSelected += () => ClearSectionSelection(_recents);
            _newAssets.RowSelected += () => ClearSectionSelection(_newAssets);
            _content.ParentRequested += () => NavigateTo(AssetPaths.Parent(currentFolder));
            _content.ContextRequested += pos => AssetsContextMenu.Show(this, pos);

            root.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            root.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);

            if (!initialized)
            {
                currentFolder = PickInitialFolder();
                initialized = true;
            }
            else if (!string.IsNullOrEmpty(currentFolder) && !AssetDatabase.IsValidFolder(currentFolder) && currentFolder != "Packages")
                currentFolder = "Assets";
            _search.SetQuery(searchText); // restores a persisted query (debounced; the folder shows until results arrive)
            ShowCurrentFolder();
            _tree.Reveal(currentFolder);

            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            FavouritesStore.Changed -= OnFavouritesChanged;
            FavouritesStore.Changed += OnFavouritesChanged;
            RecentsStore.Changed -= OnRecentsChanged;
            RecentsStore.Changed += OnRecentsChanged;
            NewAssetsStore.Changed -= OnNewAssetsChanged;
            NewAssetsStore.Changed += OnNewAssetsChanged;
            BetterProjectSettings.Changed -= OnSettingsChanged;
            BetterProjectSettings.Changed += OnSettingsChanged;
            RevealSelection(); // highlight + scroll to the selected asset (e.g. the one whose folder was just opened)
        }

        // Settings edited in Project Settings apply live.
        void OnSettingsChanged() => ApplySettings();

        void ApplySettings()
        {
            var s = BetterProjectSettings.instance;
            if (_favourites == null || _recents == null || _newAssets == null || _tree == null) return;
            _content?.Refresh(); // list layout ("Remove offset in right panel")

            // Show: hidden sections take no space; the folder tree drops or regains its Packages root.
            _favourites.style.display = s.ShowFavourites ? DisplayStyle.Flex : DisplayStyle.None;
            _recents.style.display = s.ShowRecents ? DisplayStyle.Flex : DisplayStyle.None;
            _newAssets.style.display = s.ShowNewAssets ? DisplayStyle.Flex : DisplayStyle.None;
            _tree.SetShowPackages(s.ShowPackages);

            // "Sidebar spacing": a gap between visible sections only (none between rows, none above the first).
            float gap = Theme.SectionSpacing;
            _recents.style.marginTop = s.ShowFavourites ? gap : 0;
            _newAssets.style.marginTop = s.ShowFavourites || s.ShowRecents ? gap : 0;
            _tree.style.marginTop = s.ShowFavourites || s.ShowRecents || s.ShowNewAssets ? gap : 0;
            _tree.SetRootSpacing(gap); // between Assets and Packages

            // Highlight under mouse.
            _left?.EnableInClassList(Theme.NoHoverClass, !s.HoverLeftPanel);
            _right?.EnableInClassList(Theme.NoHoverClass, !s.HoverRightPanel);
        }

        // ---- favourites ----

        AssetRefSection CreateFavouritesSection()
        {
            if (favouritesExpanded == null) favouritesExpanded = new List<string>();
            var section = new AssetRefSection("Favourites", "Favorite Icon", FavouriteEntries, favouritesExpanded, favouritesCollapsed);
            section.CollapsedChanged += c => favouritesCollapsed = c;
            section.FolderOpened += OnFolderClicked;
            section.ItemOpened += OnItemOpened;
            section.FillContextMenu = (entry, menu) =>
                menu.AddItem(new GUIContent("Remove from Favourites"), false, () => FavouritesStore.Remove(entry.Ref));
            section.HandleDrop = HandleFavouritesDrop;
            section.EmptyText = "Drag to add";
            UpdateFavouritesWarning(section);
            return section;
        }

        static IReadOnlyList<RefSectionEntry> FavouriteEntries()
        {
            var items = FavouritesStore.List.Items;
            var result = new List<RefSectionEntry>(items.Count);
            foreach (var e in items) result.Add(new RefSectionEntry(e.Ref, e.Name));
            return result;
        }

        /// <summary>Adds every dragged asset / folder / sub-asset that is not a favourite yet. Never moves anything.</summary>
        static bool HandleFavouritesDrop(bool perform)
        {
            var objects = DragAndDrop.objectReferences;
            var toAdd = new List<Object>();
            if (objects != null)
                foreach (var o in objects)
                    if (AssetRefUtil.TryFromObject(o, out var r) && !FavouritesStore.Contains(r)) toAdd.Add(o);
            if (toAdd.Count == 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return false;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (!perform) return true;
            DragAndDrop.AcceptDrag();
            foreach (var o in toAdd) FavouritesStore.Add(o); // refused (with a warning) while the file is unreadable
            return true;
        }

        void OnFavouritesChanged()
        {
            if (_favourites == null) return;
            UpdateFavouritesWarning(_favourites);
            _favourites.Rebuild();
        }

        // ---- recents ----

        AssetRefSection CreateRecentsSection()
        {
            if (recentsExpanded == null) recentsExpanded = new List<string>();
            var section = new AssetRefSection("Recents", "UndoHistory", RecentEntries, recentsExpanded, recentsCollapsed);
            section.CollapsedChanged += c => recentsCollapsed = c;
            section.FolderOpened += OnFolderClicked;
            section.ItemOpened += OnItemOpened;
            section.FillContextMenu = (entry, menu) =>
                menu.AddItem(new GUIContent("Remove from Recents"), false, () => RecentsStore.Remove(entry.Ref));
            section.FillHeaderContextMenu = menu =>
            {
                if (RecentsStore.List.Items.Count > 0) menu.AddItem(new GUIContent("Clear Recents"), false, RecentsStore.Clear);
                else menu.AddDisabledItem(new GUIContent("Clear Recents"));
            };
            return section;
        }

        static IReadOnlyList<RefSectionEntry> RecentEntries()
        {
            var items = RecentsStore.List.Items;
            var result = new List<RefSectionEntry>(items.Count);
            // Deleted assets are pruned by RecentsStore on projectChanged; never show one in between (no stored name).
            foreach (var r in items)
                if (AssetRefUtil.PathOf(r) != null) result.Add(new RefSectionEntry(r, ""));
            return result;
        }

        void OnRecentsChanged() => _recents?.Rebuild();

        // ---- new assets ----

        AssetRefSection CreateNewAssetsSection()
        {
            if (newAssetsExpanded == null) newAssetsExpanded = new List<string>();
            var section = new AssetRefSection("New assets", "Toolbar Plus", NewAssetEntries, newAssetsExpanded, newAssetsCollapsed);
            section.CollapsedChanged += c => newAssetsCollapsed = c;
            section.FolderOpened += OnFolderClicked;
            section.ItemOpened += OnItemOpened;
            section.FillContextMenu = (entry, menu) =>
                menu.AddItem(new GUIContent("Remove from New assets"), false, () => NewAssetsStore.Remove(entry.Ref));
            section.FillHeaderContextMenu = menu =>
            {
                if (NewAssetsStore.List.Items.Count > 0) menu.AddItem(new GUIContent("Clear New assets"), false, NewAssetsStore.Clear);
                else menu.AddDisabledItem(new GUIContent("Clear New assets"));
            };
            return section;
        }

        static IReadOnlyList<RefSectionEntry> NewAssetEntries()
        {
            var items = NewAssetsStore.List.Items;
            var result = new List<RefSectionEntry>(items.Count);
            foreach (var r in items)
                if (AssetRefUtil.PathOf(r) != null) result.Add(new RefSectionEntry(r, ""));
            return result;
        }

        void OnNewAssetsChanged() => _newAssets?.Rebuild();

        static void UpdateFavouritesWarning(AssetRefSection section) =>
            section.SetWarning(FavouritesStore.LoadFailed ? "Favourites file unreadable: " + FavouritesStore.LoadError : null);

        void OnFocus() => FavouritesStore.ReloadIfChanged();

        // The next click on an already selected item only re-focuses the window: it must not arm slow-click rename.
        void OnLostFocus() => _content?.NotifyFocusLost();

        // Reload: cancel any edit (a pending create is abandoned) — no asset writes while the domain goes down.
        // Runs before OnDisable, so OnDisable then finds nothing active.
        void OnBeforeAssemblyReload()
        {
            if (_content != null && _content.Rename.IsActive) _content.Rename.Cancel();
        }

        void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            FavouritesStore.Changed -= OnFavouritesChanged;
            RecentsStore.Changed -= OnRecentsChanged;
            NewAssetsStore.Changed -= OnNewAssetsChanged;
            BetterProjectSettings.Changed -= OnSettingsChanged;
            AssetsContextMenu.Disarm(this);
            // Window closing = focus loss: commit the typed name, but outside teardown (next editor tick).
            if (_content != null && _content.Rename.IsActive) _content.Rename.CommitDeferred();
        }

        /// <summary>Selects and scrolls to the asset (navigating to its folder if needed) and starts inline rename.</summary>
        public void BeginRename(string assetPath)
        {
            if (_content == null || string.IsNullOrEmpty(assetPath)) return;
            string folder = AssetPaths.Parent(assetPath);
            if (_content.IndexOfPath(assetPath) < 0 && folder.Length > 0 && folder != currentFolder) NavigateTo(folder);
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj == null) return;
            if (Selection.activeObject != obj || Selection.objects.Length != 1) Selection.activeObject = obj;
            _content.SyncFromUnitySelection();
            _content.BeginRename(assetPath);
        }

        /// <summary>Shows a placeholder for a captured Assets/Create item in the current folder and starts naming it.</summary>
        internal void BeginCreate(UnityInternals.PendingCreate pending)
        {
            string folder = currentFolder;
            if (_content == null)
            {
                AssetsContextMenu.CompleteCreate(folder, pending, pending.DefaultName);
                return;
            }
            Focus();
            // Search / "Selected objects" results are not the folder the asset is created in: show that folder first
            // (as Unity does), so the placeholder and the created asset appear where the user sees them.
            if ((_search != null && _search.IsActive) || _objectIds != null)
            {
                if (_content.Rename.IsActive) _content.Rename.Commit(); // leaving the results = focus loss
                ClearSearch();
                ShowCurrentFolder();
                _content.SyncFromUnitySelection();
            }
            bool done = false;
            void Finish(string name)
            {
                if (done) return;
                done = true;
                string created = AssetsContextMenu.CompleteCreate(folder, pending, name); // empty name → default name
                if (created == null || this == null || _content == null) return; // window may have closed (deferred commit)
                if (folder == currentFolder)
                {
                    ShowCurrentFolder();
                    _content.SyncFromUnitySelection();
                    _content.ScrollToPath(created);
                }
            }
            bool isFolder = string.IsNullOrEmpty(pending.Extension); // only sorts the placeholder row
            _content.BeginPendingCreate(folder, pending.DefaultName, pending.Extension, pending.Icon, isFolder,
                name => Finish(name),                                     // Enter / focus loss / navigate / close
                () => AssetsContextMenu.CancelCreate(pending));           // Esc / reload: nothing is created
        }

        // ---- commands (Delete / SoftDelete / Duplicate / Rename) ----

        static bool IsHandledCommand(string name) =>
            name == "Delete" || name == "SoftDelete" || name == "Duplicate" || name == "Rename";

        // Commands aimed at a text field (rename field, search field) belong to the field.
        static bool TargetsTextField(EventBase evt)
        {
            for (var e = evt.target as VisualElement; e != null; e = e.parent)
                if (e is TextField) return true;
            return false;
        }

        void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (_content == null || _content.Rename.IsActive || TargetsTextField(evt) || !IsHandledCommand(evt.commandName)) return;
            var paths = _content.SelectedVisiblePaths();
            bool ok = evt.commandName == "Rename" ? paths.Count == 1 && AssetsContextMenu.IsModifiable(paths[0])
                                                  : paths.Exists(AssetsContextMenu.IsModifiable);
            if (ok) evt.StopPropagation();
        }

        void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (_content == null || _content.Rename.IsActive || TargetsTextField(evt) || !IsHandledCommand(evt.commandName)) return;
            var paths = _content.SelectedVisiblePaths();
            if (paths.Count == 0) return;
            evt.StopPropagation();
            switch (evt.commandName)
            {
                case "Delete":
                case "SoftDelete":
                    AssetsContextMenu.DeleteWithConfirmation(paths);
                    Focus();
                    break;
                case "Duplicate":
                    var copies = AssetsContextMenu.Duplicate(paths);
                    if (copies.Count > 0)
                    {
                        var objs = new List<Object>();
                        foreach (var c in copies)
                        {
                            var o = AssetDatabase.LoadMainAssetAtPath(c);
                            if (o != null) objs.Add(o);
                        }
                        Selection.objects = objs.ToArray();
                        ShowCurrentFolder();
                        _content.SyncFromUnitySelection();
                        _content.ScrollToPath(copies[copies.Count - 1]);
                    }
                    break;
                case "Rename":
                    if (paths.Count == 1) BeginRename(paths[0]);
                    break;
            }
        }

        public void NavigateTo(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            if (folderPath != "Packages" && !AssetDatabase.IsValidFolder(folderPath)) return;
            if (_content != null && _content.Rename.IsActive) _content.Rename.Commit(); // navigating away = focus loss
            currentFolder = folderPath;
            _folders = null;
            AssetsContextMenu.OnWindowFolderChanged(this);
            if (_content == null) return;
            ClearSearch(); // navigating to a folder leaves search / object-list results
            ShowCurrentFolder();
            _tree.Reveal(folderPath);
            _content.SyncFromUnitySelection();
        }

        /// <summary>Scrolls to Unity's active selection if it is shown in the current folder.</summary>
        public void RevealSelection()
        {
            if (_content == null) return;
            _content.SyncFromUnitySelection();
            var active = Selection.activeObject;
            if (active != null) _content.ScrollToPath(AssetDatabase.GetAssetPath(active));
        }

        /// <summary>Shows what the view represents now: object-list results, search results, or the current folder.</summary>
        void ShowCurrentFolder()
        {
            UpdateBreadcrumbs();
            if (_objectIds != null)
            {
                _content.DropFolder = null; // results are not a folder: drops on empty space are rejected
                _content.Show(SearchController.FromInstanceIds(_objectIds));
            }
            else if (_search != null && _search.IsActive)
            {
                _content.DropFolder = null;
                _search.RunNow();
            }
            else if (_folders != null)
            {
                // Several folders: their contents one after the other, in tree order. Not one folder, so drops on
                // empty space are rejected (folder items remain drop targets).
                _content.DropFolder = null;
                var items = new List<ContentItem>();
                foreach (var f in _folders) items.AddRange(ContentView.ListFolder(f));
                _content.Show(items);
            }
            else if (string.IsNullOrEmpty(currentFolder))
            {
                _content.DropFolder = null;
                _content.Show(new List<ContentItem>());
            }
            else
            {
                _content.DropFolder = currentFolder;
                _content.Show(ContentView.ListFolder(currentFolder));
            }
            // Folder view with no folder: say so in the empty panel.
            _content.EmptyMessage = _objectIds == null && (_search == null || !_search.IsActive) && _folders == null
                                    && string.IsNullOrEmpty(currentFolder)
                ? "No folder is selected"
                : null;
        }

        /// <summary>
        /// Folder for a newly opened window: the folder of the selected asset; else the folder Unity's Project window
        /// shows; else none ("No folder is selected").
        /// </summary>
        static string PickInitialFolder()
        {
            var active = Selection.activeObject;
            string path = active != null ? AssetDatabase.GetAssetPath(active) : null; // sub-assets: the main asset's path
            if (!string.IsNullOrEmpty(path) && AssetPaths.IsProjectAssetPath(path))
            {
                string parent = AssetPaths.Parent(path);
                if (parent == "Packages" || AssetDatabase.IsValidFolder(parent)) return parent;
                if (path == "Assets") return path;
            }
            if (UnityInternals.TryGetProjectWindowFolder(out string folder)
                && (folder == "Packages" || AssetDatabase.IsValidFolder(folder)))
                return folder;
            return "";
        }

        void OnTreeFoldersSelected(IReadOnlyList<string> paths, string primary)
        {
            // Tree selection only shows the folders' contents; Unity's selection (the Inspector) is left as it is.
            if (paths.Count <= 1) { OnFolderClicked(primary); return; }
            if (_content != null && _content.Rename.IsActive) _content.Rename.Commit();
            ClearSearch();
            currentFolder = primary;
            _folders = new List<string>(paths);
            AssetsContextMenu.OnWindowFolderChanged(this);
            ShowCurrentFolder();
            _content.SyncFromUnitySelection();
        }

        // Object-list mode says "Selected objects"; search says "Search results"; otherwise the folder path.
        void UpdateBreadcrumbs()
        {
            if (_breadcrumbs == null) return;
            if (_objectIds != null) _breadcrumbs.SetSearch("Selected objects");
            else if (_search != null && _search.IsActive) _breadcrumbs.SetSearch();
            else if (_folders != null) _breadcrumbs.SetSearch(_folders.Count + " folders");
            else if (string.IsNullOrEmpty(currentFolder)) _breadcrumbs.SetSearch("");
            else _breadcrumbs.SetFolder(currentFolder);
        }

        void OnSearchResults(IReadOnlyList<ContentItem> items)
        {
            if (_content == null) return;
            UpdateBreadcrumbs();
            _content.DropFolder = null;
            _content.Show(items);
            _content.SyncFromUnitySelection();
        }

        /// <summary>Empties the search field and object-list mode without refreshing the view (caller does).</summary>
        void ClearSearch()
        {
            _objectIds = null;
            searchText = "";
            _search?.Reset();
            _searchField?.SetValueWithoutNotify("");
            UpdateObjectsChip();
        }

        void UpdateObjectsChip()
        {
            if (_objectsButton == null) return;
            bool on = _objectIds != null;
            _objectsButton.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) _objectsButton.text = "Selected objects (" + _objectIds.Length + ")  \u00D7";
        }

        /// <summary>
        /// Shows explicit objects (Select Dependencies) as a flat results list. The search field is emptied and a
        /// non-editable "Selected objects" chip appears next to it; typing, the chip or navigating to a folder leaves it.
        /// </summary>
        internal void ShowObjectList(int[] instanceIds)
        {
            if (_content == null || instanceIds == null) return;
            // Nothing listable (no ids, or none of them an asset): stay where we are rather than show an empty list.
            if (SearchController.FromInstanceIds(instanceIds).Count == 0) return;
            ClearSearch();
            _objectIds = instanceIds;
            UpdateObjectsChip();
            ShowCurrentFolder();
            _content.SyncFromUnitySelection();
        }

        void OnFolderClicked(string path)
        {
            if (path == currentFolder && _folders == null) // from several folders back to one: navigate
            {
                // Clicking the folder being shown while searching leaves the search.
                if (_search.IsActive || _objectIds != null) { ClearSearch(); ShowCurrentFolder(); _content.SyncFromUnitySelection(); }
                return;
            }
            NavigateTo(path);
        }

        void OnItemOpened(ContentItem item)
        {
            if (item.IsFolder) { NavigateTo(item.Path); return; }
            if (item.IsSubAsset) { if (item.InstanceId != 0) AssetDatabase.OpenAsset(item.InstanceId); return; }
            var obj = AssetDatabase.LoadMainAssetAtPath(item.Path);
            if (obj != null) AssetDatabase.OpenAsset(obj);
        }

        void OnSelectionChanged()
        {
            _content?.SyncFromUnitySelection();
            _favourites?.OnUnitySelectionChanged();
            _recents?.OnUnitySelectionChanged();
            _newAssets?.OnUnitySelectionChanged();
        }

        void ClearSectionSelection(AssetRefSection except)
        {
            if (_favourites != except) _favourites?.ClearSelection();
            if (_recents != except) _recents?.ClearSelection();
            if (_newAssets != except) _newAssets?.ClearSelection();
        }

        void OnProjectChanged()
        {
            if (_refreshPending || _content == null) return;
            _refreshPending = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _refreshPending = false;
                if (_content == null) return;
                string before = currentFolder;
                while (!string.IsNullOrEmpty(currentFolder) && currentFolder != "Packages" && !AssetDatabase.IsValidFolder(currentFolder))
                {
                    string parent = AssetPaths.Parent(currentFolder);
                    currentFolder = parent.Length > 0 ? parent : "Assets";
                }
                if (currentFolder != before) AssetsContextMenu.OnWindowFolderChanged(this); // the proxy follows the fallback
                if (_folders != null)
                {
                    _folders.RemoveAll(f => f != "Packages" && !AssetDatabase.IsValidFolder(f));
                    if (_folders.Count <= 1 || !_folders.Contains(currentFolder)) _folders = null; // back to one folder
                }
                _tree.Rebuild();
                _favourites?.Rebuild(); // renamed / moved / deleted favourites
                _recents?.Rebuild(); // renamed / moved recents (deleted ones are dropped by RecentsStore)
                _newAssets?.Rebuild();
                _content.InvalidateSubAssetCache();
                ShowCurrentFolder();
                if (_folders == null) _tree.Reveal(currentFolder); // a multi-selection is restored by Rebuild
                _content.SyncFromUnitySelection();
            });
        }
    }
}
