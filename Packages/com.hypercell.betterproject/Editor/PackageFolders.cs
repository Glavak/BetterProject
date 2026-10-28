using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;

namespace BetterProject.Editor
{
    public static class PackageFolders
    {
        public static IReadOnlyList<(string DisplayName, string Path)> All()
        {
            var list = new List<(string DisplayName, string Path)>();
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            if (packages != null)
            {
                foreach (var p in packages)
                {
                    string path = "Packages/" + p.name;
                    if (!AssetDatabase.IsValidFolder(path)) continue;
                    list.Add((string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName, path));
                }
            }
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path);
            });
            return list;
        }

        public static string DisplayNameFor(string packageRootPath)
        {
            foreach (var p in All())
                if (p.Path == packageRootPath) return p.DisplayName;
            int i = packageRootPath.LastIndexOf('/');
            return i < 0 ? packageRootPath : packageRootPath.Substring(i + 1);
        }
    }
}
