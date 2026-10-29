using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BetterProject.Editor
{
    /// <summary>
    /// Temporary diagnostics: a moved file stays in the content view until the folder is reopened (seen only in a
    /// large project). Logs the drop, projectChanged, the refresh and what the content view gets. Remove when fixed.
    /// </summary>
    static class MoveDebug
    {
        public static void Log(string message) =>
            Debug.Log("[Better Project debug] f" + Time.frameCount + " " + message);

        public static string Paths(IReadOnlyList<ContentItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(items.Count).Append(" items [");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(items[i].Name);
            }
            return sb.Append(']').ToString();
        }
    }

    class MoveDebugPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (movedAssets.Length == 0 && deletedAssets.Length == 0) return;
            MoveDebug.Log("postprocess: moved=" + string.Join("|", movedAssets) + " from=" + string.Join("|", movedFromAssetPaths)
                          + " deleted=" + string.Join("|", deletedAssets));
        }
    }
}
