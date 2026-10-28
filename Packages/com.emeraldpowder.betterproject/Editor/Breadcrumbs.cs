using System;
using System.Collections.Generic;
using BetterProject.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Path bar above the content panel: <c>Assets ▸ Art ▸ Props</c>. Names navigate, arrows open a menu of that
    /// folder's direct subfolders. Too wide: leading segments collapse into a "…" menu of the hidden ancestors.
    /// </summary>
    public class Breadcrumbs : VisualElement
    {
        const string Arrow = ">";
        const string Ellipsis = "…";
        const string PackagesRoot = "Packages";

        readonly VisualElement _row;
        readonly Button _ellipsis;
        readonly List<VisualElement> _segments = new List<VisualElement>();
        readonly List<float> _widths = new List<float>();
        List<(string Name, string Path)> _crumbs = new List<(string Name, string Path)>();
        int _hidden = -1;
        float _ellipsisWidth = 20;
        string _builtKey;
        IVisualElementScheduledItem _measure; // the one pending Measure; replaced on every (re)schedule
        int _measureTries;
        const int MaxMeasureTries = 30;       // ~0.5 s of 16 ms retries; later layout changes re-measure via geometry

        // Collapse is decided in the same frame as a rebuild (no frame with the full, uncollapsed path): segment widths
        // come from earlier measurements, else from text measured on laid-out hidden probes; Measure then corrects them.
        readonly Dictionary<string, float> _widthCache = new Dictionary<string, float>(); // name + bold flag -> segment width
        const int MaxWidthCache = 512;
        readonly Button _probe, _boldProbe, _arrowProbe;
        float _arrowWidth = -1;

        /// <summary>Raised with a folder path when a name or a subfolder menu entry is chosen.</summary>
        public event Action<string> Navigate;

        public Breadcrumbs()
        {
            name = "breadcrumbs";
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 18;
            style.flexShrink = 0;
            // Splitter between the path bar and the list, as in Unity's Project window.
            style.borderBottomWidth = 1;
            style.borderBottomColor = Theme.Separator;
            style.overflow = Overflow.Hidden;
            style.minWidth = 0;
            style.paddingLeft = 4;
            _row = new VisualElement();
            _row.style.flexDirection = FlexDirection.Row;
            _row.style.alignItems = Align.Center;
            _row.style.flexShrink = 0;
            Add(_row);
            _ellipsis = MakeButton(Ellipsis, ShowHiddenMenu);
            _ellipsis.tooltip = "Show hidden parent folders";
            SetEllipsisShown(false);
            _probe = MakeProbe(false);
            _boldProbe = MakeProbe(true);
            _arrowProbe = MakeProbe(false);
            StyleArrow(_arrowProbe);
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (Mathf.Abs(e.newRect.width - e.oldRect.width) <= 0.5f) return;
                if (_segments.Count > 0 && _widths.Count != _segments.Count) ScheduleMeasure(0); // never measured yet
                else ApplyCollapse();
            });
        }

        /// <summary>Shows the path of a folder. "Packages" is a virtual root; package roots show display names.</summary>
        public void SetFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) { SetSearch(); return; }
            var crumbs = new List<(string Name, string Path)>();
            if (folderPath == PackagesRoot || folderPath.StartsWith(PackagesRoot + "/", StringComparison.Ordinal))
            {
                crumbs.Add((PackagesRoot, PackagesRoot));
                var parts = AssetPaths.Breadcrumbs(folderPath);
                IReadOnlyList<(string DisplayName, string Path)> pkgs = null;
                for (int i = 1; i < parts.Count; i++)
                {
                    string display = parts[i].Name;
                    if (i == 1)
                    {
                        pkgs = PackageFolders.All(); // once per refresh
                        foreach (var p in pkgs) if (p.Path == parts[i].Path) { display = p.DisplayName; break; }
                    }
                    crumbs.Add((display, parts[i].Path));
                }
            }
            else crumbs.AddRange(AssetPaths.Breadcrumbs(folderPath));

            string key = string.Join("\n", crumbs.ConvertAll(c => c.Name + "|" + c.Path));
            if (key == _builtKey) return;
            _builtKey = key;
            _crumbs = crumbs;
            Rebuild();
        }

        /// <summary>Search / object-list mode: a single non-clickable label.</summary>
        public void SetSearch() => SetSearch("Search results");

        public void SetSearch(string label)
        {
            string key = "\u0001" + label;
            if (key == _builtKey) return;
            _builtKey = key;
            _crumbs = new List<(string Name, string Path)>();
            _measure?.Pause();
            _segments.Clear();
            _widths.Clear();
            _hidden = -1;
            _row.Clear();
            var l = new Label(label);
            l.pickingMode = PickingMode.Ignore;
            l.style.unityFontStyleAndWeight = FontStyle.Italic;
            l.style.paddingLeft = 2;
            _row.Add(l);
        }

        void Rebuild()
        {
            _row.Clear();
            _segments.Clear();
            _widths.Clear();
            _hidden = -1;
            _row.Add(_ellipsis);
            SetEllipsisShown(false);
            for (int i = 0; i < _crumbs.Count; i++)
            {
                var c = _crumbs[i];
                var seg = new VisualElement();
                seg.style.flexDirection = FlexDirection.Row;
                seg.style.alignItems = Align.Center;
                seg.style.flexShrink = 0;
                var folder = MakeButton(c.Name, () => Navigate?.Invoke(c.Path));
                // The current folder is bold, as in Unity's Project window.
                if (i == _crumbs.Count - 1) folder.style.unityFontStyleAndWeight = FontStyle.Bold;
                seg.Add(folder);
                Button arrow = null;
                arrow = MakeButton(Arrow, () => ShowSubfolderMenu(c.Path, arrow));
                arrow.tooltip = "Subfolders";
                StyleArrow(arrow);
                seg.Add(arrow);
                _row.Add(seg);
                _segments.Add(seg);
            }
            // Known / estimated widths: collapse now, before the first layout. Then measure the real widths once laid
            // out; later width changes reuse them.
            if (TryKnownWidths()) ApplyCollapse();
            ScheduleMeasure(0);
        }

        static string WidthKey(string name, bool bold) => (bold ? "b|" : "r|") + name;

        bool TryKnownWidths()
        {
            _widths.Clear();
            for (int i = 0; i < _crumbs.Count; i++)
            {
                string name = _crumbs[i].Name;
                bool bold = i == _crumbs.Count - 1;
                if (!_widthCache.TryGetValue(WidthKey(name, bold), out float w) && !TryEstimateWidth(name, bold, out w))
                {
                    _widths.Clear();
                    return false;
                }
                _widths.Add(w);
            }
            return true;
        }

        // Button text width (via a laid-out probe with the same style) + its padding + the arrow button.
        bool TryEstimateWidth(string name, bool bold, out float width)
        {
            width = 0;
            var probe = bold ? _boldProbe : _probe;
            if (float.IsNaN(probe.layout.width) || probe.layout.width <= 0 || float.IsNaN(_arrowProbe.layout.width)) return false;
            float arrow = _arrowWidth > 0 ? _arrowWidth : ButtonWidth(_arrowProbe, Arrow); // not measured yet: estimate too
            width = ButtonWidth(probe, name) + arrow;
            return !float.IsNaN(width) && width > arrow;
        }

        static float ButtonWidth(Button probe, string text) =>
            Mathf.Ceil(probe.MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x
                       + probe.resolvedStyle.paddingLeft + probe.resolvedStyle.paddingRight);

        // The subfolder arrow: smaller and tighter than the folder names.
        static void StyleArrow(Button arrow)
        {
            arrow.style.color = Theme.DimText;
            arrow.style.fontSize = 11;
            arrow.style.paddingLeft = 0;
            arrow.style.paddingRight = 0;
        }

        Button MakeProbe(bool bold)
        {
            var b = MakeButton("M", null);
            if (bold) b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.position = Position.Absolute; // takes no space in the bar
            b.style.visibility = Visibility.Hidden;
            b.pickingMode = PickingMode.Ignore;
            Add(b);
            return b;
        }

        // Hidden, the ellipsis stays laid out (absolute, invisible), so its width is always known without a frame
        // in which it shows.
        void SetEllipsisShown(bool shown)
        {
            _ellipsis.style.position = shown ? Position.Relative : Position.Absolute;
            _ellipsis.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;
            _ellipsis.pickingMode = shown ? PickingMode.Position : PickingMode.Ignore;
        }

        // One measure chain at a time: a new schedule (Rebuild, geometry change) replaces the pending one.
        void ScheduleMeasure(long delayMs)
        {
            _measure?.Pause();
            _measureTries = 0;
            _measure = _row.schedule.Execute(Measure);
            _measure.ExecuteLater(delayMs);
        }

        void Measure()
        {
            if (_segments.Count == 0) return;
            var known = new List<float>(_widths); // widths collapse was decided with (hidden segments keep these)
            _widths.Clear();
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                float w = s.layout.width;
                if (s.resolvedStyle.display == DisplayStyle.None && known.Count == _segments.Count) w = known[i];
                if (float.IsNaN(w) || w <= 0)
                {
                    // Not laid out yet (e.g. hidden tab): retry briefly, then wait for a GeometryChangedEvent.
                    // Keep the widths collapse was decided with: the retry needs them for the hidden segments.
                    _widths.Clear();
                    if (known.Count == _segments.Count) _widths.AddRange(known);
                    if (++_measureTries > MaxMeasureTries) return;
                    _measure = _row.schedule.Execute(Measure);
                    _measure.ExecuteLater(16);
                    return;
                }
                _widths.Add(w);
            }
            if (_widthCache.Count > MaxWidthCache) _widthCache.Clear();
            for (int i = 0; i < _segments.Count && i < _crumbs.Count; i++)
                if (_segments[i].resolvedStyle.display != DisplayStyle.None) // only real measurements
                    _widthCache[WidthKey(_crumbs[i].Name, i == _crumbs.Count - 1)] = _widths[i];
            var last = _segments[_segments.Count - 1]; // never collapsed
            var arrow = last.childCount > 1 ? last[1] : null;
            if (arrow != null && arrow.layout.width > 0) _arrowWidth = arrow.layout.width;
            float ew = _ellipsis.layout.width;
            if (!float.IsNaN(ew) && ew > 0) _ellipsisWidth = ew;
            _hidden = -1;
            ApplyCollapse();
        }

        void ApplyCollapse()
        {
            if (_widths.Count != _segments.Count || _segments.Count == 0) return;
            float available = layout.width - resolvedStyle.paddingLeft - 2;
            if (float.IsNaN(available) || available <= 0) return;
            int hidden = BreadcrumbCollapse.HiddenCount(_widths, available, _ellipsisWidth);
            if (hidden == _hidden) return;
            _hidden = hidden;
            SetEllipsisShown(hidden > 0);
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].style.display = i < hidden ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static Button MakeButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.marginLeft = 0;
            b.style.marginRight = 0;
            b.style.marginTop = 0;
            b.style.marginBottom = 0;
            b.style.paddingLeft = 3;
            b.style.paddingRight = 3;
            b.style.paddingTop = 0;
            b.style.paddingBottom = 0;
            b.style.borderTopWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0;
            b.style.borderRightWidth = 0;
            b.style.backgroundColor = Color.clear;
            b.style.flexShrink = 0;
            return b;
        }

        void ShowHiddenMenu()
        {
            var menu = new GenericMenu();
            for (int i = 0; i < _hidden && i < _crumbs.Count; i++)
            {
                string path = _crumbs[i].Path;
                menu.AddItem(new GUIContent(_crumbs[i].Name), false, () => Navigate?.Invoke(path));
            }
            menu.DropDown(_ellipsis.worldBound);
        }

        void ShowSubfolderMenu(string folderPath, VisualElement anchor)
        {
            var entries = new List<(string Name, string Path)>();
            if (folderPath == PackagesRoot)
            {
                foreach (var p in PackageFolders.All()) entries.Add((p.DisplayName, p.Path));
            }
            else if (AssetDatabase.IsValidFolder(folderPath))
            {
                foreach (var sub in AssetDatabase.GetSubFolders(folderPath))
                    entries.Add((sub.Substring(sub.LastIndexOf('/') + 1), sub));
            }
            entries.Sort((a, b) =>
            {
                int c = EditorUtility.NaturalCompare(a.Name, b.Name);
                return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path);
            });
            var menu = new GenericMenu();
            if (entries.Count == 0) menu.AddDisabledItem(new GUIContent("No subfolders"));
            foreach (var e in entries)
            {
                string path = e.Path;
                // '/' in a GenericMenu label would create a submenu.
                menu.AddItem(new GUIContent(e.Name.Replace('/', '∕')), false, () => Navigate?.Invoke(path));
            }
            menu.DropDown(anchor.worldBound);
        }
    }
}
