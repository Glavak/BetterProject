using System;
using System.Collections.Generic;
using System.IO;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BetterProject.Editor
{
    /// <summary>One top-level entry of a section: a reference plus the name stored for it (shown when it is missing).</summary>
    public readonly struct RefSectionEntry
    {
        public readonly AssetRef Ref;
        public readonly string StoredName;

        public RefSectionEntry(AssetRef r, string storedName)
        {
            Ref = r;
            StoredName = storedName ?? "";
        }
    }

    /// <summary>
    /// Collapsible left-panel section listing asset references (Favourites, Recents). Entries show live names; missing
    /// ones show the stored name greyed. Folder entries expand lazily to their subfolders and assets.
    /// Click selects the asset or folder for the Inspector (the right panel is not changed) and highlights only that row;
    /// double click opens (folders: in the right panel). Dragging works like any item.
    /// The section is a drop target only through <see cref="HandleDrop"/>; it never moves assets.
    /// </summary>
    public class AssetRefSection : VisualElement
    {
        const float DragThreshold = 5f;
        const float RowHeight = Theme.RowHeight;
        static Color SelectedColor => ItemDrag.SelectedColor;

        // Rows reuse the TreeView's own classes so the header looks like the folder tree's "Assets" / "Packages" roots
        // and entries like its children: same arrow, same indent, same alignment.
        const string TreeItemClass = "unity-tree-view__item";
        const string TreeToggleClass = "unity-tree-view__item-toggle";
        const string TreeIndentClass = "unity-tree-view__item-indent";
        const string TreeContentClass = "unity-tree-view__item-content";
        float _indentWidth = 15f; // one tree level; measured from the arrow like TreeView does
        static readonly Color MissingColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        sealed class Row
        {
            public RefSectionEntry? Entry; // top-level rows only
            public string Path;
            public string Guid;
            public string Name;
            public bool IsFolder;
            public bool IsSubAsset;
            public bool Missing;
            public int InstanceId;
            public int Depth;
            public VisualElement Element;
        }

        readonly Func<IReadOnlyList<RefSectionEntry>> _entries;
        readonly List<string> _expandedList;
        readonly HashSet<string> _expanded;
        readonly Dictionary<AssetRef, int> _subAssetIds = new Dictionary<AssetRef, int>();
        readonly List<Row> _rows = new List<Row>();
        readonly VisualElement _header;
        readonly Toggle _toggle;
        readonly Image _warning;
        readonly VisualElement _body;
        bool _collapsed;

        Row _dragRow;
        VisualElement _dragElement;
        int _dragPointer = -1;
        Vector2 _dragStart;

        /// <summary>A folder row was double-clicked: open it in the right panel.</summary>
        public event Action<string> FolderOpened;

        /// <summary>A row of this section was clicked (other panels drop their own section highlight).</summary>
        public event Action RowSelected;

        // Own selection, separate from Unity's: only the row clicked here is highlighted, never rows that merely show
        // an object selected elsewhere. Kept while Unity's selection is still what this click set.
        string _selectedKey;
        int _selectedId;
        public event Action<ContentItem> ItemOpened;
        public event Action<bool> CollapsedChanged;

        /// <summary>Adds items to the context menu of a top-level entry (right click). Null = no menu.</summary>
        public Action<RefSectionEntry, GenericMenu> FillContextMenu;

        /// <summary>Italic hint shown in place of rows when the section is expanded but has no entries.</summary>
        public string EmptyText
        {
            get => _emptyText;
            set
            {
                if (_emptyText == value) return;
                _emptyText = value;
                Rebuild(); // the constructor already built the rows (and the hint) with the old text
            }
        }
        string _emptyText = "Nothing here";

        const string HeaderKey = "header";

        /// <summary>Adds items to the context menu of the section header (right click). Null = no menu.</summary>
        public Action<GenericMenu> FillHeaderContextMenu;

        /// <summary>
        /// Drop onto the section (header or rows). Called with perform=false on drag-update and true on drop; must set
        /// DragAndDrop.visualMode and return whether the drop is accepted. Null = not a drop target.
        /// </summary>
        public Func<bool, bool> HandleDrop;

        /// <param name="iconName">Editor icon shown before the title (as the folder icon of a tree root).</param>
        /// <param name="entries">Current top-level entries (queried on every rebuild).</param>
        /// <param name="expandedPaths">Serialized list owned by the window; expanded folder paths.</param>
        public AssetRefSection(string title, string iconName, Func<IReadOnlyList<RefSectionEntry>> entries, List<string> expandedPaths, bool collapsed)
        {
            _entries = entries;
            _expandedList = expandedPaths;
            _expanded = new HashSet<string>(expandedPaths);
            _collapsed = collapsed;

            style.flexDirection = FlexDirection.Column;
            style.flexShrink = 0; // full height: the left panel scrolls as a whole

            _toggle = MakeToggle();
            _toggle.SetValueWithoutNotify(!_collapsed);
            _toggle.RegisterValueChangedCallback(e => SetCollapsed(!e.newValue));
            // Indent per level = arrow width, measured the way TreeView measures it.
            var checkmark = _toggle.Q(className: Toggle.checkmarkUssClassName);
            checkmark?.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var s = checkmark.resolvedStyle;
                float w = s.width + s.marginLeft + s.marginRight;
                if (w > 0 && !Mathf.Approximately(w, _indentWidth)) { _indentWidth = w; Rebuild(); }
            });
            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.flexGrow = 1;
            _warning = new Image { image = EditorGUIUtility.IconContent("console.warnicon.sml").image, scaleMode = ScaleMode.ScaleToFit };
            _warning.style.width = 16;
            _warning.style.height = 16;
            _warning.style.marginRight = 4;
            _warning.style.display = DisplayStyle.None;
            var content = MakeContent(EditorGUIUtility.IconContent(iconName).image, label);
            content.Add(_warning);
            _header = MakeTreeRow(0, _toggle, content);
            _header.name = "section-header";
            _header.RegisterCallback<PointerDownEvent>(e =>
            {
                if (ContentView.IsContextClick(e, true)) { e.StopPropagation(); ShowHeaderContextMenu(); return; }
                if (e.button != 0) { if (e.button == 1) e.StopPropagation(); return; }
                if (IsInside(e.target, _toggle)) return; // the arrow toggles itself
                e.StopPropagation();
                // Like the folder tree's roots: a click selects the row, a double click expands / collapses it.
                if (e.clickCount >= 2) SetCollapsed(!_collapsed);
                else SelectHeader();
            });
            _header.RegisterCallback<PointerUpEvent>(e =>
            {
                if (ContentView.IsContextClick(e, false)) { e.StopPropagation(); ShowHeaderContextMenu(); }
            });
            Add(_header);

            _body = new VisualElement();
            Add(_body);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragLeaveEvent>(_ => ClearDropHighlight());
            RegisterCallback<DragExitedEvent>(_ => ClearDropHighlight());

            ApplyCollapsed();
            Rebuild();
        }

        public bool Collapsed => _collapsed;

        public void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            ApplyCollapsed();
            if (!collapsed) Rebuild();
            CollapsedChanged?.Invoke(collapsed);
        }

        void ApplyCollapsed()
        {
            _toggle.SetValueWithoutNotify(!_collapsed);
            _body.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ---- tree-styled rows ----

        /// <summary>An arrow styled exactly like a TreeView item's expand toggle.</summary>
        static Toggle MakeToggle()
        {
            var t = new Toggle { name = TreeToggleClass, focusable = false };
            t.AddToClassList(Foldout.toggleUssClassName);
            t.AddToClassList(TreeToggleClass);
            t.Q(className: Toggle.inputUssClassName)?.AddToClassList(Foldout.inputUssClassName);
            t.Q(className: Toggle.checkmarkUssClassName)?.AddToClassList(Foldout.checkmarkUssClassName);
            return t;
        }

        /// <summary>Icon + label, laid out like the folder tree's row content.</summary>
        static VisualElement MakeContent(Texture iconTexture, Label label)
        {
            var content = new VisualElement { name = TreeContentClass };
            content.AddToClassList(TreeContentClass);
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit, image = iconTexture };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 3;
            icon.style.flexShrink = 0;
            content.Add(icon);
            label.name = "label";
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            content.Add(label);
            return content;
        }

        /// <summary>A row with TreeView's structure: indent (depth levels), arrow, content.</summary>
        VisualElement MakeTreeRow(int depth, Toggle toggle, VisualElement content)
        {
            var row = new VisualElement { name = TreeItemClass };
            row.AddToClassList(TreeItemClass);
            // Hover highlight comes from this class, as on every ListView / TreeView row.
            row.AddToClassList(BaseVerticalCollectionView.itemUssClassName);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight;
            row.style.flexShrink = 0;
            var indent = new VisualElement { name = TreeIndentClass };
            indent.style.flexDirection = FlexDirection.Row;
            indent.style.flexShrink = 0;
            indent.style.width = _indentWidth * depth;
            indent.EnableInClassList(TreeIndentClass, depth > 0);
            row.Add(indent);
            row.Add(toggle);
            row.Add(content);
            return row;
        }

        static bool IsInside(IEventHandler target, VisualElement ancestor)
        {
            for (var e = target as VisualElement; e != null; e = e.parent) if (e == ancestor) return true;
            return false;
        }

        /// <summary>Shows a warning icon in the header with this tooltip; null hides it.</summary>
        public void SetWarning(string tooltipText)
        {
            _warning.tooltip = tooltipText ?? "";
            _warning.style.display = tooltipText != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- building ----

        /// <summary>Re-resolves every entry (names, paths, existence) and rebuilds the rows.</summary>
        public void Rebuild()
        {
            DisarmDrag();
            FullNameOverlay.Hide();
            _rows.Clear();
            _body.Clear();
            if (_collapsed) return; // built when expanded
            var entries = _entries?.Invoke();
            if (entries == null || entries.Count == 0)
            {
                AddEmptyHint();
                RefreshSelection();
                return;
            }
            foreach (var e in entries)
            {
                var row = ResolveEntry(e);
                AddRow(row);
                if (row.IsFolder && !row.Missing && _expanded.Contains(row.Path)) AddChildren(row.Path, 1);
            }
            RefreshSelection();
        }

        void AddEmptyHint()
        {
            var toggle = MakeToggle();
            toggle.style.visibility = Visibility.Hidden;
            var label = new Label(EmptyText);
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.color = Theme.DimText;
            var content = MakeContent(null, label);
            content.Q<Image>("icon").style.display = DisplayStyle.None;
            var el = MakeTreeRow(1, toggle, content);
            el.RemoveFromClassList(BaseVerticalCollectionView.itemUssClassName); // not a row: no hover
            el.pickingMode = PickingMode.Ignore;
            _body.Add(el);
        }

        Row ResolveEntry(RefSectionEntry e)
        {
            var row = new Row { Entry = e, Guid = e.Ref.Guid, Name = e.StoredName, IsSubAsset = e.Ref.IsSubAsset };
            string path = AssetRefUtil.PathOf(e.Ref);
            if (path == null) { row.Missing = true; return row; }
            row.Path = path;
            if (e.Ref.IsSubAsset)
            {
                Object obj = null;
                if (_subAssetIds.TryGetValue(e.Ref, out int cached))
                {
                    obj = EditorUtility.InstanceIDToObject(cached);
                    if (obj != null && AssetDatabase.GetAssetPath(obj) != path) obj = null;
                }
                if (obj == null) obj = AssetRefUtil.Resolve(e.Ref);
                if (obj == null) { _subAssetIds.Remove(e.Ref); row.Missing = true; return row; }
                _subAssetIds[e.Ref] = obj.GetInstanceID();
                row.InstanceId = obj.GetInstanceID();
                row.Name = obj.name;
                return row;
            }
            row.IsFolder = AssetDatabase.IsValidFolder(path);
            row.Name = row.IsFolder ? System.IO.Path.GetFileName(path) : System.IO.Path.GetFileNameWithoutExtension(path);
            row.InstanceId = Internal.UnityInternals.GetMainAssetInstanceID(path);
            return row;
        }

        void AddChildren(string folder, int depth)
        {
            foreach (var item in ContentView.ListFolder(folder))
            {
                var row = new Row
                {
                    Path = item.Path,
                    Guid = item.Guid,
                    Name = item.Name,
                    IsFolder = item.IsFolder,
                    Depth = depth,
                    InstanceId = Internal.UnityInternals.GetMainAssetInstanceID(item.Path),
                };
                AddRow(row);
                if (row.IsFolder && _expanded.Contains(row.Path)) AddChildren(row.Path, depth + 1);
            }
        }

        static bool FolderHasContent(string path)
        {
            if (!path.StartsWith("Assets", StringComparison.Ordinal)) return true; // package folders: decided on expand
            try
            {
                foreach (var _ in Directory.EnumerateFileSystemEntries(path)) return true;
            }
            catch (Exception) { }
            return false;
        }

        void AddRow(Row row)
        {
            var toggle = MakeToggle();
            if (row.IsFolder && !row.Missing && FolderHasContent(row.Path))
            {
                string path = row.Path;
                toggle.SetValueWithoutNotify(_expanded.Contains(path));
                // Rebuilding replaces this toggle: do it after the click finished.
                toggle.RegisterValueChangedCallback(_ => schedule.Execute(() => ToggleExpand(path)));
            }
            else toggle.style.visibility = Visibility.Hidden; // keeps alignment, like a TreeView leaf

            var label = new Label(row.Name);
            // Entries sit one level below the section header, like a root's children in the folder tree.
            var el = MakeTreeRow(row.Depth + 1, toggle, MakeContent(IconFor(row), label));

            if (row.Missing)
            {
                label.style.color = MissingColor;
                el.tooltip = "Missing: " + row.Guid;
            }
            else el.tooltip = row.Path;

            el.RegisterCallback<PointerDownEvent>(e => OnRowPointerDown(row, e));
            el.RegisterCallback<PointerUpEvent>(e => OnRowPointerUp(row, e));
            el.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            FullNameOverlay.Attach(el, this); // names cut off by the panel show in full on hover
            if (row.IsFolder && !row.Missing)
            {
                // A folder row is a drop target like any folder: assets (or files from Finder) move / import into it.
                // What it rejects falls through to the section (e.g. Favourites: add as favourite).
                string path = row.Path;
                el.RegisterCallback<DragUpdatedEvent>(e =>
                {
                    bool ok = ItemDrag.HandleDropOnFolder(path, false);
                    SetRowDropHighlight(el, ok);
                    if (!ok) return;
                    e.StopPropagation();
                    ClearDropHighlight();
                });
                el.RegisterCallback<DragPerformEvent>(e =>
                {
                    SetRowDropHighlight(el, false);
                    if (!ItemDrag.HandleDropOnFolder(path, false)) return;
                    e.StopPropagation();
                    ItemDrag.HandleDropOnFolder(path, true);
                });
                el.RegisterCallback<DragLeaveEvent>(_ => SetRowDropHighlight(el, false));
                el.RegisterCallback<DragExitedEvent>(_ => SetRowDropHighlight(el, false));
            }
            row.Element = el;
            _rows.Add(row);
            _body.Add(el);
        }

        static Texture IconFor(Row row)
        {
            if (row.Missing) return null;
            if (row.IsSubAsset)
            {
                var obj = EditorUtility.InstanceIDToObject(row.InstanceId);
                return obj != null ? AssetPreview.GetMiniThumbnail(obj) : null;
            }
            var icon = AssetDatabase.GetCachedIcon(row.Path);
            if (icon == null && row.IsFolder) icon = EditorGUIUtility.IconContent("Folder Icon").image;
            return icon;
        }

        void ToggleExpand(string path)
        {
            if (!_expanded.Remove(path)) _expanded.Add(path);
            _expandedList.Clear();
            _expandedList.AddRange(_expanded);
            Rebuild();
        }

        // Identifies a row across rebuilds; the same asset can appear twice (top level and inside a folder entry).
        static string KeyOf(Row r) => r.Depth + "|" + (r.Path ?? r.Guid) + "|" + (r.IsSubAsset ? r.InstanceId : 0);

        /// <summary>Highlights the row clicked in this section, if any.</summary>
        public void RefreshSelection()
        {
            _header.style.backgroundColor = _selectedKey == HeaderKey
                ? new StyleColor(SelectedColor)
                : new StyleColor(StyleKeyword.Null);
            foreach (var r in _rows)
                // Not selected: no inline colour, so the theme's hover highlight shows.
                r.Element.style.backgroundColor = _selectedKey != null && KeyOf(r) == _selectedKey
                    ? new StyleColor(SelectedColor)
                    : new StyleColor(StyleKeyword.Null);
        }

        /// <summary>Drops this section's highlight (something else was clicked).</summary>
        public void ClearSelection()
        {
            if (_selectedKey == null) return;
            _selectedKey = null;
            _selectedId = 0;
            RefreshSelection();
        }

        /// <summary>Unity's selection changed: keep the highlight only if it is still exactly what this section set.</summary>
        public void OnUnitySelectionChanged()
        {
            if (_selectedKey == null || _selectedKey == HeaderKey) return; // the header selects no object
            var ids = Selection.instanceIDs;
            if (_selectedId == 0 || ids.Length != 1 || ids[0] != _selectedId) ClearSelection();
        }

        void SelectHeader()
        {
            _selectedKey = HeaderKey;
            _selectedId = 0;
            RefreshSelection();
            RowSelected?.Invoke();
        }

        void SelectRow(Row row)
        {
            int id = row.InstanceId;
            if (id == 0)
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(row.Path);
                if (obj != null) id = obj.GetInstanceID();
            }
            _selectedKey = KeyOf(row);
            _selectedId = id;
            if (id != 0) Selection.instanceIDs = new[] { id }; // Inspector shows it; the right panel is left alone
            RefreshSelection();
            RowSelected?.Invoke();
        }

        // ---- clicks / drag out ----

        static ContentItem ToItem(Row row) => new ContentItem
        {
            Path = row.Path,
            Guid = row.Guid,
            InstanceId = row.InstanceId,
            IsFolder = row.IsFolder,
            IsSubAsset = row.IsSubAsset,
            Name = row.Name,
        };

        void OnRowPointerDown(Row row, PointerDownEvent evt)
        {
            if (evt.button == 0 && IsInside(evt.target, row.Element?.Q<Toggle>())) return; // the arrow expands itself
            DisarmDrag();
            if (ContentView.IsContextClick(evt, true)) { evt.StopPropagation(); ShowContextMenu(row); return; }
            if (evt.button != 0) { if (evt.button == 1) evt.StopPropagation(); return; }
            evt.StopPropagation();
            if (row.Missing) return;

            if (evt.clickCount >= 2)
            {
                if (row.IsFolder) FolderOpened?.Invoke(row.Path);
                else ItemOpened?.Invoke(ToItem(row));
                return;
            }

            // Single click, folder or asset: select it (Inspector), keep the right panel as it is. Done on release, and
            // only if no drag started, so dragging into an Inspector field keeps the Inspector showing its object.

            _dragRow = row;
            _dragElement = row.Element;
            _dragPointer = evt.pointerId;
            _dragStart = evt.position;
            _dragElement.CapturePointer(evt.pointerId);
        }

        void OnRowPointerUp(Row row, PointerUpEvent evt)
        {
            bool pressedHere = _dragRow == row; // still armed: pressed on this row and no drag started
            DisarmDrag();
            if (ContentView.IsContextClick(evt, false)) { evt.StopPropagation(); ShowContextMenu(row); return; }
            if (evt.button == 0 && pressedHere) SelectRow(row);
        }

        void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (_dragRow == null || evt.pointerId != _dragPointer || (evt.pressedButtons & 1) == 0) return;
            if (((Vector2)evt.position - _dragStart).magnitude <= DragThreshold) return;
            var row = _dragRow;
            DisarmDrag();
            evt.StopPropagation();
            ItemDrag.StartDrag(new[] { ToItem(row) });
        }

        void DisarmDrag()
        {
            var el = _dragElement;
            int pointer = _dragPointer;
            _dragRow = null;
            _dragElement = null;
            _dragPointer = -1;
            if (el != null && el.HasPointerCapture(pointer)) el.ReleasePointer(pointer);
        }

        void ShowHeaderContextMenu()
        {
            if (FillHeaderContextMenu == null) return;
            var menu = new GenericMenu();
            FillHeaderContextMenu(menu);
            if (menu.GetItemCount() > 0) menu.ShowAsContext();
        }

        void ShowContextMenu(Row row)
        {
            if (FillContextMenu == null || !row.Entry.HasValue) return;
            var menu = new GenericMenu();
            FillContextMenu(row.Entry.Value, menu);
            if (menu.GetItemCount() > 0) menu.ShowAsContext();
        }

        // ---- drop target ----

        void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (HandleDrop == null) return;
            evt.StopPropagation();
            bool ok = HandleDrop(false);
            style.backgroundColor = ok ? new StyleColor(ItemDrag.DropColor) : new StyleColor(StyleKeyword.Null);
        }

        void OnDragPerform(DragPerformEvent evt)
        {
            if (HandleDrop == null) return;
            evt.StopPropagation();
            ClearDropHighlight();
            HandleDrop(true);
        }

        void ClearDropHighlight() => style.backgroundColor = new StyleColor(StyleKeyword.Null);

        void SetRowDropHighlight(VisualElement el, bool on)
        {
            if (on) el.style.backgroundColor = ItemDrag.DropColor;
            else RefreshSelection(); // back to selected / plain
        }
    }
}
