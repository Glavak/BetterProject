using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Shows the full text of a label that is cut off (clipped by its panel, or ending in an ellipsis) while its row is
    /// hovered: a copy of the label laid exactly over it, on the window's top layer, so it extends over both panels.
    /// One overlay at a time; it never takes the mouse (clicks, drags and hover go to the row underneath).
    /// </summary>
    static class FullNameOverlay
    {
        static Label s_Overlay;
        static VisualElement s_Owner;

        /// <summary>
        /// Shows the overlay while <paramref name="row"/> is hovered, if its label is cut off. <paramref name="clip"/> is
        /// the area the label is visible in (e.g. the left panel): text running past its right edge counts as cut off.
        /// </summary>
        public static void Attach(VisualElement row, VisualElement clip)
        {
            row.RegisterCallback<PointerEnterEvent>(_ => Show(row, row.Q<Label>("label"), clip));
            row.RegisterCallback<PointerLeaveEvent>(_ => HideFor(row));
            row.RegisterCallback<DetachFromPanelEvent>(_ => HideFor(row));
        }

        /// <summary>Hides the overlay (call when rows are rebuilt, rebound or scrolled).</summary>
        public static void Hide()
        {
            s_Owner = null;
            s_Overlay?.RemoveFromHierarchy();
        }

        /// <summary>Hides the overlay if it belongs to this row.</summary>
        public static void HideFor(VisualElement row)
        {
            if (s_Owner == row) Hide();
        }

        static void Show(VisualElement row, Label label, VisualElement clip)
        {
            Hide();
            var panel = row.panel;
            if (panel == null || label == null || string.IsNullOrEmpty(label.text)) return;
            var size = label.MeasureTextSize(label.text, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN, VisualElement.MeasureMode.Undefined);
            var box = label.worldBound;
            float visibleRight = Mathf.Min(box.xMax, clip != null ? clip.worldBound.xMax : box.xMax);
            float textRight = label.contentRect.x + box.x + size.x;
            if (textRight <= visibleRight + 0.5f) return; // fits

            if (s_Overlay == null)
            {
                s_Overlay = new Label { name = "bp-full-name", pickingMode = PickingMode.Ignore };
                s_Overlay.style.position = Position.Absolute;
                s_Overlay.style.whiteSpace = WhiteSpace.NoWrap;
                s_Overlay.style.overflow = Overflow.Visible;
                s_Overlay.style.marginLeft = s_Overlay.style.marginRight = s_Overlay.style.marginTop = s_Overlay.style.marginBottom = 0;
                s_Overlay.style.paddingTop = s_Overlay.style.paddingBottom = 0;
                s_Overlay.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            var ls = label.resolvedStyle;
            s_Overlay.text = label.text;
            s_Overlay.style.unityFontDefinition = ls.unityFontDefinition;
            s_Overlay.style.fontSize = ls.fontSize;
            s_Overlay.style.unityFontStyleAndWeight = ls.unityFontStyleAndWeight;
            s_Overlay.style.color = ls.color;
            s_Overlay.style.paddingLeft = ls.paddingLeft;
            s_Overlay.style.paddingRight = ls.paddingLeft + 2;
            s_Overlay.style.backgroundColor = BackgroundBehind(label, clip);

            // Hosted in the window's root element (not the panel's bare visual tree), so it gets the editor theme
            // (fonts, colours); positioned in that element's coordinates. Added last = drawn above both panels.
            VisualElement root = row;
            while (root.parent != null && root.parent != panel.visualTree) root = root.parent;
            root.Add(s_Overlay);
            var at = root.WorldToLocal(box.position);
            float width = ls.paddingLeft * 2 + 2 + size.x + 1;
            s_Overlay.style.left = Mathf.Max(0, Mathf.Min(at.x, root.layout.width - width));
            s_Overlay.style.top = at.y;
            s_Overlay.style.width = width;
            s_Overlay.style.height = box.height;
            s_Owner = row;
        }

        // The colour drawn behind the label: the first opaque background up to the clip area (a selected row, the
        // panel), else the editor window's background.
        static Color BackgroundBehind(VisualElement label, VisualElement clip)
        {
            for (var e = label.parent; e != null; e = e.parent)
            {
                var c = e.resolvedStyle.backgroundColor;
                if (c.a > 0.5f) return new Color(c.r, c.g, c.b, 1f);
                if (e == clip) break;
            }
            return Theme.WindowBackground;
        }
    }
}
