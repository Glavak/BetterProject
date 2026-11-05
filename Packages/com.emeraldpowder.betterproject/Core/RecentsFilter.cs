namespace BetterProject.Core
{
    public static class RecentsFilter
    {
        /// <summary>True for the Inspector and the Properties window.</summary>
        public static bool IsInspector(string windowTypeName)
        {
            return windowTypeName == "UnityEditor.InspectorWindow" || windowTypeName == "UnityEditor.PropertyEditor";
        }

        /// <summary>
        /// True for the windows whose edits count as the user editing an asset: the Inspector / Properties and the
        /// pickers they open for a field (object picker, color, gradient, curve), which take focus while editing.
        /// </summary>
        public static bool IsEditWindow(string focusedWindowTypeName)
        {
            return IsInspector(focusedWindowTypeName)
                   || focusedWindowTypeName == "UnityEditor.ObjectSelector"
                   || focusedWindowTypeName == "UnityEditor.ColorPicker"
                   || focusedWindowTypeName == "UnityEditor.GradientPicker"
                   || focusedWindowTypeName == "UnityEditor.CurveEditorWindow";
        }

        /// <summary>
        /// True if an edit happens in an edit window: the focused one, or the Inspector under the mouse (a drag and drop
        /// onto an Inspector field leaves focus on the window the drag started in).
        /// </summary>
        public static bool IsEditContext(string focusedWindowTypeName, string mouseOverWindowTypeName)
        {
            return IsEditWindow(focusedWindowTypeName) || IsInspector(mouseOverWindowTypeName);
        }

        public static bool ShouldRecordEdit(string focusedWindowTypeName, string assetPath) =>
            ShouldRecordEdit(focusedWindowTypeName, null, assetPath);

        public static bool ShouldRecordEdit(string focusedWindowTypeName, string mouseOverWindowTypeName, string assetPath)
        {
            return IsEditContext(focusedWindowTypeName, mouseOverWindowTypeName) && AssetPaths.IsProjectAssetPath(assetPath);
        }
    }
}
