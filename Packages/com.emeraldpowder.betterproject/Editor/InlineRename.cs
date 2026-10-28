using System;
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
    /// The owning view re-hosts the field via <see cref="Attach"/> / <see cref="Detach"/> as rows are rebound.
    /// </summary>
    public class InlineRename
    {
        readonly TextField _field;
        ContentItem _item;
        Action<string> _onCommit;
        Action _onCancel;
        bool _ending;
        bool _selectAllOnAttach;
        VisualElement _hiddenLabel;

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
            _item = item;
            _onCommit = onCommit;
            _onCancel = onCancel;
            IsActive = true;
            _field.SetValueWithoutNotify(item.Name ?? "");
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
            if (_field.parent == row && !selectAll) return;
            _selectAllOnAttach = false;
            if (_field.parent != row)
            {
                RestoreLabel();
                _field.RemoveFromHierarchy();
                var label = row.Q<Label>("label");
                if (label != null)
                {
                    label.style.display = DisplayStyle.None;
                    _hiddenLabel = label;
                }
                row.Add(_field);
            }
            _field.schedule.Execute(() =>
            {
                if (!IsActive || _field.panel == null) return;
                _field.Focus();
                if (selectAll) _field.SelectAll();
            });
        }

        /// <summary>Removes the field from <paramref name="row"/> if it is hosted there (row rebound to another item).</summary>
        public void Detach(VisualElement row)
        {
            if (row == null || _field.parent != row) return;
            RestoreLabel();
            _field.RemoveFromHierarchy();
        }

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
            // Real focus loss commits, like Unity's RenameOverlay.
            _field.schedule.Execute(() =>
            {
                if (!IsActive || _field.panel == null) return;
                if (_field.focusController != null && _field.focusController.focusedElement == _field) return;
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
            string newName = Clean(_field.value);
            IsActive = false;
            _onCommit = null;
            _onCancel = null;
            RestoreLabel();
            _field.RemoveFromHierarchy();
            Action run = () =>
            {
                try
                {
                    if (onCommit != null || onCancel != null)
                    {
                        if (accept) onCommit?.Invoke(newName);
                        else onCancel?.Invoke();
                    }
                    else if (accept && newName.Length > 0 && newName != item.Name)
                    {
                        string error = AssetDatabase.RenameAsset(item.Path, newName);
                        if (!string.IsNullOrEmpty(error))
                            Debug.LogWarning("[Better Project] Rename of '" + item.Path + "' to '" + newName + "' failed: " + error);
                    }
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

        static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return InternalEditorUtility.RemoveInvalidCharsFromFileName(name.Trim(), true).Trim();
        }
    }
}
