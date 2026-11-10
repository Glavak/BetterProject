using System;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Column header of the details view. Drag a title to reorder, drag a border between titles to resize the columns on
    /// both sides of it, right click for the list of columns (shown / hidden). Name stretches over the space the other
    /// columns leave.
    /// </summary>
    class DetailsHeader : VisualElement
    {
        public const float Height = 20f;
        public const float NameMinWidth = 80f;
        /// <summary>Element name prefix of a row's column cell: "col-" + <see cref="DetailsColumns.Key"/>.</summary>
        public const string ColumnPrefix = "col-";
        const string ResizeClass = "bp-col-resize"; // resize cursor, BetterProject.uss
        const float DragThreshold = 4f;
        const float HandleWidth = 8f; // grab area of a border, centred on it
        static readonly Color MarkerColor = new Color(0.23f, 0.47f, 0.85f, 1f);

        DetailsColumns _layout = new DetailsColumns();
        readonly VisualElement _cells;
        readonly VisualElement _handles;
        readonly VisualElement _marker;

        // Reorder drag (on a title).
        VisualElement _pressCell;
        int _pressPointer = -1;
        float _pressX;
        bool _reordering;
        int _gap;

        // Resize drag (on a border between titles). Null side = Name (takes the rest) or nothing.
        DetailsColumn? _resizeLeft, _resizeRight;
        float _resizeStartX, _resizeStartLeft, _resizeStartRight;

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
            // Over the titles, so a border's grab area covers both sides of it.
            _handles = new VisualElement { pickingMode = PickingMode.Ignore };
            _handles.style.position = Position.Absolute;
            _handles.style.left = _handles.style.top = _handles.style.right = _handles.style.bottom = 0;
            Add(_handles);
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
            _handles.Clear();
            var visible = _layout.Visible();
            for (int i = 0; i < visible.Count; i++)
            {
                _cells.Add(MakeCell(visible[i]));
                // Border right of column i, between it and column i + 1 (or after the last one).
                DetailsColumn? left = visible[i].Id == DetailsColumn.Name ? (DetailsColumn?)null : visible[i].Id;
                DetailsColumn? right = i + 1 < visible.Count && visible[i + 1].Id != DetailsColumn.Name
                    ? visible[i + 1].Id : (DetailsColumn?)null;
                if (left == null && right == null) continue; // Name last: nothing to resize
                _handles.Add(MakeResizeHandle(left, right));
            }
        }

        VisualElement MakeCell(DetailsColumns.Column c)
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
            cell.RegisterCallback<GeometryChangedEvent>(_ => PlaceHandles());
            return cell;
        }

        VisualElement MakeResizeHandle(DetailsColumn? left, DetailsColumn? right)
        {
            var handle = new VisualElement();
            handle.AddToClassList(ResizeClass);
            handle.style.position = Position.Absolute;
            handle.style.top = 0;
            handle.style.bottom = 0;
            handle.style.width = HandleWidth;
            handle.RegisterCallback<PointerDownEvent>(e => OnResizeDown(e, left, right));
            handle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            handle.RegisterCallback<PointerUpEvent>(OnResizeUp);
            return handle;
        }

        /// <summary>Centres each resize handle on its border (the right edge of its title).</summary>
        void PlaceHandles()
        {
            int h = 0;
            var visible = _layout.Visible();
            for (int i = 0; i < _cells.childCount && h < _handles.childCount; i++)
            {
                bool nameLast = i == visible.Count - 1 && visible[i].Id == DetailsColumn.Name;
                if (nameLast) continue; // no handle there, see Rebuild
                _handles[h++].style.left = _cells[i].layout.xMax - HandleWidth / 2f;
            }
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

        void OnResizeDown(PointerDownEvent e, DetailsColumn? left, DetailsColumn? right)
        {
            if (e.button != 0) return;
            e.StopPropagation(); // not a reorder press on the title
            _resizeLeft = left;
            _resizeRight = right;
            _resizeStartX = e.position.x;
            _resizeStartLeft = left.HasValue ? _layout.WidthOf(left.Value) : 0f;
            _resizeStartRight = right.HasValue ? _layout.WidthOf(right.Value) : 0f;
            ((VisualElement)e.currentTarget).CapturePointer(e.pointerId);
        }

        void OnResizeMove(PointerMoveEvent e)
        {
            var handle = (VisualElement)e.currentTarget;
            if (!handle.HasPointerCapture(e.pointerId)) return;
            // The left column grows by dx, the right one shrinks by it; dx is clamped so both stay in range.
            float dx = e.position.x - _resizeStartX;
            float min = float.NegativeInfinity, max = float.PositiveInfinity;
            if (_resizeLeft.HasValue)
            {
                min = Mathf.Max(min, DetailsColumns.MinWidth - _resizeStartLeft);
                max = Mathf.Min(max, DetailsColumns.MaxWidth - _resizeStartLeft);
            }
            if (_resizeRight.HasValue)
            {
                min = Mathf.Max(min, _resizeStartRight - DetailsColumns.MaxWidth);
                max = Mathf.Min(max, _resizeStartRight - DetailsColumns.MinWidth);
            }
            dx = Mathf.Clamp(dx, min, max);
            if (_resizeLeft.HasValue) ApplyWidth(_resizeLeft.Value, _resizeStartLeft + dx);
            if (_resizeRight.HasValue) ApplyWidth(_resizeRight.Value, _resizeStartRight - dx);
        }

        void ApplyWidth(DetailsColumn id, float width)
        {
            if (!_layout.SetWidth(id, width)) return;
            float w = _layout.WidthOf(id);
            foreach (var cell in _cells.Children())
                if (cell.userData is DetailsColumn cid && cid == id) cell.style.width = w;
            WidthChanging?.Invoke(id, w);
        }

        void OnResizeUp(PointerUpEvent e)
        {
            var handle = (VisualElement)e.currentTarget;
            if (!handle.HasPointerCapture(e.pointerId)) return;
            handle.ReleasePointer(e.pointerId);
            e.StopPropagation();
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
