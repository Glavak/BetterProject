using System;
using System.Collections.Generic;

namespace BetterProject.Core
{
    public static class AssetPaths
    {
        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = path.LastIndexOf('/');
            return i < 0 ? "" : path.Substring(0, i);
        }

        public static IReadOnlyList<(string Name, string Path)> Breadcrumbs(string folderPath)
        {
            var result = new List<(string Name, string Path)>();
            if (string.IsNullOrEmpty(folderPath)) return result;
            int pos = 0;
            while (pos <= folderPath.Length)
            {
                int next = folderPath.IndexOf('/', pos);
                int end = next < 0 ? folderPath.Length : next;
                if (end > pos) result.Add((folderPath.Substring(pos, end - pos), folderPath.Substring(0, end)));
                if (next < 0) break;
                pos = next + 1;
            }
            return result;
        }

        public static bool IsProjectAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            return path.StartsWith("Packages/", StringComparison.Ordinal) && path.Length > "Packages/".Length;
        }

        public static bool CanMoveInto(string sourcePath, string targetFolder)
        {
            if (!IsProjectAssetPath(sourcePath) || !IsProjectAssetPath(targetFolder)) return false;
            if (string.Equals(sourcePath, targetFolder, StringComparison.OrdinalIgnoreCase)) return false;
            if (targetFolder.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(Parent(sourcePath), targetFolder, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
