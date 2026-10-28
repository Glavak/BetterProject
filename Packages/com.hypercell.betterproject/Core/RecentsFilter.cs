namespace BetterProject.Core
{
    public static class RecentsFilter
    {
        /// <summary>True for the windows whose edits count as the user editing an asset (Inspector, Properties).</summary>
        public static bool IsEditWindow(string focusedWindowTypeName)
        {
            return focusedWindowTypeName == "UnityEditor.InspectorWindow" || focusedWindowTypeName == "UnityEditor.PropertyEditor";
        }

        public static bool ShouldRecordEdit(string focusedWindowTypeName, string assetPath)
        {
            return IsEditWindow(focusedWindowTypeName) && AssetPaths.IsProjectAssetPath(assetPath);
        }
    }
}
