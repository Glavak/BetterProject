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

        /// <summary>
        /// True if a file or folder with this name is an asset Unity imports: not a .meta file and not one of the names
        /// Unity hides (starting with '.', ending with '~', "cvs", "*.tmp").
        /// </summary>
        public static bool IsImportedName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] == '.' || name[name.Length - 1] == '~') return false;
            if (string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return false;
            return !name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Extension of the path's last segment including the dot (".png"), or "" if it has none.</summary>
        public static string Extension(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int start = path.LastIndexOf('/') + 1;
            int dot = path.LastIndexOf('.');
            if (dot <= start || dot == path.Length - 1) return "";
            return path.Substring(dot);
        }

        /// <summary>
        /// Splits a file name typed in rename while extensions are shown into the base name and the extension the
        /// file should end up with. Typing no extension keeps <paramref name="originalExtension"/>; the original one
        /// in another case counts as unchanged.
        /// </summary>
        public static (string Name, string Extension) SplitTypedFileName(string typed, string originalExtension)
        {
            typed = typed ?? "";
            originalExtension = originalExtension ?? "";
            if (originalExtension.Length > 0 && typed.EndsWith(originalExtension, StringComparison.OrdinalIgnoreCase))
                return (typed.Substring(0, typed.Length - originalExtension.Length), originalExtension);
            string ext = Extension(typed);
            if (ext.Length == 0) return (typed, originalExtension);
            return (typed.Substring(0, typed.Length - ext.Length), ext);
        }
    }
}
