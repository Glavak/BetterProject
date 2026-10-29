using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>Sizes and colours shared by the panels, matched to Unity's Project window.</summary>
    static class Theme
    {
        /// <summary>Row height of list entries, tree rows and section rows (Unity's Project window uses 16).</summary>
        public const float RowHeight = 16f;

        /// <summary>Gap between the left panel's sections (Favourites, Recents, folder tree): the "Sidebar spacing" setting.</summary>
        public static float SectionSpacing => BetterProjectSettings.instance.SidebarSpacing;

        /// <summary>Background of the right (content) panel: a shade apart from the window background.</summary>
        public static Color ContentBackground => EditorGUIUtility.isProSkin
            ? new Color(0.19f, 0.19f, 0.19f, 1f)
            : new Color(0.745f, 0.745f, 0.745f, 1f);

        /// <summary>Unity's default editor window background (the left panel shows it).</summary>
        public static Color WindowBackground => EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f, 1f)
            : new Color(0.784f, 0.784f, 0.784f, 1f);

        /// <summary>Thin separator lines, e.g. under the breadcrumbs.</summary>
        public static Color Separator => EditorGUIUtility.isProSkin
            ? new Color(0.1f, 0.1f, 0.1f, 1f)
            : new Color(0.6f, 0.6f, 0.6f, 1f);

        /// <summary>Class that turns off the hover highlight inside an element (see BetterProject.uss).</summary>
        public const string NoHoverClass = "bp-no-hover";

        /// <summary>
        /// Class on the right panel in the dark skin: the theme's row hover colour equals ContentBackground there, so
        /// BetterProject.uss gives rows / tiles the Hierarchy's hover colour instead.
        /// </summary>
        public const string DarkContentClass = "bp-content-dark";

        static StyleSheet s_StyleSheet;

        /// <summary>The package's BetterProject.uss, found by name so it works wherever the package is installed.</summary>
        public static StyleSheet StyleSheet
        {
            get
            {
                if (s_StyleSheet != null) return s_StyleSheet;
                foreach (var guid in AssetDatabase.FindAssets("BetterProject t:StyleSheet"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/BetterProject.uss", System.StringComparison.Ordinal)) continue;
                    s_StyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (s_StyleSheet != null) break;
                }
                return s_StyleSheet;
            }
        }

        public static Color DimText => EditorGUIUtility.isProSkin
            ? new Color(0.6f, 0.6f, 0.6f, 1f)
            : new Color(0.35f, 0.35f, 0.35f, 1f);
    }
}
