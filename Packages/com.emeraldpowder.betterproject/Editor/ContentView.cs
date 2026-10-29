using System;
using System.Collections.Generic;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    public class ContentView : VisualElement
    {
        const float RowHeight = Theme.RowHeight;
        static Color SelectedColor => ItemDrag.SelectedColor;

        // Slow click: the second click must come later than this after the first, and rename starts only after this
        // long without a further click — so a slow double-click still opens. At least the OS double-click time.
        static readonly long SlowClickMs = Math.Max(500, SystemDoubleClickMs());

        const float TilePad = 12f;            // horizontal space around a tile's thumbnail
        const float TileLabelHeight = 26f;    // two lines of 10px text
        const float ScrollbarAllowance = 16f;
        const float ArrowWidth = 12f;
        const string GridCellClass = "bp-grid-cell";
        const float DepthIndent = 14f;

        // Sub-asset arrows only for items that can hold sub-assets: skip plain text-like files without loading them.
        static readonly HashSet<string> NoSubAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".txt", ".json", ".md", ".xml", ".shader", ".asmdef", ".asmref", ".uss", ".uxml", ".mat" };

        readonly ListView _list;
        List<ContentItem> _items = new List<ContentItem>();

        // The ListView's rows: one row per item in list mode, one row per `Columns` items in grid mode.
        readonly List<int> _rowSource = new List<int>();
        float _iconSize;              // 0 = list
        int _columns = 1;
        float _builtIconSize = -1;
        float _builtRowHeight = RowHeight;
        double _lastRebuild;
        bool _rebuildQueued;
        readonly HashSet<string> _expanded = new HashSet<string>();     // guids, per window, not persisted
        readonly Dictionary<string, bool> _hasSub = new Dictionary<string, bool>(); // path -> has sub-assets (lazy)
        readonly HashSet<int> _loadingTiles = new HashSet<int>();       // item indices whose preview is pending
        readonly IVisualElementScheduledItem _previewPoll;

        // Placeholder row for an asset being created (exists only until its name is committed).
        string _pendingPath;
        string _pendingName;
        bool _pendingIsFolder;
        Texture _pendingIcon;

        // Ping (see Ping): a yellow square over the item, shrinking from big to the item's size, then fading out.
        static readonly Color PingColor = new Color(1f, 0.82f, 0.1f, 1f);
        const float PingAlpha = 0.6f;
        const float PingInflate = 48f;    // the start square extends this far around the item on each side
        const double PingShrink = 0.25, PingHold = 0.25, PingFade = 0.6; // seconds
        const int PingMaxWaitTicks = 30;  // ~0.5 s for the scrolled-to item to be laid out
        string _pingPath;
        double _pingStart;                // < 0: waiting for the item's cell
        int _pingWaitTicks;
        VisualElement _pingOverlay;
        IVisualElementScheduledItem _pingAnim;

        // Slow-click rename tracking.
        string _lastClickPath;
        double _lastClickTime;
        IVisualElementScheduledItem _slowClickRename;
        // True when keyboard focus left the list (or the window lost focus) since the last press in it: the next press
        // only brings focus back and must not arm slow-click rename. Starts true: a fresh view has never had focus.
        bool _focusLostSincePress = true;

        // AssetPreview's cache is editor-global: only ever grow it (shared by all our windows), never shrink it.
        const int MinPreviewCacheSize = 256;
        static int s_PreviewCacheSize;

        /// <summary>0 = list mode; otherwise grid with tiles of this size (values in (0, 32) snap to 32, max 96).</summary>
        public float IconSize
        {
            get => _iconSize;
            set
            {
                float s = Mathf.Round(SnapIconSize(value));
                if (Mathf.Approximately(s, _iconSize) && _builtIconSize >= 0) return;
                _iconSize = s;
                Reconfigure();
            }
        }

        /// <summary>Current grid column count (1 in list mode).</summary>
        public int Columns => _columns;

        public static float SnapIconSize(float v) => v <= 0f ? 0f : v < 32f ? 32f : Mathf.Min(v, 96f);

        float CellWidth => _iconSize + TilePad;
        float CellHeight => _iconSize + 6f + TileLabelHeight;
        float CurrentRowHeight => _iconSize > 0f ? CellHeight : RowHeight;

        public SelectionModel Selection { get; } = new SelectionModel();
        public InlineRename Rename { get; } = new InlineRename();
        public event Action<ContentItem> ItemOpened;
        /// <summary>Context menu requested at a panel-space position (selection already updated).</summary>
        public event Action<Vector2> ContextRequested;
        /// <summary>Keyboard request to go up one folder (Backspace / Cmd+Up).</summary>
        public event Action ParentRequested;

        int _cursorId;   // item the keyboard moves from (last clicked / moved to)

        // Drag out: armed on left pointer-down on a row, starts after moving more than DragThreshold px.
        const float DragThreshold = 5f;
        static readonly Color DropColor = ItemDrag.DropColor;
        int _collapseId;            // plainly pressed item: select it (only it) on pointer-up unless a drag starts
        int _dropIndex = -1;        // item index of the highlighted drop cell
        int _dragIndex = -1;
        int _dragPointer = -1;
        Vector2 _dragStart;
        VisualElement _dragCell;
        VisualElement _dropCell;    // folder cell currently highlighted as a drop target
        bool _dropOnView;
        readonly VisualElement _dropFrame;   // drop-on-view outline: an overlay, so it does not shift the content

        Label _emptyMessage;

        /// <summary>Text shown in the middle of the panel when it is empty (null = none).</summary>
        public string EmptyMessage
        {
            get => _emptyMessage?.text;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (_emptyMessage != null) _emptyMessage.style.display = DisplayStyle.None;
                    return;
                }
                if (_emptyMessage == null)
                {
                    _emptyMessage = new Label { name = "empty-message", pickingMode = PickingMode.Ignore };
                    _emptyMessage.style.position = Position.Absolute;
                    _emptyMessage.style.left = 0;
                    _emptyMessage.style.right = 0;
                    _emptyMessage.style.top = 0;
                    _emptyMessage.style.bottom = 0;
                    _emptyMessage.style.unityTextAlign = TextAnchor.MiddleCenter;
                    _emptyMessage.style.color = Theme.DimText;
                    Add(_emptyMessage);
                }
                _emptyMessage.text = value;
                _emptyMessage.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>Folder that receives drops on empty space (the folder being shown).</summary>
        public string DropFolder { get; set; }

        public ContentView()
        {
            style.flexGrow = 1;
            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _rowSource,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
            };
            _list.style.flexGrow = 1;
            Add(_list);
            _dropFrame = new VisualElement { name = "drop-frame", pickingMode = PickingMode.Ignore };
            _dropFrame.style.position = Position.Absolute;
            _dropFrame.style.left = _dropFrame.style.right = _dropFrame.style.top = _dropFrame.style.bottom = 0;
            _dropFrame.style.borderTopWidth = _dropFrame.style.borderBottomWidth =
                _dropFrame.style.borderLeftWidth = _dropFrame.style.borderRightWidth = 2;
            _dropFrame.style.borderTopColor = _dropFrame.style.borderBottomColor =
                _dropFrame.style.borderLeftColor = _dropFrame.style.borderRightColor = DropColor;
            _dropFrame.style.display = DisplayStyle.None;
            Add(_dropFrame);
            // Focus moving out of the list (to another element, or nowhere when the window loses focus). Moves inside
            // it (the rename field) do not count.
            _list.RegisterCallback<FocusOutEvent>(e =>
            {
                if (!(e.relatedTarget is VisualElement to) || !IsInList(to)) _focusLostSincePress = true;
            });
            _list.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (!Mathf.Approximately(e.newRect.width, e.oldRect.width)) Reconfigure();
                UpdatePreviewCache();
            });
            _previewPoll = schedule.Execute(PollPreviews).Every(100);
            _previewPoll.Pause();

            // Right click on empty space: clear selection, then context menu. Rows stop propagation for their own clicks.
            RegisterCallback<PointerDownEvent>(e => { if (IsContextClick(e, true)) OnEmptyContextClick(e); });
            RegisterCallback<PointerUpEvent>(e => { if (IsContextClick(e, false)) OnEmptyContextClick(e); });
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(OnBackgroundPointerDown);
            // Drops are handled here, before they reach the ListView (its own drag handling would reject them): the
            // folder cell under the mouse, otherwise the folder being shown.
            RegisterCallback<DragUpdatedEvent>(e => OnDragOver(e, CellOf(e.target)), TrickleDown.TrickleDown);
            RegisterCallback<DragPerformEvent>(e => OnDragDrop(e, CellOf(e.target)), TrickleDown.TrickleDown);
            RegisterCallback<DragLeaveEvent>(e => ClearDropHighlight());
            RegisterCallback<DragExitedEvent>(e => ClearDropHighlight());
            Rename.Ended += () =>
            {
                if (_pendingPath != null) ClearPending();
                // Enter / Esc leave nothing focused: give keys back to the list. If focus moved elsewhere, leave it.
                if (focusController == null || focusController.focusedElement == null) _list.Focus();
            };
        }

        // macOS opens context menus on mouse down, Windows/Linux on mouse up (as ContextualMenuManipulator does).
        internal static bool IsContextClick(IPointerEvent e, bool down)
        {
            bool mac = Application.platform == RuntimePlatform.OSXEditor;
            if (mac != down) return false;
            return e.button == 1 || (mac && e.button == 0 && e.ctrlKey && !e.commandKey);
        }

        public IReadOnlyList<ContentItem> Items => _items;

        /// <summary>The owning window lost focus: the next press only re-focuses (no slow-click rename).</summary>
        public void NotifyFocusLost() => _focusLostSincePress = true;

        bool IsInList(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e == _list) return true;
            return false;
        }

        public void Show(IReadOnlyList<ContentItem> items)
        {
            // Carry resolved ids over only for the same asset: same path AND same guid (a file replaced at the same
            // path is a different asset with a different id).
            var known = new Dictionary<string, (string guid, int id)>();
            foreach (var old in _items)
                if (old.InstanceId != 0 && !old.IsSubAsset && !string.IsNullOrEmpty(old.Guid)) known[old.Path] = (old.Guid, old.InstanceId);
            _loadingTiles.Clear();
            _items = new List<ContentItem>(items);
            for (int i = 0; i < _items.Count; i++)
                if (known.TryGetValue(_items[i].Path, out var k) && k.guid == _items[i].Guid)
                {
                    int kid = k.id;
                    var it = _items[i];
                    it.InstanceId = kid;
                    _items[i] = it;
                }
            ApplyExpansion();
            if (_pendingPath != null) InsertPending();
            if (Rename.IsActive && IndexOfPath(Rename.Path) < 0) Rename.Cancel();
            var resolved = new List<int>();
            foreach (var it in _items) if (it.InstanceId != 0) resolved.Add(it.InstanceId);
            Selection.Retain(resolved);
            RefreshAll();
            MoveDebug.Log("ContentView.Show: items=" + _items.Count + " rows=" + _rowSource.Count + " columns=" + _columns
                          + " listSource=" + (_list.itemsSource?.Count ?? -1) + " panel=" + (panel != null));
        }

        /// <summary>Mirror Unity's current selection into this view's highlight state.</summary>
        public void SyncFromUnitySelection()
        {
            var ids = UnityEditor.Selection.instanceIDs;
            Selection.Set(ids);
            if (_cursorId != 0 && !ContainsId(_cursorId)) _cursorId = 0;
            if (ids.Length > 0 && ids.Length <= 256)
            {
                var byPath = new Dictionary<string, int>();
                for (int i = 0; i < _items.Count; i++) if (!_items[i].IsSubAsset) byPath[_items[i].Path] = i;
                foreach (int id in ids)
                {
                    if (!AssetDatabase.IsMainAsset(id)) continue;
                    if (byPath.TryGetValue(AssetDatabase.GetAssetPath(id), out int idx))
                    {
                        var it = _items[idx];
                        it.InstanceId = id;
                        _items[idx] = it;
                    }
                }
            }
            RefreshAll();
        }

        // ---- rows ----

        VisualElement MakeRow()
        {
            var row = new VisualElement { name = "bp-row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = CurrentRowHeight;
            int cells = _iconSize > 0f ? _columns : 1;
            for (int i = 0; i < cells; i++) row.Add(MakeCell());
            return row;
        }

        VisualElement MakeCell()
        {
            bool grid = _iconSize > 0f;
            var cell = new VisualElement { name = "cell" };
            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit };
            var label = new Label { name = "label" };
            var arrow = new Label { name = "arrow", text = ">" };
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            arrow.style.width = ArrowWidth;
            arrow.style.marginLeft = arrow.style.marginRight = arrow.style.marginTop = arrow.style.marginBottom = 0;
            arrow.style.paddingLeft = arrow.style.paddingRight = arrow.style.paddingTop = arrow.style.paddingBottom = 0;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.RegisterCallback<PointerDownEvent>(OnArrowPointerDown);
            if (!grid)
            {
                cell.style.flexDirection = FlexDirection.Row;
                cell.style.alignItems = Align.Center;
                cell.style.flexGrow = 1;
                cell.style.height = RowHeight;
                icon.style.width = 16;
                icon.style.height = 16;
                icon.style.marginRight = 4;
                icon.style.flexShrink = 0; // a long name must not squeeze the icon
                label.style.whiteSpace = WhiteSpace.NoWrap;
                arrow.style.height = RowHeight;
                cell.Add(arrow);
                cell.Add(icon);
                cell.Add(label);
            }
            else
            {
                cell.style.flexDirection = FlexDirection.Column;
                cell.AddToClassList(GridCellClass); // hover per tile (BetterProject.uss)
                cell.style.alignItems = Align.Stretch;
                cell.style.width = CellWidth;
                cell.style.height = CellHeight;
                cell.style.flexShrink = 0;
                cell.style.paddingTop = 2;
                cell.style.paddingLeft = cell.style.paddingRight = 2;
                cell.style.borderTopLeftRadius = cell.style.borderTopRightRadius =
                    cell.style.borderBottomLeftRadius = cell.style.borderBottomRightRadius = 3;
                icon.style.width = _iconSize;
                icon.style.height = _iconSize;
                icon.style.alignSelf = Align.Center;
                icon.style.marginBottom = 2;
                icon.style.flexShrink = 0; // a long name must not squeeze the thumbnail
                label.style.fontSize = 10;
                label.style.unityTextAlign = TextAnchor.UpperCenter;
                label.style.whiteSpace = WhiteSpace.Normal; // wraps to 2 lines; the fixed height clips the rest
                label.style.height = TileLabelHeight;
                label.style.flexShrink = 0;
                arrow.style.position = Position.Absolute;
                arrow.style.right = 2;
                arrow.style.top = 2 + _iconSize * 0.5f - 8;
                arrow.style.height = 16;
                cell.Add(icon);
                cell.Add(label);
                cell.Add(arrow);
            }
            cell.RegisterCallback<PointerDownEvent>(OnRowPointerDown);
            cell.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            cell.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            cell.RegisterCallback<PointerCaptureOutEvent>(e => { if (_dragCell == e.currentTarget) DisarmDrag(false); });
            cell.RegisterCallback<PointerCancelEvent>(e => DisarmDrag(true));
            cell.RegisterCallback<DragLeaveEvent>(e => { if (_dropCell == e.currentTarget) ClearDropHighlight(); });
            return cell;
        }

        void BindRow(VisualElement row, int rowIndex)
        {
            // A grid row holds several tiles: the ListView's hover would light up the whole row, so it is cleared
            // (inline colour wins over the theme's :hover) and each tile highlights itself instead.
            var itemRoot = row.ClassListContains(BaseVerticalCollectionView.itemUssClassName) ? row : row.parent;
            if (itemRoot != null && itemRoot != _list)
                itemRoot.style.backgroundColor = _iconSize > 0f ? new StyleColor(Color.clear) : new StyleColor(StyleKeyword.Null);
            int first = rowIndex * _columns;
            for (int c = 0; c < row.childCount; c++)
            {
                var cell = row[c];
                int index = first + c;
                if (index < _items.Count)
                {
                    cell.style.display = DisplayStyle.Flex;
                    BindCell(cell, index);
                }
                else
                {
                    cell.userData = -1;
                    cell.style.display = DisplayStyle.None;
                    Rename.Detach(cell);
                }
            }
        }

        void BindCell(VisualElement cell, int index)
        {
            cell.userData = index;
            bool grid = _iconSize > 0f;
            var item = _items[index];
            bool pending = IsPending(item);
            if (!pending && !item.IsFolder && !item.IsSubAsset)
            {
                if (grid && item.InstanceId == 0) { ResolveId(index); item = _items[index]; }
                item = EnsureHasSubAssets(index, item);
            }

            Texture tex;
            if (pending) tex = _pendingIcon;
            else if (grid)
            {
                tex = Thumbnails.Get(item, out bool loading);
                if (loading) { _loadingTiles.Add(index); _previewPoll.Resume(); }
            }
            else tex = Thumbnails.Icon(item);
            cell.Q<Image>("icon").image = tex;
            cell.Q<Label>("label").text = item.Name;

            var arrow = cell.Q<Label>("arrow");
            bool expandable = item.HasSubAssets && !pending;
            arrow.style.visibility = expandable ? Visibility.Visible : Visibility.Hidden;
            arrow.style.rotate = new Rotate(new Angle(expandable && _expanded.Contains(item.Guid) ? 90f : 0f, AngleUnit.Degree));
            if (!grid)
            {
                // "Remove offset in right panel": no left padding and no reserved arrow column, so icons sit at the
                // left edge; the sub-asset arrow moves to the row's right end (absolute: takes no space).
                bool flush = BetterProjectSettings.instance.RemoveRightPanelOffset;
                cell.style.paddingLeft = (flush ? 0 : 4) + item.Depth * DepthIndent;
                arrow.style.position = flush ? Position.Absolute : Position.Relative;
                arrow.style.right = flush ? new StyleLength(4f) : new StyleLength(StyleKeyword.Null);
                arrow.style.top = flush ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
                var label = cell.Q<Label>("label");
                label.style.marginRight = flush && expandable ? ArrowWidth + 4 : 0;
            }

            bool selected = !pending && item.InstanceId != 0 && ContainsId(item.InstanceId);
            // Not highlighted: no inline colour, so the hover highlight (grid tiles: BetterProject.uss) shows.
            cell.style.backgroundColor = _dropCell != null && index == _dropIndex ? new StyleColor(DropColor)
                : selected ? new StyleColor(SelectedColor) : new StyleColor(StyleKeyword.Null);
            if (Rename.IsActive && Rename.Path == item.Path && !item.IsSubAsset) Rename.Attach(cell);
            else Rename.Detach(cell);
        }

        // ---- sub-assets ----

        // Lazy: LoadAllAssetRepresentationsAtPath loads the asset, so only bound (visible) items ask, once per path.
        ContentItem EnsureHasSubAssets(int index, ContentItem item)
        {
            if (!_hasSub.TryGetValue(item.Path, out bool has))
            {
                has = !NoSubAssetExtensions.Contains(Path.GetExtension(item.Path)) && PathHasChildren(item.Path);
                _hasSub[item.Path] = has;
            }
            if (item.HasSubAssets != has)
            {
                item.HasSubAssets = has;
                _items[index] = item;
            }
            return item;
        }

        // Same source as the default window's foldout arrow: HierarchyProperty.hasChildren, which does not load the asset.
        static HierarchyProperty _hierarchy;

        static bool PathHasChildren(string path)
        {
            int id = Internal.UnityInternals.GetMainAssetInstanceID(path);
            if (id == 0) return false;
            if (_hierarchy == null) _hierarchy = new HierarchyProperty(HierarchyType.Assets);
            return _hierarchy.Find(id, null) && _hierarchy.hasChildren;
        }

        /// <summary>Forget cached sub-asset presence (call when the project changed / assets were reimported).</summary>
        public void InvalidateSubAssetCache()
        {
            _hasSub.Clear();
            _hierarchy = null;
        }

        static List<ContentItem> LoadSubAssets(ContentItem parent)
        {
            var result = new List<ContentItem>();
            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(parent.Path))
            {
                if (obj == null) continue;
                result.Add(new ContentItem
                {
                    Path = parent.Path,
                    Guid = parent.Guid,
                    InstanceId = obj.GetInstanceID(),
                    IsSubAsset = true,
                    Depth = 1,
                    Name = obj.name,
                });
            }
            return result;
        }

        void ApplyExpansion()
        {
            if (_expanded.Count == 0) return;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.IsFolder || it.IsSubAsset || it.Guid == null || !_expanded.Contains(it.Guid)) continue;
                var subs = LoadSubAssets(it);
                if (subs.Count == 0) { _expanded.Remove(it.Guid); continue; }
                _items.InsertRange(i + 1, subs);
                i += subs.Count;
            }
        }

        void OnArrowPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.ctrlKey || evt.commandKey) return; // right / ctrl click: row context menu
            evt.StopPropagation();
            if (((VisualElement)evt.currentTarget).parent?.userData is int index && index >= 0 && index < _items.Count)
                ToggleExpand(index);
        }

        void ToggleExpand(int index)
        {
            var item = _items[index];
            if (item.Guid == null || item.IsFolder || item.IsSubAsset || !item.HasSubAssets) return;
            if (_expanded.Remove(item.Guid))
            {
                int n = 0;
                while (index + 1 + n < _items.Count && _items[index + 1 + n].IsSubAsset) n++;
                _items.RemoveRange(index + 1, n);
                // Hidden sub-assets must not stay selected.
                var visibleIds = new List<int>();
                foreach (var it in _items) if (it.InstanceId != 0) visibleIds.Add(it.InstanceId);
                int before = Selection.Selected.Count;
                Selection.Retain(visibleIds);
                if (Selection.Selected.Count != before)
                    UnityEditor.Selection.instanceIDs = new List<int>(Selection.Selected).ToArray();
            }
            else
            {
                _expanded.Add(item.Guid);
                _items.InsertRange(index + 1, LoadSubAssets(item));
            }
            RefreshAll();
        }

        // ---- grid layout / thumbnails ----

        void Reconfigure()
        {
            float width = _list.layout.width;
            int cols = _iconSize > 0f && !float.IsNaN(width) && width > 0f
                ? Math.Max(1, Mathf.FloorToInt((width - ScrollbarAllowance) / CellWidth))
                : 1;
            if (cols == _columns && Mathf.Approximately(_builtIconSize, _iconSize)) return;

            // Throttle rebuilds (slider drags, window resizes): at most one per 50 ms, the last state always wins.
            double now = EditorApplication.timeSinceStartup;
            if (_builtIconSize >= 0 && now - _lastRebuild < 0.05)
            {
                if (!_rebuildQueued)
                {
                    _rebuildQueued = true;
                    schedule.Execute(() => { _rebuildQueued = false; Reconfigure(); }).ExecuteLater(50);
                }
                return;
            }
            _lastRebuild = now;

            // Keep the first visible item in view across the re-layout.
            int firstItem = (int)(_list.Q<ScrollView>().scrollOffset.y / Mathf.Max(1f, _builtRowHeight)) * _columns;
            _columns = cols;
            _builtIconSize = _iconSize;
            _builtRowHeight = CurrentRowHeight;
            _loadingTiles.Clear();
            _list.fixedItemHeight = CurrentRowHeight;
            SyncRowSource();
            _list.Rebuild();
            UpdatePreviewCache();
            float y = firstItem / _columns * CurrentRowHeight;
            if (y > 0f) _list.schedule.Execute(() => _list.Q<ScrollView>().scrollOffset = new Vector2(0f, y));
        }

        void SyncRowSource()
        {
            int rows = (_items.Count + _columns - 1) / _columns;
            while (_rowSource.Count < rows) _rowSource.Add(_rowSource.Count);
            if (_rowSource.Count > rows) _rowSource.RemoveRange(rows, _rowSource.Count - rows);
        }

        /// <summary>Re-binds every visible item (e.g. after a display setting changed).</summary>
        public void Refresh() => RefreshAll();

        void RefreshAll()
        {
            SyncRowSource();
            _list.RefreshItems();
        }

        int RowOf(int itemIndex) => itemIndex / _columns;

        /// <summary>The element currently showing the item (null when not bound / scrolled out).</summary>
        VisualElement CellFor(int itemIndex)
        {
            var wrapper = _list.GetRootElementForIndex(RowOf(itemIndex));
            if (wrapper == null) return null;
            var row = wrapper.name == "bp-row" ? wrapper : wrapper.Q("bp-row");
            int c = itemIndex % _columns;
            return row != null && c < row.childCount ? row[c] : null;
        }

        void UpdatePreviewCache()
        {
            if (_iconSize <= 0f) return;
            float h = _list.layout.height;
            if (float.IsNaN(h) || h <= 0f) return;
            int visible = (Mathf.CeilToInt(h / CellHeight) + 2) * _columns;
            int want = Math.Max(MinPreviewCacheSize, visible + 64);
            if (want <= s_PreviewCacheSize) return;
            s_PreviewCacheSize = want;
            AssetPreview.SetPreviewTextureCacheSize(want);
        }

        // While tiles wait for their preview: re-ask every 100 ms until none is loading.
        void PollPreviews()
        {
            if (_loadingTiles.Count == 0) { _previewPoll.Pause(); return; }
            var pending = new List<int>(_loadingTiles);
            _loadingTiles.Clear();
            foreach (int index in pending)
            {
                if (index >= _items.Count) continue;
                var cell = CellFor(index);
                if (cell == null || !(cell.userData is int bound) || bound != index) continue;
                var tex = Thumbnails.Get(_items[index], out bool loading);
                cell.Q<Image>("icon").image = tex;
                if (loading) _loadingTiles.Add(index);
            }
            if (_loadingTiles.Count == 0) _previewPoll.Pause();
        }

        bool IsPending(ContentItem item) => _pendingPath != null && item.Guid == null && item.Path == _pendingPath;

        bool ContainsId(int id)
        {
            var sel = Selection.Selected;
            for (int i = 0; i < sel.Count; i++) if (sel[i] == id) return true;
            return false;
        }

        int ResolveId(int index)
        {
            var it = _items[index];
            if (it.InstanceId == 0)
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(it.Path);
                it.InstanceId = obj != null ? obj.GetInstanceID() : 0;
                _items[index] = it;
            }
            return it.InstanceId;
        }

        void OnRowPointerDown(PointerDownEvent evt)
        {
            var row = (VisualElement)evt.currentTarget;
            if (evt.target is VisualElement t && Rename.Contains(t)) return; // clicks inside the rename field
            _slowClickRename?.Pause();
            DisarmDrag(true); // never keep a stale capture from an earlier press
            if (!(row.userData is int index) || index < 0 || index >= _items.Count) return;
            if (IsPending(_items[index])) { evt.StopPropagation(); return; }

            if (IsContextClick(evt, true)) { evt.StopPropagation(); OnRowContextClick(index, evt.position); return; }
            if (evt.button != 0) { if (evt.button == 1) evt.StopPropagation(); return; }
            // Whether the list already had keyboard focus before this press (must be read before focusing it).
            bool hadFocus = !_focusLostSincePress && focusController != null
                            && focusController.focusedElement is VisualElement fe && IsInList(fe);
            _list.Focus();
            _focusLostSincePress = false;

            if (evt.clickCount >= 2)
            {
                ItemOpened?.Invoke(_items[index]);
                return;
            }

            int id = ResolveId(index);
            if (id == 0) return;
            ArmDrag(row, index, evt);
            // "Don't select folders in right pane": a folder can be dragged and double-click opened, never selected.
            if (_items[index].IsFolder && !_items[index].IsSubAsset && BetterProjectSettings.instance.DontSelectFoldersInRightPane)
                return;

            // Slow click: plain click on the single selected item, not the click that selected it, not a double click,
            // and not the click that brings focus back to the list / window (e.g. after editing in the Inspector).
            string path = _items[index].Path;
            double now = EditorApplication.timeSinceStartup;
            bool slow = hadFocus && !_items[index].IsSubAsset && !evt.shiftKey && !evt.actionKey && Selection.Selected.Count == 1 && Selection.Selected[0] == id
                        && !(path == _lastClickPath && (now - _lastClickTime) * 1000.0 <= SlowClickMs);
            _lastClickPath = path;
            _lastClickTime = now;
            if (slow)
            {
                _slowClickRename = schedule.Execute(() =>
                {
                    if (Selection.Selected.Count == 1 && Selection.Selected[0] == id) BeginRename(path);
                });
                _slowClickRename.ExecuteLater(SlowClickMs);
                return;
            }
            // Ctrl/Cmd and Shift clicks act on press.
            if (evt.actionKey || evt.shiftKey) { SelectIndex(index, evt.actionKey, evt.shiftKey); return; }
            // A plain press selects on release, and only if no drag started: dragging an unselected item (e.g. into an
            // Inspector field) leaves the selection, and so the Inspector, as it was. Pressing an item of a
            // multi-selection keeps the selection, so all of it can be dragged.
            _collapseId = id;
        }

        // ---- drag out / drop ----

        void ArmDrag(VisualElement cell, int index, PointerDownEvent evt)
        {
            if (Rename.IsActive) return;
            _collapseId = 0;
            _dragIndex = index;
            _dragCell = cell;
            _dragPointer = evt.pointerId;
            _dragStart = evt.position;
            cell.CapturePointer(evt.pointerId);
        }

        void DisarmDrag(bool release)
        {
            var cell = _dragCell;
            int pointer = _dragPointer;
            _collapseId = 0;
            _dragIndex = -1;
            _dragCell = null;
            _dragPointer = -1;
            if (release && cell != null && cell.HasPointerCapture(pointer)) cell.ReleasePointer(pointer);
        }

        void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (_dragIndex < 0 || evt.pointerId != _dragPointer || (evt.pressedButtons & 1) == 0) return;
            if (((Vector2)evt.position - _dragStart).magnitude <= DragThreshold) return;
            int index = _dragIndex;
            DisarmDrag(true);
            if (Rename.IsActive || index >= _items.Count || IsPending(_items[index])) return;
            _slowClickRename?.Pause();

            // Drag the selection if the pressed item is part of it, otherwise just the pressed item.
            var items = new List<ContentItem>();
            bool pressedSelected = _items[index].InstanceId != 0 && ContainsId(_items[index].InstanceId);
            if (pressedSelected)
            {
                foreach (var it in _items)
                    if (it.Guid != null && it.InstanceId != 0 && ContainsId(it.InstanceId)) items.Add(it);
            }
            else items.Add(_items[index]);
            evt.StopPropagation();
            ItemDrag.StartDrag(items);
        }

        // The grid / list cell containing the event target, if any.
        VisualElement CellOf(IEventHandler target)
        {
            for (var e = target as VisualElement; e != null && e != this; e = e.parent)
                if (e.name == "cell") return e;
            return null;
        }

        // A folder cell is the target itself; anything else (non-folder cell, empty space) means the folder being shown
        // (null with several folders or results shown: then the drop is rejected). `cell` is set to null unless the
        // drop goes into that cell's folder.
        string DropTargetFor(ref VisualElement cell)
        {
            if (cell != null && cell.userData is int i && i >= 0 && i < _items.Count)
            {
                var it = _items[i];
                if (it.IsFolder && !it.IsSubAsset && !IsPending(it)) return it.Path;
            }
            cell = null;
            return DropFolder;
        }

        void OnDragOver(DragUpdatedEvent evt, VisualElement cell)
        {
            string target = DropTargetFor(ref cell);
            evt.StopPropagation();
            bool ok = target != null && ItemDrag.HandleDropOnFolder(target, false);
            if (target == null) DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            SetDropHighlight(ok ? cell : null, ok && cell == null);
        }

        void OnDragDrop(DragPerformEvent evt, VisualElement cell)
        {
            string target = DropTargetFor(ref cell);
            evt.StopPropagation();
            ClearDropHighlight();
            if (target != null) ItemDrag.HandleDropOnFolder(target, true);
        }

        void SetDropHighlight(VisualElement cell, bool onView)
        {
            int idx = cell != null && cell.userData is int ci ? ci : -1;
            if (_dropCell != cell || _dropIndex != idx)
            {
                var old = _dropCell;
                _dropCell = cell;
                _dropIndex = idx;
                if (old != null) RestoreCellBackground(old);
                if (cell != null) cell.style.backgroundColor = DropColor;
            }
            if (_dropOnView != onView)
            {
                _dropOnView = onView;
                _dropFrame.style.display = onView ? DisplayStyle.Flex : DisplayStyle.None;
                _dropFrame.BringToFront();
            }
        }

        void ClearDropHighlight() => SetDropHighlight(null, false);

        void RestoreCellBackground(VisualElement cell)
        {
            bool selected = false;
            if (cell.userData is int i && i >= 0 && i < _items.Count)
            {
                var it = _items[i];
                selected = !IsPending(it) && it.InstanceId != 0 && ContainsId(it.InstanceId);
            }
            cell.style.backgroundColor = selected ? new StyleColor(SelectedColor) : new StyleColor(StyleKeyword.Null);
        }

        // Selects an item by index (mouse or keyboard) and pushes the result to Unity's selection.
        // Range selection resolves only the ids between the anchor and the target.
        void SelectIndex(int index, bool toggle, bool range)
        {
            int id = ResolveId(index);
            if (id == 0) return;
            List<int> order = null;
            if (range)
            {
                int anchorIdx = -1;
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].InstanceId != 0 && _items[i].InstanceId == Selection.Anchor) { anchorIdx = i; break; }
                if (anchorIdx >= 0)
                {
                    order = new List<int>();
                    int lo = Math.Min(anchorIdx, index), hi = Math.Max(anchorIdx, index);
                    for (int i = lo; i <= hi; i++) order.Add(ResolveId(i));
                    if (anchorIdx > index) order.Reverse();
                }
            }
            Selection.Click(id, toggle, range, order);
            _cursorId = id;
            UnityEditor.Selection.instanceIDs = new List<int>(Selection.Selected).ToArray();
            RefreshAll();
        }

        // Left click on empty space (below the rows, right of the last grid cell) clears the selection.
        void OnBackgroundPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || IsContextClick(evt, true)) return;
            if (!(evt.target is VisualElement t) || Rename.Contains(t)) return;
            for (var e = t; e != null && e != this; e = e.parent)
            {
                if (e.name == "cell" || e is Scroller || e is Slider) return;
            }
            _slowClickRename?.Pause();
            _list.Focus();
            _focusLostSincePress = false;
            if (evt.shiftKey || evt.actionKey) return;
            if (Rename.IsActive) Rename.Commit();
            if (Selection.Selected.Count > 0)
            {
                Selection.Set(Array.Empty<int>());
                UnityEditor.Selection.instanceIDs = Array.Empty<int>();
                RefreshAll();
            }
        }

        void OnRowPointerUp(PointerUpEvent evt)
        {
            var row = (VisualElement)evt.currentTarget;
            if (evt.button == 0)
            {
                int collapseId = _collapseId;
                DisarmDrag(true);
                if (collapseId != 0 && !IsContextClick(evt, false))
                {
                    for (int i = 0; i < _items.Count; i++)
                        if (_items[i].InstanceId == collapseId) { SelectIndex(i, false, false); break; }
                }
            }
            if (!IsContextClick(evt, false)) return;
            evt.StopPropagation();
            if (!(row.userData is int index) || index < 0 || index >= _items.Count) return;
            if (IsPending(_items[index])) return;
            OnRowContextClick(index, evt.position);
        }

        void OnRowContextClick(int index, Vector2 position)
        {
            if (Rename.IsActive) Rename.Commit(); // like any other focus loss
            int id = ResolveId(index);
            if (id == 0)
            {
                // The asset cannot be loaded: no command may target whatever was selected before (as for empty space).
                if (Selection.Selected.Count > 0 || UnityEditor.Selection.instanceIDs.Length > 0)
                {
                    Selection.Set(Array.Empty<int>());
                    UnityEditor.Selection.instanceIDs = Array.Empty<int>();
                    RefreshAll();
                }
            }
            else if (!ContainsId(id))
            {
                Selection.Click(id, false, false, null);
                UnityEditor.Selection.instanceIDs = new[] { id };
                RefreshAll();
            }
            else
            {
                // Menu commands act on Unity's selection: drop anything selected that is not visible here.
                var visible = new List<int>();
                var ids = new HashSet<int>(Selection.Selected);
                foreach (var it in _items) if (it.InstanceId != 0 && it.Guid != null && ids.Contains(it.InstanceId)) visible.Add(it.InstanceId);
                if (visible.Count != UnityEditor.Selection.instanceIDs.Length)
                {
                    Selection.Set(visible);
                    UnityEditor.Selection.instanceIDs = visible.ToArray();
                    RefreshAll();
                }
            }
            ContextRequested?.Invoke(position);
        }

        void OnEmptyContextClick(IPointerEvent evt)
        {
            if (Rename.IsActive) Rename.Commit();
            if (Selection.Selected.Count > 0)
            {
                Selection.Set(Array.Empty<int>());
                UnityEditor.Selection.instanceIDs = Array.Empty<int>();
                RefreshAll();
            }
            ContextRequested?.Invoke(evt.position);
        }

        int CursorIndex()
        {
            int fallback = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                int id = _items[i].InstanceId;
                if (id == 0) continue;
                if (_cursorId != 0 && id == _cursorId) return i;
                if (fallback < 0 && (id == Selection.Anchor || ContainsId(id))) fallback = i;
            }
            return fallback;
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (Rename.IsActive || (evt.target is VisualElement tv && Rename.Contains(tv))) return;
            bool mac = Application.platform == RuntimePlatform.OSXEditor;
            bool handled = false;
            var key = evt.keyCode;

            if (key == KeyCode.F2 && !evt.shiftKey && !evt.actionKey && !evt.altKey)
            {
                var paths = SelectedVisiblePaths();
                handled = paths.Count == 1 && BeginRename(paths[0]);
            }
            else if (evt.altKey) return;
            else if (key == KeyCode.UpArrow && evt.actionKey && mac)
            {
                ParentRequested?.Invoke();
                handled = true;
            }
            else if (key == KeyCode.Backspace && !mac && !evt.actionKey && !evt.shiftKey)
            {
                ParentRequested?.Invoke();
                handled = true;
            }
            else if (mac && (key == KeyCode.Return || key == KeyCode.KeypadEnter) && !evt.actionKey && !evt.shiftKey)
            {
                var paths = SelectedVisiblePaths();
                if (paths.Count == 1 && !IsPending(_items[Math.Max(0, IndexOfPath(paths[0]))])) BeginRename(paths[0]);
                handled = true;
            }
            else if ((mac ? (key == KeyCode.DownArrow && evt.actionKey && !evt.shiftKey)
                          : ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !evt.actionKey && !evt.shiftKey)))
            {
                int cur = CursorIndex();
                if (cur >= 0 && !IsPending(_items[cur]))
                {
                    ResolveId(cur);
                    ItemOpened?.Invoke(_items[cur]);
                }
                handled = true;
            }
            else if (!evt.actionKey && _items.Count > 0)
            {
                handled = HandleNavKey(key, evt.shiftKey);
            }
            if (handled)
            {
                evt.StopPropagation();
#pragma warning disable CS0618 // still needed so ListView's Navigation* default actions do not also fire
                evt.PreventDefault();
#pragma warning restore CS0618
            }
        }

        bool HandleNavKey(KeyCode key, bool extend)
        {
            bool grid = _iconSize > 0f;
            NavDir dir;
            switch (key)
            {
                case KeyCode.UpArrow: dir = NavDir.Up; break;
                case KeyCode.DownArrow: dir = NavDir.Down; break;
                case KeyCode.Home: dir = NavDir.Home; break;
                case KeyCode.End: dir = NavDir.End; break;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    if (grid) { dir = key == KeyCode.LeftArrow ? NavDir.Left : NavDir.Right; break; }
                    return HandleListHorizontal(key == KeyCode.RightArrow);
                default: return false;
            }
            int cur = CursorIndex();
            int target = GridNav.Move(cur, _items.Count, _columns, dir);
            if (target < 0 || IsPending(_items[target])) return true;
            SelectIndex(target, false, extend);
            _list.ScrollToItem(RowOf(target));
            return true;
        }

        // List mode: Right expands an item with sub-assets, Left collapses it (or jumps from a sub-asset to its parent).
        bool HandleListHorizontal(bool right)
        {
            int cur = CursorIndex();
            if (cur < 0) return true;
            var item = _items[cur];
            if (item.IsSubAsset)
            {
                if (right) return true;
                for (int i = cur - 1; i >= 0; i--)
                    if (!_items[i].IsSubAsset) { SelectIndex(i, false, false); _list.ScrollToItem(RowOf(i)); break; }
                return true;
            }
            if (!item.HasSubAssets || item.Guid == null) return true;
            if (right != _expanded.Contains(item.Guid)) ToggleExpand(cur);
            return true;
        }

        // ---- rename / create ----

        public int IndexOfPath(string path)
        {
            if (path == null) return -1;
            for (int i = 0; i < _items.Count; i++) if (_items[i].Path == path) return i;
            return -1;
        }

        /// <summary>Paths of selected items that are visible in this view (never acts on unseen selection).</summary>
        public List<string> SelectedVisiblePaths()
        {
            var result = new List<string>();
            if (Selection.Selected.Count == 0) return result;
            var ids = new HashSet<int>(Selection.Selected);
            foreach (var it in _items)
                if (it.InstanceId != 0 && it.Guid != null && !it.IsSubAsset && ids.Contains(it.InstanceId)) result.Add(it.Path);
            return result;
        }

        public void ScrollToPath(string path)
        {
            int i = IndexOfPath(path);
            if (i >= 0) _list.ScrollToItem(RowOf(i));
        }

        /// <summary>Scrolls to a shown item and flashes it, like a ping in Unity's Project window. Selection is unchanged.</summary>
        public void Ping(string path)
        {
            int i = IndexOfPath(path);
            if (i < 0) return;
            _pingPath = path;
            _list.ScrollToItem(RowOf(i));
            if (_pingOverlay == null)
            {
                _pingOverlay = new VisualElement { name = "ping", pickingMode = PickingMode.Ignore };
                _pingOverlay.style.position = Position.Absolute;
                _pingOverlay.style.backgroundColor = PingColor;
                _pingOverlay.style.borderTopLeftRadius = _pingOverlay.style.borderTopRightRadius =
                    _pingOverlay.style.borderBottomLeftRadius = _pingOverlay.style.borderBottomRightRadius = 3;
                Add(_pingOverlay);
            }
            _pingOverlay.BringToFront();
            _pingOverlay.style.display = DisplayStyle.None; // until the scrolled-to cell is laid out
            _pingStart = -1;
            _pingWaitTicks = 0;
            _pingAnim?.Pause();
            _pingAnim = schedule.Execute(StepPing).Every(16);
        }

        void StepPing()
        {
            int i = _pingPath != null ? IndexOfPath(_pingPath) : -1;
            var cell = i >= 0 ? CellFor(i) : null;
            if (cell == null || !(cell.userData is int bound) || bound != i || float.IsNaN(cell.layout.width))
            {
                // Not laid out yet after the scroll: wait a little. Scrolled out / gone mid-animation: stop.
                if (_pingStart >= 0 || ++_pingWaitTicks > PingMaxWaitTicks) StopPing();
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (_pingStart < 0) _pingStart = now;
            double t = now - _pingStart;
            if (t >= PingShrink + PingHold + PingFade) { StopPing(); return; }

            Rect target = this.WorldToLocal(cell.worldBound);
            // Shrink (ease-out) from a big square around the item to the item's rect.
            float k = t < PingShrink ? 1f - (float)(t / PingShrink) : 0f;
            k *= k;
            float side = Mathf.Max(target.width, target.height) + 2 * PingInflate;
            float w = Mathf.Lerp(target.width, side, k), h = Mathf.Lerp(target.height, side, k);
            float alpha = t <= PingShrink + PingHold ? PingAlpha
                : PingAlpha * (1f - (float)((t - PingShrink - PingHold) / PingFade));
            var s = _pingOverlay.style;
            s.left = target.center.x - w / 2;
            s.top = target.center.y - h / 2;
            s.width = w;
            s.height = h;
            s.opacity = alpha;
            s.display = DisplayStyle.Flex;
        }

        void StopPing()
        {
            _pingAnim?.Pause();
            _pingPath = null;
            if (_pingOverlay != null) _pingOverlay.style.display = DisplayStyle.None;
        }

        /// <summary>Starts inline rename of a visible item. Returns false if it is not shown here.</summary>
        public bool BeginRename(string path)
        {
            int index = IndexOfPath(path);
            if (index < 0 || IsPending(_items[index])) return false;
            if (!AssetsContextMenu.IsModifiable(path)) return false;
            _slowClickRename?.Pause();
            _list.ScrollToItem(RowOf(index));
            Rename.Begin(_items[index], CellFor(index));
            _list.RefreshItem(RowOf(index));
            return true;
        }

        /// <summary>
        /// Shows a placeholder row for an asset about to be created in this folder and starts inline rename on it.
        /// <paramref name="onCommit"/> receives the typed name; <paramref name="onCancel"/> runs on Esc / focus-out.
        /// </summary>
        public void BeginPendingCreate(string folder, string defaultName, string extension, Texture icon, bool isFolder,
                                       Action<string> onCommit, Action onCancel)
        {
            if (Rename.IsActive) Rename.Commit();
            ClearPending();
            _pendingPath = folder + "/" + defaultName + (extension ?? "");
            _pendingIcon = icon;
            _pendingIsFolder = isFolder;
            _pendingName = defaultName;
            InsertPending();
            RefreshAll();
            int index = IndexOfPending();
            if (index < 0) { onCancel?.Invoke(); ClearPending(); return; }
            _list.ScrollToItem(RowOf(index));
            // Drop the placeholder before the real asset appears, so the list never shows both.
            Rename.Begin(_items[index], CellFor(index),
                name => { ClearPending(); onCommit?.Invoke(name); },
                () => { ClearPending(); onCancel?.Invoke(); });
            _list.RefreshItem(RowOf(index));
        }

        int IndexOfPending()
        {
            for (int i = 0; i < _items.Count; i++) if (IsPending(_items[i])) return i;
            return -1;
        }

        void InsertPending()
        {
            int existing = IndexOfPending();
            if (existing >= 0) _items.RemoveAt(existing);
            var item = new ContentItem { Path = _pendingPath, Guid = null, IsFolder = _pendingIsFolder, Name = _pendingName };
            string file = Path.GetFileName(_pendingPath);
            int at = _items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (_pendingIsFolder)
                {
                    if (!it.IsFolder || EditorUtility.NaturalCompare(_pendingName, it.Name) < 0) { at = i; break; }
                }
                else if (!it.IsFolder && EditorUtility.NaturalCompare(file, Path.GetFileName(it.Path)) < 0) { at = i; break; }
            }
            _items.Insert(at, item);
        }

        void ClearPending()
        {
            if (_pendingPath == null) return;
            int existing = IndexOfPending();
            if (existing >= 0) _items.RemoveAt(existing);
            _pendingPath = null;
            _pendingIcon = null;
            RefreshAll();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDoubleClickTime();

        static long SystemDoubleClickMs()
        {
            // Windows exposes it; macOS/Linux defaults are <= 500 ms (the floor used above).
            if (Application.platform != RuntimePlatform.WindowsEditor) return 0;
            try { return GetDoubleClickTime(); }
            catch (Exception) { return 0; }
        }

        // ---- data ----

        public static List<ContentItem> ListFolder(string folderPath)
        {
            var result = new List<ContentItem>();
            if (folderPath == "Packages")
            {
                foreach (var p in PackageFolders.All())
                    result.Add(MakeFolderItem(p.Path, p.DisplayName));
                return result;
            }
            if (!AssetDatabase.IsValidFolder(folderPath)) return result;

            var folders = new List<ContentItem>();
            foreach (var sub in AssetDatabase.GetSubFolders(folderPath))
                folders.Add(MakeFolderItem(sub, sub.Substring(sub.LastIndexOf('/') + 1)));
            folders.Sort((a, b) => EditorUtility.NaturalCompare(a.Name, b.Name));
            result.AddRange(folders);

            var assets = new List<(string file, ContentItem item)>();
            string disk = ResolveDiskPath(folderPath);
            if (disk != null && Directory.Exists(disk))
            {
                foreach (var file in Directory.GetFiles(disk))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    if (fileName.StartsWith(".", StringComparison.Ordinal) || fileName.EndsWith("~", StringComparison.Ordinal)) continue;
                    string assetPath = folderPath + "/" + fileName;
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(guid)) continue;
                    assets.Add((fileName, new ContentItem
                    {
                        Path = assetPath,
                        Guid = guid,
                        IsFolder = false,
                        Name = Path.GetFileNameWithoutExtension(fileName),
                    }));
                }
            }
            assets.Sort((a, b) => EditorUtility.NaturalCompare(a.file, b.file));
            foreach (var a in assets) result.Add(a.item);
            return result;
        }

        static ContentItem MakeFolderItem(string path, string name) => new ContentItem
        {
            Path = path,
            Guid = AssetDatabase.AssetPathToGUID(path),
            IsFolder = true,
            Name = name,
        };

        static string ResolveDiskPath(string assetFolder)
        {
            if (!assetFolder.StartsWith("Packages/", StringComparison.Ordinal)) return assetFolder;
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetFolder);
            if (pkg == null) return null;
            string prefix = "Packages/" + pkg.name;
            return pkg.resolvedPath + assetFolder.Substring(prefix.Length);
        }
    }
}
