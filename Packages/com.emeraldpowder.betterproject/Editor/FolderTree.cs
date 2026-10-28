using System;
using System.Collections.Generic;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    public class FolderTree : VisualElement
    {
        const string AssetsRoot = "Assets";
        const string PackagesRoot = "Packages";
        static readonly Color DropColor = ItemDrag.DropColor;

        sealed class Node
        {
            public string Path;
            public string Display;
            public bool HasChildren;
            public bool Placeholder;
        }

        readonly TreeView _tree;
        readonly List<string> _expandedList;
        readonly HashSet<string> _expanded;
        readonly Dictionary<string, int> _ids = new Dictionary<string, int>();
        readonly HashSet<int> _built = new HashSet<int>();
        int _nextId = 1;
        int _nextPlaceholder = -1;
        bool _busy;
        bool _showPackages = BetterProjectSettings.instance.ShowPackages;
        readonly List<string> _selectedPaths = new List<string>(); // tree selection, kept across rebuilds

        /// <summary>
        /// The user changed the tree selection: all selected folders (tree order) and the one just clicked (primary).
        /// </summary>
        public event Action<IReadOnlyList<string>, string> FoldersSelected;

        /// <param name="expandedFolders">Serialized list owned by the window; kept in sync with expand state.</param>
        public FolderTree(List<string> expandedFolders)
        {
            _expandedList = expandedFolders;
            _expanded = new HashSet<string>(expandedFolders);
            _expanded.Add(AssetsRoot);

            style.flexGrow = 1;
            _tree = new TreeView
            {
                fixedItemHeight = Theme.RowHeight,
                selectionType = SelectionType.Multiple, // Ctrl/Cmd and Shift click, like Unity's Project window
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _tree.style.flexGrow = 1;
            var scroller = _tree.Q<ScrollView>()?.verticalScroller;
            if (scroller != null) scroller.valueChanged += _ => FullNameOverlay.Hide();
            _tree.itemExpandedChanged += OnExpandedChanged;
            _tree.selectedIndicesChanged += OnSelectedIndicesChanged;
            Add(_tree);
            Rebuild();
        }

        // ---- public API ----

        /// <summary>Shows or hides the Packages root ("Show / Packages" setting).</summary>
        public void SetShowPackages(bool show)
        {
            if (show == _showPackages) return;
            _showPackages = show;
            Rebuild();
        }

        public void Rebuild()
        {
            PruneExpanded();
            FullNameOverlay.Hide();
            _busy = true;
            try
            {
                _built.Clear();
                _tree.SetRootItems(BuildRoots());
                _tree.Rebuild();
                foreach (var p in _expanded)
                    if (_ids.TryGetValue(p, out int id) && _built.Contains(id))
                        _tree.ExpandItem(id, false, false);
                _tree.RefreshItems();
                var ids = new List<int>();
                foreach (var p in _selectedPaths)
                    if (_ids.TryGetValue(p, out int sid) && _built.Contains(sid)) ids.Add(sid);
                if (ids.Count > 0) _tree.SetSelectionById(ids);
            }
            finally { _busy = false; }
        }

        public void Reveal(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            _selectedPaths.Clear();
            _selectedPaths.Add(folderPath);
            bool changed = false;
            for (string p = AssetPaths.Parent(folderPath); p.Length > 0; p = AssetPaths.Parent(p))
                changed |= _expanded.Add(p);
            if (changed) { PersistExpanded(); Rebuild(); }
            else if (_ids.TryGetValue(folderPath, out int id) && _built.Contains(id))
            {
                _busy = true;
                try { _tree.SetSelectionById(id); } finally { _busy = false; }
            }
            if (_ids.TryGetValue(folderPath, out int sid) && _built.Contains(sid)) _tree.ScrollToItemById(sid);
        }

        // ---- building ----

        int IdFor(string path)
        {
            if (!_ids.TryGetValue(path, out int id)) _ids[path] = id = _nextId++;
            return id;
        }

        static bool HasSubFolders(string path) => AssetDatabase.GetSubFolders(path).Length > 0;


        IList<TreeViewItemData<Node>> BuildRoots()
        {
            var roots = new List<TreeViewItemData<Node>>
            {
                Make(new Node { Path = AssetsRoot, Display = AssetsRoot, HasChildren = HasSubFolders(AssetsRoot) }),
            };
            if (_showPackages) roots.Add(Make(new Node { Path = PackagesRoot, Display = PackagesRoot, HasChildren = true }));
            return roots;
        }

        TreeViewItemData<Node> Make(Node n)
        {
            int id = IdFor(n.Path);
            _built.Add(id);
            List<TreeViewItemData<Node>> kids = null;
            if (n.HasChildren)
            {
                kids = new List<TreeViewItemData<Node>>();
                if (_expanded.Contains(n.Path))
                    foreach (var c in ChildrenOf(n.Path)) kids.Add(Make(c));
                else
                    kids.Add(new TreeViewItemData<Node>(_nextPlaceholder--, new Node { Placeholder = true, Path = "", Display = "" }));
            }
            return new TreeViewItemData<Node>(id, n, kids);
        }

        static List<Node> ChildrenOf(string path)
        {
            var list = new List<Node>();
            if (path == PackagesRoot)
            {
                foreach (var p in PackageFolders.All())
                    list.Add(new Node { Path = p.Path, Display = p.DisplayName, HasChildren = HasSubFolders(p.Path) });
                return list;
            }
            if (!AssetDatabase.IsValidFolder(path)) return list;
            foreach (var sub in AssetDatabase.GetSubFolders(path))
                list.Add(new Node { Path = sub, Display = sub.Substring(sub.LastIndexOf('/') + 1), HasChildren = HasSubFolders(sub) });
            // Same order as the content panel and the breadcrumb menus.
            list.Sort((a, b) =>
            {
                int c = EditorUtility.NaturalCompare(a.Display, b.Display);
                return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path);
            });
            return list;
        }

        // ---- rows ----

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 3;
            var label = new Label { name = "label" };
            label.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(icon);
            row.Add(label);
            row.RegisterCallback<DragUpdatedEvent>(OnRowDragUpdated);
            row.RegisterCallback<DragPerformEvent>(OnRowDragPerform);
            row.RegisterCallback<DragLeaveEvent>(e => ClearDropHighlight((VisualElement)e.currentTarget));
            row.RegisterCallback<DragExitedEvent>(e => ClearDropHighlight((VisualElement)e.currentTarget));
            FullNameOverlay.Attach(row, this); // names cut off by the panel show in full on hover
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            var node = _tree.GetItemDataForIndex<Node>(index);
            var icon = row.Q<Image>("icon");
            var label = row.Q<Label>("label");
            FullNameOverlay.HideFor(row); // the row is about to show another folder
            ClearDropHighlight(row);
            row.userData = node != null && !node.Placeholder ? node.Path : null;
            if (node == null || node.Placeholder)
            {
                icon.image = null;
                label.text = "";
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                return;
            }
            bool open = _tree.IsExpanded(_tree.GetIdForIndex(index));
            icon.image = EditorGUIUtility.IconContent(open ? "FolderOpened Icon" : "Folder Icon").image;
            label.text = node.Display;
            // Roots are bold, as in Unity's Project window.
            bool root = node.Path == AssetsRoot || node.Path == PackagesRoot;
            label.style.unityFontStyleAndWeight = root ? FontStyle.Bold : FontStyle.Normal;
        }

        // ---- drop targets ----

        void OnRowDragUpdated(DragUpdatedEvent evt)
        {
            var row = (VisualElement)evt.currentTarget;
            if (!(row.userData is string path)) return;
            evt.StopPropagation();
            bool ok = ItemDrag.HandleDropOnFolder(path, false);
            row.style.backgroundColor = ok ? new StyleColor(DropColor) : new StyleColor(StyleKeyword.Null);
        }

        void OnRowDragPerform(DragPerformEvent evt)
        {
            var row = (VisualElement)evt.currentTarget;
            if (!(row.userData is string path)) return;
            evt.StopPropagation();
            ClearDropHighlight(row);
            ItemDrag.HandleDropOnFolder(path, true);
        }

        static void ClearDropHighlight(VisualElement row) => row.style.backgroundColor = new StyleColor(StyleKeyword.Null);

        // ---- events ----

        void OnSelectedIndicesChanged(IEnumerable<int> indices)
        {
            if (_busy) return;
            var paths = new List<string>();
            foreach (int i in indices)
            {
                var node = _tree.GetItemDataForIndex<Node>(i);
                if (node != null && !node.Placeholder && !paths.Contains(node.Path)) paths.Add(node.Path);
            }
            if (paths.Count == 0) return;
            // Primary = the folder just added (the clicked one); a Ctrl/Cmd click that removed one keeps the last.
            string primary = null;
            foreach (var p in paths) if (!_selectedPaths.Contains(p)) primary = p;
            if (primary == null) primary = paths[paths.Count - 1];
            _selectedPaths.Clear();
            _selectedPaths.AddRange(paths);
            FoldersSelected?.Invoke(paths, primary);
        }

        void OnExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            if (_busy) return;
            var node = _tree.GetItemDataForId<Node>(args.id);
            if (node == null || node.Placeholder) return;
            if (args.isExpanded)
            {
                _expanded.Add(node.Path);
                if (args.isAppliedToAllChildren) AddDescendants(node.Path);
            }
            else
            {
                _expanded.Remove(node.Path);
                if (args.isAppliedToAllChildren) RemoveDescendants(node.Path);
            }
            PersistExpanded();
            // Rebuild after the tree finishes handling this event (loads/unloads that subtree).
            // A full rebuild is kept (TreeView has no clean per-subtree swap); the scroll offset is preserved.
            var scroll = _tree.Q<ScrollView>();
            Vector2 offset = scroll != null ? scroll.scrollOffset : Vector2.zero;
            _tree.schedule.Execute(() =>
            {
                Rebuild();
                if (scroll != null)
                {
                    scroll.scrollOffset = offset;
                    _tree.schedule.Execute(() => scroll.scrollOffset = offset);
                }
            });
        }

        void AddDescendants(string path)
        {
            foreach (var c in ChildrenOf(path))
            {
                if (!c.HasChildren) continue;
                _expanded.Add(c.Path);
                AddDescendants(c.Path);
            }
        }

        void RemoveDescendants(string path)
        {
            string prefix = path + "/";
            _expanded.RemoveWhere(p => p.StartsWith(prefix, StringComparison.Ordinal));
        }

        void PruneExpanded()
        {
            _expanded.RemoveWhere(p => p != PackagesRoot && !AssetDatabase.IsValidFolder(p));
            PersistExpanded();
        }

        void PersistExpanded()
        {
            _expandedList.Clear();
            _expandedList.AddRange(_expanded);
        }
    }
}
