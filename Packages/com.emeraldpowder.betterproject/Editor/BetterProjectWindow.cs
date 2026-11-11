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
        [SerializeField] float tileSize = 64; // the slider value, kept while in list mode
        [SerializeField] bool detailsView;    // list mode as a table (iconSize 0)
        [SerializeField] string detailsColumns = ""; // DetailsColumns.Serialize(); "" = default columns
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
        VisualElement _pinned;   // pinned sections, above the left panel's scroll area
        VisualElement _sections; // the other sections, in the scroll area above the folder tree
        VisualElement _right;
        bool _refreshPending;
        bool? _shownFileExtensions; // last applied "Show file extensions"; section labels are built with it
        SearchController _search;
        ToolbarSearchField _searchField;
        Button _objectsButton;
        ToolbarButton _createButton;
        ToolbarButton _favouriteButton;
        StarIcon _favouriteStar;
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
            _refreshPending = false; // a refresh scheduled on a previous UI would never run and block all later ones
            // Not serialized: a domain reload drops them anyway. A UI rebuilt without one (e.g. after a reimport) would
            // otherwise keep showing results with no chip to leave them and a tree showing one folder.
            _objectIds = null;
            _folders = null;
            titleContent = new GUIContent("Better Project");
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            if (Theme.StyleSheet != null) root.styleSheets.Add(Theme.StyleSheet);
            root.EnableInClassList(Theme.DarkHoverClass, EditorGUIUtility.isProSkin);

            // Toolbar: "+" (Create menu), search field (persisted text) + the "Selected objects" chip shown for
            // object-list results.
            var toolbar = new Toolbar();
            _createButton = new ToolbarButton { name = "create", tooltip = "Create" };
            _createButton.clicked += () => AssetsContextMenu.ShowCreateMenu(this, _createButton.worldBound);
            _createButton.style.flexShrink = 0;
            _createButton.style.flexDirection = FlexDirection.Row;
            _createButton.style.alignItems = Align.Center;
            var plus = new Image { image = EditorGUIUtility.IconContent("Toolbar Plus").image, scaleMode = ScaleMode.ScaleToFit };
            plus.style.width = 16;
            plus.style.height = 16;
            _createButton.Add(plus);
            var dropArrow = new Image { image = EditorGUIUtility.IconContent("icon dropdown").image, scaleMode = ScaleMode.ScaleToFit };
            dropArrow.style.width = 8;
            dropArrow.style.height = 16;
            _createButton.Add(dropArrow);
            toolbar.Add(_createButton);
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
                if (wasObjects && !_search.IsActive) LeaveResults();
                UpdateFavouriteButton();
                NotifySectionsShown();
            });
            // Esc clears the field (and leaves it when already empty). Trickle-down so the inner text field never sees it.
            _searchField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation();
                if (!string.IsNullOrEmpty(_searchField.value)) _searchField.value = "";
                else if (_objectIds != null) LeaveResults();
                else { _searchField.Blur(); _content?.Focus(); }
            }, TrickleDown.TrickleDown);
            _objectsButton = new Button(LeaveResults)
            {
                name = "objects-chip",
                tooltip = "Showing the objects listed by Select Dependencies. Click to return to the folder.",
            };
            _objectsButton.style.display = DisplayStyle.None;
            toolbar.Add(_objectsButton);
            toolbar.Add(_searchField);
            // Star: adds / removes the current folder, or the current search, as a favourite.
            _favouriteButton = new ToolbarButton(ToggleFavourite) { name = "favourite" };
            _favouriteButton.style.flexShrink = 0;
            _favouriteButton.style.justifyContent = Justify.Center;
            _favouriteButton.style.alignItems = Align.Center;
            _favouriteStar = new StarIcon();
            _favouriteButton.Add(_favouriteStar);
            toolbar.Add(_favouriteButton);
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
            _search.Cleared += () => { if (_content != null) LeaveResults(); };

            // Split view
            if (expandedFolders == null) expandedFolders = new List<string>();
            _tree = new FolderTree(expandedFolders);
            _content = new ContentView { IconSize = iconSize, DetailsLayout = DetailsColumns.Parse(detailsColumns) };
            _content.DetailsLayoutChanged += () => detailsColumns = _content.DetailsLayout.Serialize();
            var split = new TwoPaneSplitView(0, Mathf.Max(100f, splitterWidth), TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            // Left panel = Favourites, Recents and (optional) New assets sections above the folder tree, all at full
            // height in one ScrollView: a single scrollbar for the whole panel. Pinned sections ("Pin on top") sit
            // above that ScrollView instead and never scroll (ApplySettings moves them).
            var left = new VisualElement();
            _left = left;
            left.style.flexDirection = FlexDirection.Column;
            left.style.minWidth = 100;
            left.style.overflow = Overflow.Hidden; // pinned sections taller than the panel are cut, not drawn over
            _pinned = new VisualElement { name = "pinned" };
            _pinned.style.flexDirection = FlexDirection.Column;
            _pinned.style.flexShrink = 0;
            _pinned.style.borderBottomColor = Theme.Separator;
            left.Add(_pinned);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScroller.valueChanged += _ => FullNameOverlay.Hide();
            left.Add(scroll);
            var sections = _sections = new VisualElement { name = "sections" };
            sections.style.flexDirection = FlexDirection.Column;
            sections.style.flexShrink = 0;
            _favourites = CreateFavouritesSection();
            sections.Add(_favourites);
            _recents = CreateRecentsSection();
            sections.Add(_recents);
            _newAssets = CreateNewAssetsSection();
            sections.Add(_newAssets);
            scroll.Add(sections);
            scroll.Add(_tree);
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

            // Bottom bar, under the content only: the left panel keeps the full height.
            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.justifyContent = Justify.FlexEnd;
            bottom.style.height = 22;
            bottom.style.flexShrink = 0; // the content above grows; without this a tall panel squeezes the bar
            bottom.style.alignItems = Align.Center;
            if (iconSize > 0f) tileSize = iconSize;
            tileSize = Mathf.Clamp(tileSize, 32f, 96f);
            var slider = new Slider(32, 96) { value = tileSize };
            slider.style.width = 100;
            slider.style.marginRight = 6;
            slider.style.marginTop = slider.style.marginBottom = 0;
            slider.RegisterValueChangedCallback(e =>
            {
                tileSize = Mathf.Clamp(e.newValue, 32f, 96f);
                iconSize = tileSize;
                _content.IconSize = tileSize;
            });
            bottom.Add(slider);
            var listToggle = CreateViewToggle("VerticalLayoutGroup Icon", "List", "List view");
            var detailsToggle = CreateViewToggle(DetailsIcon(), "Details", "Details view");
            var tilesToggle = CreateViewToggle("GridLayoutGroup Icon", "Tiles", "Tile view");
            void SetViewMode(ViewMode mode)
            {
                bool tiles = mode == ViewMode.Tiles;
                iconSize = tiles ? tileSize : 0f;
                if (!tiles) detailsView = mode == ViewMode.Details;
                _content.DetailsMode = detailsView;
                _content.IconSize = iconSize;
                listToggle.SetValueWithoutNotify(mode == ViewMode.List);
                detailsToggle.SetValueWithoutNotify(mode == ViewMode.Details);
                tilesToggle.SetValueWithoutNotify(tiles);
                slider.style.display = tiles ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // Radio behaviour: clicking the active button keeps it on.
            listToggle.RegisterValueChangedCallback(_ => SetViewMode(ViewMode.List));
            detailsToggle.RegisterValueChangedCallback(_ => SetViewMode(ViewMode.Details));
            tilesToggle.RegisterValueChangedCallback(_ => SetViewMode(ViewMode.Tiles));
            SetViewMode(iconSize > 0f ? ViewMode.Tiles : detailsView ? ViewMode.Details : ViewMode.List);
            bottom.Add(listToggle);
            bottom.Add(detailsToggle);
            bottom.Add(tilesToggle);
            // The ping listener can only be created inside OnGUI (see PingTracker): a 1 px IMGUI hook does it.
            var pingHook = new IMGUIContainer(PingTracker.EnsureInstalledFromOnGUI);
            pingHook.style.width = 1;
            pingHook.style.height = 1;
            bottom.Add(pingHook);
            right.Add(bottom);

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
            _tree.ContextRequested += pos => AssetsContextMenu.Show(this, pos);

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
            ItemDrag.Dropped -= OnDropped;
            ItemDrag.Dropped += OnDropped;
            RecentsStore.Changed -= OnRecentsChanged;
            RecentsStore.Changed += OnRecentsChanged;
            NewAssetsStore.Changed -= OnNewAssetsChanged;
            NewAssetsStore.Changed += OnNewAssetsChanged;
            BetterProjectSettings.Changed -= OnSettingsChanged;
            BetterProjectSettings.Changed += OnSettingsChanged;
            PingTracker.Register(this);
            RevealSelection(); // highlight + scroll to the selected asset (e.g. the one whose folder was just opened)
        }

        // Settings edited in Project Settings apply live.
        void OnSettingsChanged() => ApplySettings();

        void ApplySettings()
        {
            var s = BetterProjectSettings.instance;
            if (_favourites == null || _recents == null || _newAssets == null || _tree == null) return;
            _content?.Refresh(); // list layout ("Remove offset in right panel"), labels ("Show file extensions")
            if (_shownFileExtensions.HasValue && _shownFileExtensions != s.ShowFileExtensions)
            {
                _favourites.Rebuild();
                _recents.Rebuild();
                _newAssets.Rebuild();
            }
            _shownFileExtensions = s.ShowFileExtensions;

            // Show: hidden sections take no space; the folder tree drops or regains its Packages root.
            _favourites.style.display = s.ShowFavourites ? DisplayStyle.Flex : DisplayStyle.None;
            _recents.style.display = s.ShowRecents ? DisplayStyle.Flex : DisplayStyle.None;
            _newAssets.style.display = s.ShowNewAssets ? DisplayStyle.Flex : DisplayStyle.None;
            _tree.SetShowPackages(s.ShowPackages);

            // "Pin on top": pinned sections above the scroll area, the others in it; both keep the sections' order.
            var pinned = new List<VisualElement>();
            var scrolled = new List<VisualElement>();
            (s.PinFavourites ? pinned : scrolled).Add(_favourites);
            (s.PinRecents ? pinned : scrolled).Add(_recents);
            scrolled.Add(_newAssets);
            Arrange(_pinned, pinned);
            Arrange(_sections, scrolled);
            // A line under the pinned sections, where the scrolled rows pass beneath them.
            bool anyPinned = (s.PinFavourites && s.ShowFavourites) || (s.PinRecents && s.ShowRecents);
            _pinned.style.borderBottomWidth = anyPinned ? 1 : 0;

            // "Sidebar spacing": a gap between visible sections only (none between rows, none above the first).
            // Counted per area: the first visible section of the pinned area and of the scroll area has none.
            float gap = Theme.SectionSpacing;
            bool before = false;
            foreach (var section in pinned) before |= SetGap(section, before, gap);
            before = false;
            foreach (var section in scrolled) before |= SetGap(section, before, gap);
            _tree.style.marginTop = before ? gap : 0;
            _tree.SetRootSpacing(gap); // between Assets and Packages

            if (_createButton != null) _createButton.style.display = s.ShowCreateButton ? DisplayStyle.Flex : DisplayStyle.None;

            // Highlight under mouse.
            _left?.EnableInClassList(Theme.NoHoverClass, !s.HoverLeftPanel);
            _right?.EnableInClassList(Theme.NoHoverClass, !s.HoverRightPanel);
        }

        // Top margin of a section: `gap` if a visible section is above it. Returns whether this one is visible.
        static bool SetGap(VisualElement section, bool visibleAbove, float gap)
        {
            section.style.marginTop = visibleAbove ? gap : 0;
            return section.style.display != DisplayStyle.None;
        }

        // Makes `children` the children of `container`, in that order; does nothing if they already are.
        static void Arrange(VisualElement container, List<VisualElement> children)
        {
            bool same = container.childCount == children.Count;
            for (int i = 0; same && i < children.Count; i++) same = container[i] == children[i];
            if (same) return;
            foreach (var child in children) container.Add(child); // Add moves it from its old parent, to the end
        }

        enum ViewMode { List, Details, Tiles }

        // The Search window's table view icon; null if this Unity version has none (the button shows text then).
        static Texture DetailsIcon()
        {
            string name = "Icons/QuickSearch/" + (EditorGUIUtility.isProSkin ? "d_" : "") + "TableView.png";
            return EditorGUIUtility.Load(name) as Texture;
        }

        static ToolbarToggle CreateViewToggle(string iconName, string fallbackText, string tooltip)
            => CreateViewToggle(EditorGUIUtility.IconContent(iconName).image, fallbackText, tooltip);

        static ToolbarToggle CreateViewToggle(Texture texture, string fallbackText, string tooltip)
        {
            var toggle = new ToolbarToggle { tooltip = tooltip };
            toggle.style.flexShrink = 0;
            if (texture == null)
            {
                toggle.text = fallbackText;
                return toggle;
            }
            var icon = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 16;
            icon.style.height = 16;
            toggle.Add(icon);
            return toggle;
        }

        // ---- favourites ----

        AssetRefSection CreateFavouritesSection()
        {
            if (favouritesExpanded == null) favouritesExpanded = new List<string>();
            var section = new AssetRefSection("Favourites", "Favorite Icon", FavouriteEntries, favouritesExpanded, favouritesCollapsed);
            section.CollapsedChanged += c => favouritesCollapsed = c;
            section.FolderOpened += OnFolderClicked;
            section.ItemOpened += OnItemOpened;
            section.SearchOpened += OpenSearch;
            section.FillChildContextMenu = AddOpenFolderItem;
            section.FillContextMenu = (entry, menu) =>
            {
                if (entry.Query != null)
                {
                    menu.AddItem(new GUIContent("Remove from Favourites"), false, () => FavouritesStore.RemoveSearch(entry.Query));
                    return;
                }
                AddOpenFolderItem(AssetRefUtil.PathOf(entry.Ref), menu);
                menu.AddItem(new GUIContent("Remove from Favourites"), false, () => FavouritesStore.Remove(entry.Ref));
            };
            section.HandleDrop = HandleFavouritesDrop;
            section.EmptyText = "Drag to add";
            UpdateFavouritesWarning(section);
            return section;
        }

        static IReadOnlyList<RefSectionEntry> FavouriteEntries()
        {
            var items = FavouritesStore.List.Items;
            var result = new List<RefSectionEntry>(items.Count);
            foreach (var e in items)
                result.Add(e.IsSearch ? RefSectionEntry.Search(e.Query, e.Name) : new RefSectionEntry(e.Ref, e.Name));
            return result;
        }

        /// <summary>Puts a saved search's query in the search field and shows its results at once.</summary>
        void OpenSearch(string query)
        {
            if (_searchField == null) return;
            if (_content != null && _content.Rename.IsActive) _content.Rename.Commit();
            _searchField.value = query; // the field's callback leaves object-list mode and sets the query
            _search.RunNow();
        }

        /// <summary>What the star button stands for now: the search shown, or the single folder shown.</summary>
        enum FavouriteTarget { None, Search, Folder }

        FavouriteTarget CurrentFavouriteTarget(out string folderGuid)
        {
            folderGuid = null;
            if (_objectIds != null) return FavouriteTarget.None;
            if (_search != null && _search.IsActive) return FavouriteTarget.Search;
            if (_folders != null || string.IsNullOrEmpty(currentFolder)) return FavouriteTarget.None;
            folderGuid = AssetDatabase.AssetPathToGUID(currentFolder); // none for the "Packages" root
            return string.IsNullOrEmpty(folderGuid) ? FavouriteTarget.None : FavouriteTarget.Folder;
        }

        void UpdateFavouriteButton()
        {
            if (_favouriteButton == null) return;
            bool on = false;
            string tip;
            var target = CurrentFavouriteTarget(out string guid);
            switch (target)
            {
                case FavouriteTarget.Search:
                    on = FavouritesStore.ContainsSearch(_search.Query);
                    tip = on ? "Remove this search from Favourites" : "Add this search to Favourites";
                    break;
                case FavouriteTarget.Folder:
                    on = FavouritesStore.Contains(new AssetRef(guid));
                    tip = on ? "Remove this folder from Favourites" : "Add this folder to Favourites";
                    break;
                default:
                    tip = "Add the current folder or search to Favourites";
                    break;
            }
            _favouriteButton.SetEnabled(target != FavouriteTarget.None);
            _favouriteButton.tooltip = tip;
            _favouriteStar.Filled = on;
        }

        void ToggleFavourite()
        {
            switch (CurrentFavouriteTarget(out string guid))
            {
                case FavouriteTarget.Search:
                    if (!FavouritesStore.RemoveSearch(_search.Query)) FavouritesStore.AddSearch(_search.Query);
                    break;
                case FavouriteTarget.Folder:
                    if (!FavouritesStore.Remove(new AssetRef(guid)))
                        FavouritesStore.Add(AssetDatabase.LoadAssetAtPath<Object>(currentFolder));
                    break;
            }
            UpdateFavouriteButton();
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
            UpdateFavouriteButton();
        }

        // ---- recents ----

        AssetRefSection CreateRecentsSection()
        {
            if (recentsExpanded == null) recentsExpanded = new List<string>();
            var section = new AssetRefSection("Recents", "UndoHistory", RecentEntries, recentsExpanded, recentsCollapsed);
            section.CollapsedChanged += c => recentsCollapsed = c;
            section.FolderOpened += OnFolderClicked;
            section.ItemOpened += OnItemOpened;
            section.FillChildContextMenu = AddOpenFolderItem;
            section.FillContextMenu = (entry, menu) =>
            {
                AddOpenFolderItem(AssetRefUtil.PathOf(entry.Ref), menu);
                menu.AddItem(new GUIContent("Remove from Recents"), false, () => RecentsStore.Remove(entry.Ref));
            };
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
            section.FillChildContextMenu = AddOpenFolderItem;
            section.FillContextMenu = (entry, menu) =>
            {
                AddOpenFolderItem(AssetRefUtil.PathOf(entry.Ref), menu);
                menu.AddItem(new GUIContent("Remove from New assets"), false, () => NewAssetsStore.Remove(entry.Ref));
            };
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

        // "Open folder": shows the folder containing the asset and flashes the asset there (a ping: selection kept).
        // `path` null = missing asset (item disabled).
        void AddOpenFolderItem(string path, GenericMenu menu)
        {
            var content = new GUIContent("Open folder");
            if (path == null || AssetPaths.Parent(path).Length == 0) { menu.AddDisabledItem(content); return; }
            menu.AddItem(content, false, () => PingAsset(path));
        }

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
            ItemDrag.Dropped -= OnDropped;
            RecentsStore.Changed -= OnRecentsChanged;
            NewAssetsStore.Changed -= OnNewAssetsChanged;
            BetterProjectSettings.Changed -= OnSettingsChanged;
            PingTracker.Unregister(this);
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
                RefreshLog.Write("created '" + created + "' in '" + folder + "' (current=" + currentFolder
                                 + ", valid folder=" + (created != null && AssetDatabase.IsValidFolder(created)) + ")");
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

        /// <summary>
        /// A ping (e.g. an object field clicked in the Inspector), as in Unity's Project window: shows the asset's folder
        /// (leaving search results), scrolls to the asset and flashes it. Unity's selection is left as it is.
        /// </summary>
        internal void PingAsset(string assetPath)
        {
            if (_content == null || !AssetPaths.IsProjectAssetPath(assetPath)) return;
            string folder = AssetPaths.Parent(assetPath);
            if (folder.Length == 0) return; // a root ("Assets"): no folder shows it
            if (folder != currentFolder || _folders != null || _objectIds != null || _search.IsActive) NavigateTo(folder);
            if (folder == currentFolder) _content.Ping(assetPath); // NavigateTo refuses folders it can't show
        }

        /// <summary>Shows what the view represents now: object-list results, search results, or the current folder.</summary>
        void ShowCurrentFolder()
        {
            UpdateBreadcrumbs();
            UpdateFavouriteButton();
            // Create puts the asset in the current folder (also while results are shown): only where it can be written.
            _createButton?.SetEnabled(!string.IsNullOrEmpty(currentFolder) && AssetsContextMenu.IsWritableFolder(currentFolder));
            List<ContentItem> objects = _objectIds != null ? SearchController.FromInstanceIds(_objectIds) : null;
            if (objects != null && objects.Count == 0)
            {
                // Every listed object is gone (deleted, or ids stale after a reimport): back to the folder.
                _objectIds = null;
                UpdateObjectsChip();
                UpdateBreadcrumbs();
                objects = null;
            }
            if (objects != null)
            {
                _content.DropFolder = null; // results are not a folder: drops on empty space are rejected
                _content.Show(objects);
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
            NotifySectionsShown();
        }

        // A subfolder or saved search clicked in a section stays highlighted while it is what the right panel shows.
        void NotifySectionsShown()
        {
            bool searching = _objectIds == null && _search != null && _search.IsActive;
            string folder = _objectIds == null && !searching && _folders == null ? currentFolder : null;
            string query = searching ? _search.Query : null;
            foreach (var section in new[] { _favourites, _recents, _newAssets })
            {
                section?.OnFolderShown(folder);
                section?.OnSearchShown(query);
            }
        }

        /// <summary>
        /// Folder for a newly opened window: the folder of the selected asset; else the folder Unity's Project window
        /// shows; else none ("No folder is selected").
        /// </summary>
        static string PickInitialFolder()
        {
            string selected = SelectionFolder();
            if (selected != null) return selected;
            if (UnityInternals.TryGetProjectWindowFolder(out string folder)
                && (folder == "Packages" || AssetDatabase.IsValidFolder(folder)))
                return folder;
            return "";
        }

        /// <summary>Folder that shows Unity's active selection (the root itself for "Assets"); null if not a project asset.</summary>
        static string SelectionFolder()
        {
            var active = Selection.activeObject;
            string path = active != null ? AssetDatabase.GetAssetPath(active) : null; // sub-assets: the main asset's path
            if (string.IsNullOrEmpty(path) || !AssetPaths.IsProjectAssetPath(path)) return null;
            string parent = AssetPaths.Parent(path);
            if (parent == "Packages" || AssetDatabase.IsValidFolder(parent)) return parent;
            return path == "Assets" ? path : null;
        }

        /// <summary>
        /// Leaves search / object-list results, as Unity's Project window does: shows the folder of the asset selected
        /// (e.g. picked among the results) and scrolls to it; with no asset selected, the folder shown before.
        /// </summary>
        void LeaveResults()
        {
            string folder = SelectionFolder();
            bool navigated = false;
            if (folder != null && (folder != currentFolder || _folders != null))
            {
                NavigateTo(folder);
                navigated = currentFolder == folder && _folders == null; // NavigateTo refuses folders it can't show
            }
            if (!navigated)
            {
                ClearSearch();
                ShowCurrentFolder();
            }
            RevealSelection();
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
            UpdateFavouriteButton();
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
            RefreshLog.Write("projectChanged (pending=" + _refreshPending + ", content=" + (_content != null)
                             + ", panel=" + (rootVisualElement.panel != null) + ")");
            if (_refreshPending || _content == null) return;
            _refreshPending = true;
            rootVisualElement.schedule.Execute(() =>
            {
                RefreshLog.Write("scheduled refresh runs (pending=" + _refreshPending + ")");
                if (_refreshPending) RefreshFromProject();
            });
        }

        void OnDropped()
        {
            if (_content == null) return;
            RefreshFromProject();
            Repaint();
        }

        /// <summary>Rebuilds every panel from the asset database (assets added, moved, renamed or deleted).</summary>
        void RefreshFromProject()
        {
            _refreshPending = false;
            RefreshLog.Write("RefreshFromProject (folder=" + currentFolder + ", folders=" + (_folders?.Count ?? 0) + ")");
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
        }
    }

    /// <summary>Temporary diagnostics for panels not refreshing after asset changes. Set Enabled = false to silence.</summary>
    static class RefreshLog
    {
        internal const bool Enabled = true;

        internal static void Write(string message)
        {
            if (Enabled) Debug.Log("[Better Project][refresh] " + message);
        }
    }
}
