using System;
using BetterProject.Core;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>
    /// Inline text field that replaces a row's label while renaming (Unity RenameOverlay semantics): Enter and focus loss
    /// commit, Esc cancels. Default commit: AssetDatabase.RenameAsset; a non-empty error is logged as a warning and the
    /// name reverts; an empty or unchanged name does nothing.
    /// With "Show file extensions" on, the field holds the name with its extension and preselects only the name;
    /// a typed different extension moves the file to it, a removed one keeps the original.
    /// The owning view re-hosts the field via <see cref="Attach"/> / <see cref="Detach"/> as rows are rebound.
    /// </summary>
    public class InlineRename
    {
        readonly TextField _field;
        ContentItem _item;
        Action<string> _onCommit;
        Action _onCancel;
        bool _ending;
        int _session; // bumped on every Begin: callbacks scheduled for an earlier edit must not touch a newer one
        bool _selectAllOnAttach;
        string _extension; // shown after the name in the field; null when extensions are hidden
        VisualElement _hiddenLabel;
        // The part of the field holding keyboard focus (its text input), tracked from focus events: inside a ListView,
        // focusController.focusedElement reports the ListView instead.
        Focusable _focusedPart;

        public bool IsActive { get; private set; }
        /// <summary>Path of the item being renamed (null when inactive).</summary>
        public string Path => IsActive ? _item.Path : null;
        public event Action Ended;

        public InlineRename()
        {
            _field = new TextField { name = "rename-field", isDelayed = false, selectAllOnFocus = false };
            _field.style.flexGrow = 1;
            _field.style.flexShrink = 1;
            _field.style.marginLeft = 0;
            _field.style.marginRight = 2;
            _field.style.marginTop = 0;
            _field.style.marginBottom = 0;
            _field.style.height = 16;
            _field.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusInEvent>(e => _focusedPart = e.target as Focusable, TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusOutEvent>(e => { if (e.target == _focusedPart) _focusedPart = null; }, TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusOutEvent>(OnFocusOut);
        }

        public bool Contains(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent) if (e == _field) return true;
            return false;
        }

        /// <summary>Starts renaming <paramref name="item"/>; <paramref name="row"/> may be null (attached when bound).</summary>
        public void Begin(ContentItem item, VisualElement row) => Begin(item, row, null, null);

        /// <summary>
        /// Starts an edit with custom handlers: <paramref name="onCommit"/> gets the cleaned new name (may be empty) on
        /// Enter / focus loss / <see cref="Commit"/>; <paramref name="onCancel"/> runs on Esc / <see cref="Cancel"/>.
        /// </summary>
        public void Begin(ContentItem item, VisualElement row, Action<string> onCommit, Action onCancel)
        {
            if (IsActive) Commit(); // starting another edit = focus moved away
            _session++;
            _field.RemoveFromHierarchy(); // the previous edit's field may still wait for its removal
            _field.style.display = DisplayStyle.Flex;
            _item = item;
            _onCommit = onCommit;
            _onCancel = onCancel;
            IsActive = true;
            _extension = AssetRefUtil.ShowsExtension(item.IsFolder, item.IsSubAsset) ? AssetPaths.Extension(item.Path) : null;
            _field.SetValueWithoutNotify((item.Name ?? "") + (_extension ?? ""));
            _selectAllOnAttach = true;
            if (row != null) Attach(row);
        }

        public void Commit() => End(true, false);
        public void Cancel() => End(false, false);

        /// <summary>
        /// Commits, but runs the commit handler on the next editor tick (EditorApplication.delayCall) — for teardown
        /// paths such as window close, where AssetDatabase writes are not safe yet.
        /// </summary>
        public void CommitDeferred() => End(true, true);

        /// <summary>Hosts the field in <paramref name="row"/> (the row currently bound to the renamed item).</summary>
        public void Attach(VisualElement row)
        {
            if (!IsActive || row == null) return;
            bool selectAll = _selectAllOnAttach;
            bool hosted = IsHostedIn(row);
            if (hosted && !selectAll) return;
            _selectAllOnAttach = false;
            int session = _session;
            if (!hosted)
            {
                RestoreLabel();
                _field.RemoveFromHierarchy();
                var label = row.Q<Label>("label");
                if (label != null)
                {
                    // In the label's place (details rows have more columns after it).
                    label.style.display = DisplayStyle.None;
                    _hiddenLabel = label;
                    label.parent.Insert(label.parent.IndexOf(label) + 1, _field);
                }
                else row.Add(_field);
            }
            _field.schedule.Execute(() =>
            {
                if (!IsActive || session != _session || _field.panel == null) return;
                _field.Focus();
                if (!selectAll) return;
                if (_extension == null) _field.SelectAll();
                else _field.SelectRange(_field.value.Length - _extension.Length, 0); // the name only, caret at its end
            });
        }

        /// <summary>Removes the field from <paramref name="row"/> if it is hosted there (row rebound to another item).</summary>
        public void Detach(VisualElement row)
        {
            if (row == null || !IsHostedIn(row)) return;
            RestoreLabel();
            _field.RemoveFromHierarchy();
        }

        // The field, or the text input it delegates focus to, has keyboard focus.
        bool HasFocus() => _focusedPart != null;

        bool IsHostedIn(VisualElement row) => _field.parent != null && (_field.parent == row || row.Contains(_field));

        void RestoreLabel()
        {
            if (_hiddenLabel != null) _hiddenLabel.style.display = DisplayStyle.Flex;
            _hiddenLabel = null;
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (!IsActive) return;
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopImmediatePropagation();
                Commit();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopImmediatePropagation();
                Cancel();
            }
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (!IsActive) return;
            // Decide next frame: re-hosting the field (row rebinding) also blurs it, and a detached field is not "focus out".
            // Real focus loss commits, like Unity's RenameOverlay. Only for this edit: the field removed by Esc / Enter
            // also blurs, and a new edit started before this runs (F2 right after Esc) must not be committed by it.
            int session = _session;
            _field.schedule.Execute(() =>
            {
                if (!IsActive || session != _session || _field.panel == null) return;
                if (HasFocus()) return;
                Commit();
            });
        }

        void End(bool accept, bool deferHandlers)
        {
            if (!IsActive || _ending) return;
            _ending = true;
            var item = _item;
            var onCommit = _onCommit;
            var onCancel = _onCancel;
            var extension = _extension;
            string newName = Clean(_field.value);
            IsActive = false;
            _onCommit = null;
            _onCancel = null;
            RestoreLabel();
            // Blurred while still in the panel, removed on the next tick: a field removed while focused (inside its own
            // key event) leaves the editor swallowing keys before they reach the panel, so Enter / arrows do nothing in
            // the window until the next click.
            _focusedPart?.Blur();
            _field.style.display = DisplayStyle.None;
            int ended = _session;
            if (_field.panel != null) _field.schedule.Execute(() => { if (_session == ended) _field.RemoveFromHierarchy(); });
            else _field.RemoveFromHierarchy();
            Action run = () =>
            {
                try
                {
                    if (onCommit != null || onCancel != null)
                    {
                        if (accept) onCommit?.Invoke(WithExtension(newName, extension));
                        else onCancel?.Invoke();
                    }
                    else if (accept) RenameAsset(item, newName, extension);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            };
            try
            {
                if (deferHandlers) EditorApplication.delayCall += () => run();
                else run();
            }
            finally
            {
                _ending = false;
                Ended?.Invoke();
            }
        }

        // Typed name as the commit handler gets it: the original extension added back if the user removed it,
        // "" when only an extension is left (the handler's empty name = default name).
        static string WithExtension(string typed, string extension)
        {
            if (extension == null) return typed;
            var (name, ext) = AssetPaths.SplitTypedFileName(typed, extension);
            return name.Length == 0 ? "" : name + ext;
        }

        static void RenameAsset(ContentItem item, string typed, string extension)
        {
            string name = typed, ext = extension;
            if (extension != null) (name, ext) = AssetPaths.SplitTypedFileName(typed, extension);
            if (name.Length == 0) return;
            string error;
            if (extension == null || string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase))
            {
                if (name == item.Name) return;
                error = AssetDatabase.RenameAsset(item.Path, name);
            }
            else error = AssetDatabase.MoveAsset(item.Path, AssetPaths.Parent(item.Path) + "/" + name + ext); // extension changed
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[Better Project] Rename of '" + item.Path + "' to '" + name + ext + "' failed: " + error);
        }

        static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return InternalEditorUtility.RemoveInvalidCharsFromFileName(name.Trim(), true).Trim();
        }
    }
}
