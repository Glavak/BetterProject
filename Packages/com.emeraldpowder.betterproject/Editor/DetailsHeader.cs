using System;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Column header of the details view. Drag a title to reorder, drag a border between titles to resize the column
    /// left of it, right click for the list of columns (shown / hidden). Name stretches over the space the other columns leave.
    /// </summary>
    class DetailsHeader : VisualElement
    {
        public const float Height = 20f;
        public const float NameMinWidth = 80f;
        /// <summary>Element name prefix of a row's column cell: "col-" + <see cref="DetailsColumns.Key"/>.</summary>
        public const string ColumnPrefix = "col-";
        const string ResizeClass = "bp-col-resize"; // resize cursor, BetterProject.uss
        const float DragThreshold = 4f;
        static readonly Color MarkerColor = new Color(0.23f, 0.47f, 0.85f, 1f);

        DetailsColumns _layout = new DetailsColumns();
        readonly VisualElement _cells;
        readonly VisualElement _marker;

        // Reorder drag (on a title).
        VisualElement _pressCell;
        int _pressPointer = -1;
        float _pressX;
        bool _reordering;
        int _gap;

        // Resize drag (on a border between titles).
        DetailsColumn _resizeColumn;
        VisualElement _resizeCell;
        float _resizeStartX, _resizeStartWidth, _resizeSign;

        /// <summary>Order or shown columns changed: rows must be rebuilt.</summary>
        public event Action ColumnsChanged;
        /// <summary>A column is being resized (live): apply the width to the rows' cells.</summary>
        public event Action<DetailsColumn, float> WidthChanging;
        /// <summary>A resize finished.</summary>
        public event Action WidthChanged;

        public DetailsColumns Layout
        {
            get => _layout;
            set { _layout = value ?? new DetailsColumns(); Rebuild(); }
        }

        public DetailsHeader()
        {
            style.flexShrink = 0;
            style.height = Height;
            style.backgroundColor = Theme.WindowBackground;
            style.borderBottomWidth = 1;
            style.borderBottomColor = Theme.Separator;
            style.overflow = Overflow.Hidden;
            _cells = new VisualElement();
            _cells.style.flexDirection = FlexDirection.Row;
            _cells.style.flexGrow = 1;
            Add(_cells);
            _marker = new VisualElement { pickingMode = PickingMode.Ignore };
            _marker.style.position = Position.Absolute;
            _marker.style.top = 0;
            _marker.style.bottom = 0;
            _marker.style.width = 2;
            _marker.style.backgroundColor = MarkerColor;
            _marker.style.display = DisplayStyle.None;
            Add(_marker);

            // The content view treats presses on itself as clicks on empty space (clear selection, asset menu).
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (ContentView.IsContextClick(e, true)) ShowMenu();
                e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (ContentView.IsContextClick(e, false)) ShowMenu();
                e.StopPropagation();
            });
            Rebuild();
        }

        /// <summary>Space right of the columns that the rows below don't have (their vertical scrollbar).</summary>
        public void SetRightInset(float width) => style.paddingRight = Mathf.Max(0f, width);

        /// <summary>Gives a header or row cell its column's width (Name: whatever is left, at least NameMinWidth).</summary>
        public static void StyleColumn(VisualElement e, DetailsColumns.Column c)
        {
            e.style.overflow = Overflow.Hidden;
            if (c.Id == DetailsColumn.Name)
            {
                e.style.flexGrow = 1;
                e.style.flexShrink = 1;
                e.style.flexBasis = 0;
                e.style.minWidth = NameMinWidth;
            }
            else
            {
                e.style.flexGrow = 0;
                e.style.flexShrink = 0;
                e.style.width = c.Width;
            }
        }

        public void Rebuild()
        {
            _cells.Clear();
            var visible = _layout.Visible();
            for (int i = 0; i < visible.Count; i++)
                _cells.Add(MakeCell(visible[i], i == 0, i == visible.Count - 1));
        }

        VisualElement MakeCell(DetailsColumns.Column c, bool first, bool last)
        {
            var cell = new VisualElement { userData = c.Id, tooltip = DetailsColumns.Title(c.Id) };
            StyleColumn(cell, c);
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.borderRightWidth = 1;
            cell.style.borderRightColor = Theme.Separator;
            var title = new Label(DetailsColumns.Title(c.Id)) { pickingMode = PickingMode.Ignore };
            title.style.flexGrow = 1;
            title.style.paddingLeft = 4;
            title.style.marginLeft = title.style.marginRight = title.style.marginTop = title.style.marginBottom = 0;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.overflow = Overflow.Hidden;
            cell.Add(title);
            cell.RegisterCallback<PointerDownEvent>(OnCellPointerDown);
            cell.RegisterCallback<PointerMoveEvent>(OnCellPointerMove);
            cell.RegisterCallback<PointerUpEvent>(OnCellPointerUp);
            cell.RegisterCallback<PointerCaptureOutEvent>(_ => EndReorder(false));

            // A border between two titles resizes the column left of it: half of its grab area is in each cell.
            if (!first) cell.Add(MakeResizeHandle(c.Id, false));
            if (!(last && c.Id == DetailsColumn.Name)) cell.Add(MakeResizeHandle(c.Id, true)); // Name last: no neighbour
            return cell;
        }

        VisualElement MakeResizeHandle(DetailsColumn id, bool rightEdge)
        {
            var handle = new VisualElement();
            handle.AddToClassList(ResizeClass);
            handle.style.position = Position.Absolute;
            if (rightEdge) handle.style.right = 0;
            else handle.style.left = 0;
            handle.style.top = 0;
            handle.style.bottom = 0;
            handle.style.width = 3;
            handle.RegisterCallback<PointerDownEvent>(e => OnResizeDown(e, id, rightEdge));
            handle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            handle.RegisterCallback<PointerUpEvent>(OnResizeUp);
            return handle;
        }

        // ---- reorder ----

        void OnCellPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || ContentView.IsContextClick(e, true)) return;
            _pressCell = (VisualElement)e.currentTarget;
            _pressPointer = e.pointerId;
            _pressX = e.position.x;
            _reordering = false;
            _pressCell.CapturePointer(e.pointerId);
        }

        void OnCellPointerMove(PointerMoveEvent e)
        {
            if (_pressCell == null || e.pointerId != _pressPointer) return;
            if (!_reordering)
            {
                if (Mathf.Abs(e.position.x - _pressX) <= DragThreshold) return;
                _reordering = true;
                _pressCell.style.opacity = 0.5f;
                _marker.style.display = DisplayStyle.Flex;
                _marker.BringToFront();
            }
            // Gap = how many titles have their middle left of the pointer.
            _gap = 0;
            foreach (var cell in _cells.Children())
                if (cell.worldBound.center.x < e.position.x) _gap++;
            float x = _gap < _cells.childCount ? _cells[_gap].layout.xMin : _cells[_cells.childCount - 1].layout.xMax;
            _marker.style.left = Mathf.Max(0f, x - 1f);
        }

        void OnCellPointerUp(PointerUpEvent e)
        {
            if (_pressCell == null || e.pointerId != _pressPointer) return;
            EndReorder(_reordering);
        }

        void EndReorder(bool apply)
        {
            var cell = _pressCell;
            if (cell == null) return;
            _pressCell = null;
            bool reordering = _reordering;
            _reordering = false;
            if (cell.HasPointerCapture(_pressPointer)) cell.ReleasePointer(_pressPointer);
            _pressPointer = -1;
            cell.style.opacity = StyleKeyword.Null;
            _marker.style.display = DisplayStyle.None;
            if (apply && reordering && cell.userData is DetailsColumn id && _layout.MoveTo(id, _gap))
            {
                Rebuild();
                ColumnsChanged?.Invoke();
            }
        }

        // ---- resize ----

        void OnResizeDown(PointerDownEvent e, DetailsColumn id, bool rightEdge)
        {
            if (e.button != 0) return;
            e.StopPropagation(); // not a reorder press on the title
            // The column left of the border, and the one right of it.
            var visible = _layout.Visible();
            int self = visible.FindIndex(c => c.Id == id);
            int left = rightEdge ? self : self - 1;
            if (left < 0) return; // left edge of the first title: no border there
            DetailsColumn? right = left + 1 < visible.Count ? visible[left + 1].Id : (DetailsColumn?)null;
            // Name takes the rest, so it's resized through its right neighbour (which shrinks as Name grows).
            if (visible[left].Id == DetailsColumn.Name)
            {
                if (right == null) return; // Name is last: nothing to take the space from
                _resizeColumn = right.Value;
                _resizeSign = -1f;
            }
            else
            {
                _resizeColumn = visible[left].Id;
                _resizeSign = 1f;
            }
            _resizeCell = null;
            foreach (var cell in _cells.Children())
                if (cell.userData is DetailsColumn cid && cid == _resizeColumn) _resizeCell = cell;
            if (_resizeCell == null) return;
            _resizeStartX = e.position.x;
            _resizeStartWidth = _layout.WidthOf(_resizeColumn);
            ((VisualElement)e.currentTarget).CapturePointer(e.pointerId);
        }

        void OnResizeMove(PointerMoveEvent e)
        {
            var handle = (VisualElement)e.currentTarget;
            if (_resizeCell == null || !handle.HasPointerCapture(e.pointerId)) return;
            if (!_layout.SetWidth(_resizeColumn, _resizeStartWidth + _resizeSign * (e.position.x - _resizeStartX))) return;
            float w = _layout.WidthOf(_resizeColumn);
            _resizeCell.style.width = w;
            WidthChanging?.Invoke(_resizeColumn, w);
        }

        void OnResizeUp(PointerUpEvent e)
        {
            var handle = (VisualElement)e.currentTarget;
            if (!handle.HasPointerCapture(e.pointerId)) return;
            handle.ReleasePointer(e.pointerId);
            e.StopPropagation();
            _resizeCell = null;
            WidthChanged?.Invoke();
        }

        // ---- columns menu ----

        void ShowMenu()
        {
            EndReorder(false);
            var menu = new GenericMenu();
            foreach (var c in _layout.All)
            {
                var content = new GUIContent(DetailsColumns.Title(c.Id));
                if (c.Id == DetailsColumn.Name) { menu.AddDisabledItem(content, true); continue; }
                var id = c.Id;
                bool visible = c.Visible;
                menu.AddItem(content, visible, () =>
                {
                    if (!_layout.SetVisible(id, !visible)) return;
                    Rebuild();
                    ColumnsChanged?.Invoke();
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Reset Columns"), false, () =>
            {
                _layout.Reset();
                Rebuild();
                ColumnsChanged?.Invoke();
            });
            menu.ShowAsContext();
        }
    }
}
